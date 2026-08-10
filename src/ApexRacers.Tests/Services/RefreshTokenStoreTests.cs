using System.Security.Cryptography;
using System.Text;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ApexRacers.Tests.Services;

public class RefreshTokenStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 9, 16, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task IssueAsync_PersistsOnlyTheHashAndUsesTheCanonicalLifetime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = DbContextFactory.CreateInMemory();
        var store = new RefreshTokenStore(db, new TestTimeProvider(Now));
        var userId = Guid.NewGuid();

        var rawToken = await store.IssueAsync(userId, ct);

        var stored = await db.RefreshTokens.SingleAsync(ct);
        Assert.Equal(64, Convert.FromBase64String(rawToken).Length);
        Assert.NotEqual(rawToken, stored.TokenHash);
        Assert.Equal(Hash(rawToken), stored.TokenHash);
        Assert.Equal(userId, stored.UserId);
        Assert.Equal(Now, stored.CreatedAt);
        Assert.Equal(Now.AddDays(7), stored.ExpiresAt);
        Assert.Null(stored.RevokedAt);
    }

    [Fact]
    public async Task RotateAsync_ExpiresExactlyNow_IsRejectedWithTheExistingMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = DbContextFactory.CreateInMemory();
        const string rawToken = "expires-at-the-boundary";
        db.RefreshTokens.Add(Token(
            Guid.NewGuid(), rawToken, Now.AddDays(-7), expiresAt: Now));
        await db.SaveChangesAsync(ct);
        var store = new RefreshTokenStore(db, new TestTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RotateAsync(rawToken, ct));

        Assert.Equal("Invalid or expired refresh token.", exception.Message);
        Assert.Null((await db.RefreshTokens.SingleAsync(ct)).RevokedAt);
    }

    [Fact]
    public async Task RotateAsync_RevokesAndInsertsInOneSaveAndDoesNotPersistTheRawReplacement()
    {
        var ct = TestContext.Current.CancellationToken;
        var probe = new SaveShapeProbe();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(probe)
            .Options;
        await using var db = new AppDbContext(options);
        var clock = new TestTimeProvider(Now);
        var store = new RefreshTokenStore(db, clock);
        var userId = Guid.NewGuid();
        var originalRaw = await store.IssueAsync(userId, ct);
        var original = await db.RefreshTokens.SingleAsync(ct);
        probe.Snapshots.Clear();
        clock.SetUtcNow(Now.AddHours(1));

        var rotation = await store.RotateAsync(originalRaw, ct);

        var snapshot = Assert.Single(probe.Snapshots);
        Assert.Equal(1, snapshot.Added);
        Assert.Equal(1, snapshot.Modified);
        Assert.Equal(userId, rotation.UserId);
        Assert.NotEqual(originalRaw, rotation.RawToken);
        Assert.Equal(clock.GetUtcNow(), original.RevokedAt);
        var replacement = await db.RefreshTokens.SingleAsync(
            token => token.Id != original.Id, ct);
        Assert.Equal(Hash(rotation.RawToken), replacement.TokenHash);
        Assert.NotEqual(rotation.RawToken, replacement.TokenHash);
        Assert.Equal(clock.GetUtcNow(), replacement.CreatedAt);
        Assert.Equal(clock.GetUtcNow().AddDays(7), replacement.ExpiresAt);
    }

    [Fact]
    public async Task IssueAsync_AtCap_RevokesTheOldestCanonicalActiveToken()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = DbContextFactory.CreateInMemory();
        var userId = Guid.NewGuid();
        var tokens = Enumerable.Range(0, 5)
            .Select(index => Token(
                userId,
                $"active-{index}",
                Now.AddMinutes(-10 + index),
                Now.AddDays(1)))
            .ToList();
        db.RefreshTokens.AddRange(tokens);
        await db.SaveChangesAsync(ct);
        var store = new RefreshTokenStore(db, new TestTimeProvider(Now));

        await store.IssueAsync(userId, ct);

        Assert.Equal(6, await db.RefreshTokens.CountAsync(ct));
        Assert.Equal(Now, tokens[0].RevokedAt);
        Assert.All(tokens.Skip(1), token => Assert.Null(token.RevokedAt));
        Assert.Equal(5, await db.RefreshTokens.CountAsync(
            token => token.RevokedAt == null && token.ExpiresAt > Now, ct));
    }

    [Fact]
    public async Task RotateAsync_AtCap_IsExemptAndKeepsFiveActiveTokens()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = DbContextFactory.CreateInMemory();
        var clock = new TestTimeProvider(Now);
        var store = new RefreshTokenStore(db, clock);
        var userId = Guid.NewGuid();
        var rawTokens = new List<string>();
        for (var index = 0; index < 5; index++)
        {
            clock.SetUtcNow(Now.AddSeconds(index));
            rawTokens.Add(await store.IssueAsync(userId, ct));
        }
        clock.SetUtcNow(Now.AddMinutes(1));

        await store.RotateAsync(rawTokens[2], ct);

        Assert.Equal(6, await db.RefreshTokens.CountAsync(ct));
        Assert.Equal(1, await db.RefreshTokens.CountAsync(
            token => token.RevokedAt != null, ct));
        Assert.Equal(5, await db.RefreshTokens.CountAsync(
            token => token.RevokedAt == null && token.ExpiresAt > clock.GetUtcNow(), ct));
    }

    [Fact]
    public async Task RevokeAllActiveAsync_LeavesExpiredAndPreviouslyRevokedRowsUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = DbContextFactory.CreateInMemory();
        var userId = Guid.NewGuid();
        var previouslyRevokedAt = Now.AddDays(-2);
        var active = Token(userId, "active", Now.AddDays(-1), Now.AddDays(1));
        var expired = Token(userId, "expired", Now.AddDays(-8), Now.AddSeconds(-1));
        var revoked = Token(
            userId, "revoked", Now.AddDays(-2), Now.AddDays(1), previouslyRevokedAt);
        var otherUser = Token(
            Guid.NewGuid(), "other-user", Now.AddDays(-1), Now.AddDays(1));
        db.RefreshTokens.AddRange(active, expired, revoked, otherUser);
        await db.SaveChangesAsync(ct);
        var store = new RefreshTokenStore(db, new TestTimeProvider(Now));

        await store.RevokeAllActiveAsync(userId, ct);

        Assert.Equal(Now, active.RevokedAt);
        Assert.Null(expired.RevokedAt);
        Assert.Equal(previouslyRevokedAt, revoked.RevokedAt);
        Assert.Null(otherUser.RevokedAt);

        await store.RevokeAllActiveAsync(userId, ct);
        Assert.Equal(Now, active.RevokedAt);
    }

    [Fact]
    public async Task RevokeAsync_IsBestEffortAndCanStampAnExpiredPresentedCredential()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = DbContextFactory.CreateInMemory();
        var clock = new TestTimeProvider(Now);
        const string expiredRaw = "expired-presented-token";
        var expired = Token(
            Guid.NewGuid(), expiredRaw, Now.AddDays(-8), Now.AddSeconds(-1));
        db.RefreshTokens.Add(expired);
        await db.SaveChangesAsync(ct);
        var store = new RefreshTokenStore(db, clock);

        await store.RevokeAsync("unknown-token", ct);
        Assert.Null(expired.RevokedAt);

        await store.RevokeAsync(expiredRaw, ct);
        Assert.Equal(Now, expired.RevokedAt);

        clock.SetUtcNow(Now.AddHours(1));
        await store.RevokeAsync(expiredRaw, ct);
        Assert.Equal(Now, expired.RevokedAt);
    }

    [Fact]
    public async Task PurgeExpiredAsync_RemovesOnlyRowsOlderThanTheRetentionBoundary()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = DbContextFactory.CreateInMemory();
        var userId = Guid.NewGuid();
        var beyondRetention = Token(
            userId, "too-old", Now.AddDays(-50), Now.AddDays(-31));
        var atBoundary = Token(
            userId, "at-boundary", Now.AddDays(-40), Now.AddDays(-30));
        var withinRetention = Token(
            userId, "within", Now.AddDays(-20), Now.AddDays(-10));
        db.RefreshTokens.AddRange(beyondRetention, atBoundary, withinRetention);
        await db.SaveChangesAsync(ct);
        var store = new RefreshTokenStore(db, new TestTimeProvider(Now));

        var removed = await store.PurgeExpiredAsync(TimeSpan.FromDays(30), ct);

        Assert.Equal(1, removed);
        Assert.DoesNotContain(await db.RefreshTokens.ToListAsync(ct),
            token => token.Id == beyondRetention.Id);
        Assert.Contains(await db.RefreshTokens.ToListAsync(ct),
            token => token.Id == atBoundary.Id);
        Assert.Contains(await db.RefreshTokens.ToListAsync(ct),
            token => token.Id == withinRetention.Id);
        Assert.Equal(0, await store.PurgeExpiredAsync(TimeSpan.FromDays(30), ct));
    }

    private static RefreshToken Token(
        Guid userId,
        string rawToken,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? revokedAt = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Hash(rawToken),
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt,
        };

    private static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)))
            .ToLowerInvariant();

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }

    private sealed class SaveShapeProbe : SaveChangesInterceptor
    {
        public List<SaveShape> Snapshots { get; } = [];

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var entries = eventData.Context!.ChangeTracker.Entries<RefreshToken>();
            Snapshots.Add(new SaveShape(
                entries.Count(entry => entry.State == EntityState.Added),
                entries.Count(entry => entry.State == EntityState.Modified)));
            return ValueTask.FromResult(result);
        }
    }

    private sealed record SaveShape(int Added, int Modified);
}
