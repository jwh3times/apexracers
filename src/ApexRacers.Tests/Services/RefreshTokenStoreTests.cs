using System.Security.Cryptography;
using System.Text;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using ApexRacers.Tests.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApexRacers.Tests.Services;

[Collection(PostgreSqlCollection.Name)]
public class RefreshTokenStoreTests(PostgreSqlFixture postgres)
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 9, 16, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task IssueAsync_PersistsOnlyTheHashAndUsesTheCanonicalLifetime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await postgres.CreateDbContextAsync(ct);
        var store = new RefreshTokenStore(db, new TestTimeProvider(Now), NullLogger<RefreshTokenStore>.Instance);
        var userId = Guid.NewGuid();
        await SeedUsersAsync(db, ct, userId);

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
        await using var db = await postgres.CreateDbContextAsync(ct);
        const string rawToken = "expires-at-the-boundary";
        var userId = Guid.NewGuid();
        await SeedUsersAsync(db, ct, userId);
        db.RefreshTokens.Add(Token(
            userId, rawToken, Now.AddDays(-7), expiresAt: Now));
        await db.SaveChangesAsync(ct);
        var store = new RefreshTokenStore(db, new TestTimeProvider(Now), NullLogger<RefreshTokenStore>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RotateAsync(rawToken, ct));

        Assert.Equal("Invalid or expired refresh token.", exception.Message);
        Assert.Null((await db.RefreshTokens.SingleAsync(ct)).RevokedAt);
    }

    /// <summary>
    /// The persisted outcome of a rotation: the presented credential is spent, its successor is
    /// stored only as a hash, and the successor carries a fresh lifetime. The revocation is applied
    /// by a conditional update rather than a tracked edit — that is what makes it single-use — so
    /// this reads the committed rows instead of inspecting change-tracker state.
    /// </summary>
    [Fact]
    public async Task RotateAsync_SpendsTheCredentialAndPersistsOnlyTheReplacementHash()
    {
        var ct = TestContext.Current.CancellationToken;
        var probe = new SaveShapeProbe();
        var options = await postgres.CreateOptionsAsync(ct, probe);
        await using var db = new AppDbContext(options);
        var clock = new TestTimeProvider(Now);
        var store = new RefreshTokenStore(db, clock, NullLogger<RefreshTokenStore>.Instance);
        var userId = Guid.NewGuid();
        await SeedUsersAsync(db, ct, userId);
        var originalRaw = await store.IssueAsync(userId, ct);
        var originalId = (await db.RefreshTokens.AsNoTracking().SingleAsync(ct)).Id;
        probe.Snapshots.Clear();
        clock.SetUtcNow(Now.AddHours(1));

        var rotation = await store.RotateAsync(originalRaw, ct);

        // The only tracked write left in a rotation is the replacement insert; the revocation
        // travels as its own conditional statement inside the same transaction.
        var snapshot = Assert.Single(probe.Snapshots);
        Assert.Equal(1, snapshot.Added);
        Assert.Equal(0, snapshot.Modified);
        Assert.Equal(userId, rotation.UserId);
        Assert.NotEqual(originalRaw, rotation.RawToken);

        await using var verification = new AppDbContext(options);
        var rows = await verification.RefreshTokens.AsNoTracking().ToListAsync(ct);
        Assert.Equal(clock.GetUtcNow(), Assert.Single(rows, token => token.Id == originalId).RevokedAt);
        var replacement = Assert.Single(rows, token => token.Id != originalId);
        Assert.Equal(Hash(rotation.RawToken), replacement.TokenHash);
        Assert.NotEqual(rotation.RawToken, replacement.TokenHash);
        Assert.Null(replacement.RevokedAt);
        Assert.Equal(clock.GetUtcNow(), replacement.CreatedAt);
        Assert.Equal(clock.GetUtcNow().AddDays(7), replacement.ExpiresAt);
    }

    [Fact]
    public async Task RotateAsync_WhenReplacementInsertFails_RollsBackTheRevocation()
    {
        var ct = TestContext.Current.CancellationToken;
        var collision = new ReplacementHashCollision();
        var options = await postgres.CreateOptionsAsync(ct, collision);
        await using var db = new AppDbContext(options);
        var userId = Guid.NewGuid();
        await SeedUsersAsync(db, ct, userId);
        var store = new RefreshTokenStore(db, new TestTimeProvider(Now), NullLogger<RefreshTokenStore>.Instance);
        var rawToken = await store.IssueAsync(userId, ct);
        var original = await db.RefreshTokens.SingleAsync(ct);
        collision.CollideWith(original.TokenHash);

        await Assert.ThrowsAsync<DbUpdateException>(() => store.RotateAsync(rawToken, ct));

        await using var verification = new AppDbContext(options);
        var persisted = await verification.RefreshTokens.AsNoTracking().SingleAsync(ct);
        Assert.Null(persisted.RevokedAt);
    }

    [Fact]
    public async Task RefreshTokenSchema_EnforcesUniqueHashForeignKeyAndUserCascade()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await postgres.CreateOptionsAsync(ct);
        var userId = Guid.NewGuid();
        const string sharedRaw = "one-persisted-hash";
        await using (var seed = new AppDbContext(options))
        {
            await SeedUsersAsync(seed, ct, userId);
            seed.RefreshTokens.Add(Token(userId, sharedRaw, Now, Now.AddDays(1)));
            await seed.SaveChangesAsync(ct);
        }

        await using (var duplicate = new AppDbContext(options))
        {
            duplicate.RefreshTokens.Add(Token(userId, sharedRaw, Now, Now.AddDays(1)));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync(ct));
        }

        await using (var orphan = new AppDbContext(options))
        {
            orphan.RefreshTokens.Add(Token(Guid.NewGuid(), "orphan", Now, Now.AddDays(1)));
            await Assert.ThrowsAsync<DbUpdateException>(() => orphan.SaveChangesAsync(ct));
        }

        await using (var deleteUser = new AppDbContext(options))
        {
            deleteUser.Users.Remove(await deleteUser.Users.SingleAsync(user => user.Id == userId, ct));
            await deleteUser.SaveChangesAsync(ct);
        }

        await using var verification = new AppDbContext(options);
        Assert.Empty(await verification.RefreshTokens.AsNoTracking().ToListAsync(ct));
    }

    [Fact]
    public async Task IssueAsync_AtCap_RevokesTheOldestCanonicalActiveToken()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await postgres.CreateDbContextAsync(ct);
        var userId = Guid.NewGuid();
        await SeedUsersAsync(db, ct, userId);
        var tokens = Enumerable.Range(0, 5)
            .Select(index => Token(
                userId,
                $"active-{index}",
                Now.AddMinutes(-10 + index),
                Now.AddDays(1)))
            .ToList();
        db.RefreshTokens.AddRange(tokens);
        await db.SaveChangesAsync(ct);
        var store = new RefreshTokenStore(db, new TestTimeProvider(Now), NullLogger<RefreshTokenStore>.Instance);

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
        await using var db = await postgres.CreateDbContextAsync(ct);
        var clock = new TestTimeProvider(Now);
        var store = new RefreshTokenStore(db, clock, NullLogger<RefreshTokenStore>.Instance);
        var userId = Guid.NewGuid();
        await SeedUsersAsync(db, ct, userId);
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
        await using var db = await postgres.CreateDbContextAsync(ct);
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await SeedUsersAsync(db, ct, userId, otherUserId);
        var previouslyRevokedAt = Now.AddDays(-2);
        var active = Token(userId, "active", Now.AddDays(-1), Now.AddDays(1));
        var expired = Token(userId, "expired", Now.AddDays(-8), Now.AddSeconds(-1));
        var revoked = Token(
            userId, "revoked", Now.AddDays(-2), Now.AddDays(1), previouslyRevokedAt);
        var otherUser = Token(
            otherUserId, "other-user", Now.AddDays(-1), Now.AddDays(1));
        db.RefreshTokens.AddRange(active, expired, revoked, otherUser);
        await db.SaveChangesAsync(ct);
        var store = new RefreshTokenStore(db, new TestTimeProvider(Now), NullLogger<RefreshTokenStore>.Instance);

        await store.RevokeAllActiveAsync(userId, ct);

        // Revocation is a set-based statement, so the committed rows are the record of what it did;
        // the tracked instances above are deliberately not consulted.
        db.ChangeTracker.Clear();
        var afterFirst = await db.RefreshTokens.AsNoTracking().ToListAsync(ct);
        Assert.Equal(Now, Assert.Single(afterFirst, token => token.Id == active.Id).RevokedAt);
        Assert.Null(Assert.Single(afterFirst, token => token.Id == expired.Id).RevokedAt);
        Assert.Equal(previouslyRevokedAt, Assert.Single(afterFirst, token => token.Id == revoked.Id).RevokedAt);
        Assert.Null(Assert.Single(afterFirst, token => token.Id == otherUser.Id).RevokedAt);

        // Idempotent: a second call finds nothing active and leaves the first stamp alone.
        await store.RevokeAllActiveAsync(userId, ct);
        db.ChangeTracker.Clear();
        Assert.Equal(
            Now,
            (await db.RefreshTokens.AsNoTracking().SingleAsync(token => token.Id == active.Id, ct)).RevokedAt);
    }

    [Fact]
    public async Task RevokeAsync_IsBestEffortAndCanStampAnExpiredPresentedCredential()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await postgres.CreateDbContextAsync(ct);
        var clock = new TestTimeProvider(Now);
        const string expiredRaw = "expired-presented-token";
        var userId = Guid.NewGuid();
        await SeedUsersAsync(db, ct, userId);
        var expired = Token(
            userId, expiredRaw, Now.AddDays(-8), Now.AddSeconds(-1));
        db.RefreshTokens.Add(expired);
        await db.SaveChangesAsync(ct);
        var store = new RefreshTokenStore(db, clock, NullLogger<RefreshTokenStore>.Instance);

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
        await using var db = await postgres.CreateDbContextAsync(ct);
        var userId = Guid.NewGuid();
        await SeedUsersAsync(db, ct, userId);
        var beyondRetention = Token(
            userId, "too-old", Now.AddDays(-50), Now.AddDays(-31));
        var atBoundary = Token(
            userId, "at-boundary", Now.AddDays(-40), Now.AddDays(-30));
        var withinRetention = Token(
            userId, "within", Now.AddDays(-20), Now.AddDays(-10));
        db.RefreshTokens.AddRange(beyondRetention, atBoundary, withinRetention);
        await db.SaveChangesAsync(ct);
        var store = new RefreshTokenStore(db, new TestTimeProvider(Now), NullLogger<RefreshTokenStore>.Instance);

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

    [Fact]
    public async Task RotateAsync_ReplayedToken_DurablyRevokesAllUserSessionsAndLogsOnlyUserId()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await postgres.CreateOptionsAsync(ct);
        await using var db = new AppDbContext(options);
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await SeedUsersAsync(db, ct, userId, otherUserId);
        var logger = new FakeLogger<RefreshTokenStore>();
        var store = new RefreshTokenStore(db, new TestTimeProvider(Now), logger);
        var original = await store.IssueAsync(userId, ct);
        var sibling = await store.IssueAsync(userId, ct);
        var otherUser = await store.IssueAsync(otherUserId, ct);
        var replacement = await store.RotateAsync(original, ct);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.RotateAsync(original, ct));

        Assert.Equal("Invalid or expired refresh token.", exception.Message);
        await using var verification = new AppDbContext(options);
        var rows = await verification.RefreshTokens.AsNoTracking().ToListAsync(ct);
        Assert.All(rows.Where(token => token.UserId == userId), token => Assert.Equal(Now, token.RevokedAt));
        Assert.Null(Assert.Single(rows, token => token.UserId == otherUserId).RevokedAt);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal($"Refresh-token reuse detected for user {userId}.", entry.Message);
        foreach (var raw in new[] { original, sibling, otherUser, replacement.RawToken })
        {
            Assert.DoesNotContain(raw, entry.Message);
            Assert.DoesNotContain(Hash(raw), entry.Message);
        }
        var freshStore = new RefreshTokenStore(verification, new TestTimeProvider(Now), NullLogger<RefreshTokenStore>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => freshStore.RotateAsync(replacement.RawToken, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => freshStore.RotateAsync(sibling, ct));
        await freshStore.RotateAsync(otherUser, ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RotateAsync_RevokedToken_TriggersReuseEvenIfExpired(bool expired)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await postgres.CreateDbContextAsync(ct);
        var userId = Guid.NewGuid();
        await SeedUsersAsync(db, ct, userId);
        var revoked = Token(userId, "revoked", Now.AddDays(-8), expired ? Now.AddDays(-1) : Now.AddDays(1), Now.AddDays(-2));
        db.RefreshTokens.Add(revoked);
        await db.SaveChangesAsync(ct);
        var logger = new FakeLogger<RefreshTokenStore>();
        var store = new RefreshTokenStore(db, new TestTimeProvider(Now), logger);
        await store.IssueAsync(userId, ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RotateAsync("revoked", ct));

        db.ChangeTracker.Clear();
        Assert.Equal(Now, (await db.RefreshTokens.SingleAsync(token => token.Id != revoked.Id, ct)).RevokedAt);
        Assert.Equal(Now.AddDays(-2), (await db.RefreshTokens.SingleAsync(token => token.Id == revoked.Id, ct)).RevokedAt);
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("expired")]
    [InlineData("boundary")]
    public async Task RotateAsync_UnknownOrUnrevokedExpiredToken_LeavesOtherSessionsActive(string presented)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await postgres.CreateDbContextAsync(ct);
        var userId = Guid.NewGuid();
        await SeedUsersAsync(db, ct, userId);
        db.RefreshTokens.AddRange(
            Token(userId, "expired", Now.AddDays(-8), Now.AddSeconds(-1)),
            Token(userId, "boundary", Now.AddDays(-7), Now));
        await db.SaveChangesAsync(ct);
        var logger = new FakeLogger<RefreshTokenStore>();
        var store = new RefreshTokenStore(db, new TestTimeProvider(Now), logger);
        var sibling = await store.IssueAsync(userId, ct);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.RotateAsync(presented, ct));

        Assert.Equal("Invalid or expired refresh token.", exception.Message);
        db.ChangeTracker.Clear();
        Assert.All(await db.RefreshTokens.ToListAsync(ct), token => Assert.Null(token.RevokedAt));
        Assert.Empty(logger.Entries);
        await store.RotateAsync(sibling, ct);
    }

    /// <summary>
    /// Two requests present the same refresh token at once, each on its own context, with both
    /// reads forced to complete before either acts. Exactly one may end up holding a usable
    /// successor: rotation exists to make a credential single-use, and a race that mints two of
    /// them leaves a second valid session nobody asked for.
    /// </summary>
    [Fact]
    public async Task RotateAsync_ConcurrentRotationsOfOneToken_IssueExactlyOneSuccessor()
    {
        var ct = TestContext.Current.CancellationToken;
        var barrier = new RefreshTokenReadBarrier();
        var options = await postgres.CreateOptionsAsync(ct, barrier);
        var userId = Guid.NewGuid();
        string originalRaw;
        await using (var setup = new AppDbContext(options))
        {
            await SeedUsersAsync(setup, ct, userId);
            var setupStore = new RefreshTokenStore(setup, new TestTimeProvider(Now), NullLogger<RefreshTokenStore>.Instance);
            originalRaw = await setupStore.IssueAsync(userId, ct);
        }

        await using var firstDb = new AppDbContext(options);
        await using var secondDb = new AppDbContext(options);
        var firstStore = new RefreshTokenStore(firstDb, new TestTimeProvider(Now.AddHours(1)), NullLogger<RefreshTokenStore>.Instance);
        var secondStore = new RefreshTokenStore(secondDb, new TestTimeProvider(Now.AddHours(1)), NullLogger<RefreshTokenStore>.Instance);
        barrier.Arm();

        var outcomes = await Task.WhenAll(
            RotationOutcomeAsync(firstStore, originalRaw, ct),
            RotationOutcomeAsync(secondStore, originalRaw, ct));

        // The gate held both racers: this test really did force the interleaving.
        Assert.Equal(2, barrier.Arrivals);
        var winners = outcomes.OfType<RefreshTokenRotation>().ToList();
        var loser = Assert.Single(outcomes.OfType<Exception>());
        Assert.Single(winners);
        // The loser learns nothing it could distinguish from an ordinary bad token.
        Assert.IsType<InvalidOperationException>(loser);
        Assert.Equal("Invalid or expired refresh token.", loser.Message);

        await using var verification = new AppDbContext(options);
        var rows = await verification.RefreshTokens.AsNoTracking().ToListAsync(ct);
        var active = rows.Where(token => token.RevokedAt is null).ToList();
        Assert.Equal(Hash(winners[0].RawToken), Assert.Single(active).TokenHash);
        // The credential is spent exactly once, and no orphan successor was left behind.
        Assert.Equal(2, rows.Count);
        Assert.Equal(Now.AddHours(1), Assert.Single(rows, token => token.TokenHash == Hash(originalRaw)).RevokedAt);
    }

    /// <summary>
    /// The losing racer must not be treated as a replay. Two tabs share one refresh token through
    /// the client's store while its single-flight guard is per tab, so simultaneous rotation is an
    /// ordinary thing for an honest client to do; answering it with family-wide revocation would
    /// sign that user out everywhere for refreshing twice at once.
    /// </summary>
    [Fact]
    public async Task RotateAsync_ConcurrentRotationsOfOneToken_DoNotRevokeTheUsersOtherSessions()
    {
        var ct = TestContext.Current.CancellationToken;
        var barrier = new RefreshTokenReadBarrier();
        var options = await postgres.CreateOptionsAsync(ct, barrier);
        var userId = Guid.NewGuid();
        string contestedRaw;
        string bystanderRaw;
        await using (var setup = new AppDbContext(options))
        {
            await SeedUsersAsync(setup, ct, userId);
            var setupStore = new RefreshTokenStore(setup, new TestTimeProvider(Now), NullLogger<RefreshTokenStore>.Instance);
            contestedRaw = await setupStore.IssueAsync(userId, ct);
            bystanderRaw = await setupStore.IssueAsync(userId, ct);
        }

        await using var firstDb = new AppDbContext(options);
        await using var secondDb = new AppDbContext(options);
        var logger = new FakeLogger<RefreshTokenStore>();
        var firstStore = new RefreshTokenStore(firstDb, new TestTimeProvider(Now.AddHours(1)), logger);
        var secondStore = new RefreshTokenStore(secondDb, new TestTimeProvider(Now.AddHours(1)), NullLogger<RefreshTokenStore>.Instance);
        barrier.Arm();

        await Task.WhenAll(
            RotationOutcomeAsync(firstStore, contestedRaw, ct),
            RotationOutcomeAsync(secondStore, contestedRaw, ct));

        Assert.Equal(2, barrier.Arrivals);
        // An unrelated session of the same user survives, and losing is not logged as reuse.
        await using var verification = new AppDbContext(options);
        var bystander = await verification.RefreshTokens.AsNoTracking()
            .SingleAsync(token => token.TokenHash == Hash(bystanderRaw), ct);
        Assert.Null(bystander.RevokedAt);
        Assert.Empty(logger.Entries);

        var freshStore = new RefreshTokenStore(verification, new TestTimeProvider(Now.AddHours(2)), NullLogger<RefreshTokenStore>.Instance);
        await freshStore.RotateAsync(bystanderRaw, ct);
    }

    /// <summary>
    /// Replay detection must keep working after the race is closed: presenting an already-consumed
    /// credential later is still reuse, and still takes the whole family down.
    /// </summary>
    [Fact]
    public async Task RotateAsync_LosingRacerReplaysItsSpentToken_StillTriggersReuseRevocation()
    {
        var ct = TestContext.Current.CancellationToken;
        var barrier = new RefreshTokenReadBarrier();
        var options = await postgres.CreateOptionsAsync(ct, barrier);
        var userId = Guid.NewGuid();
        string contestedRaw;
        await using (var setup = new AppDbContext(options))
        {
            await SeedUsersAsync(setup, ct, userId);
            var setupStore = new RefreshTokenStore(setup, new TestTimeProvider(Now), NullLogger<RefreshTokenStore>.Instance);
            contestedRaw = await setupStore.IssueAsync(userId, ct);
        }

        await using (var firstDb = new AppDbContext(options))
        await using (var secondDb = new AppDbContext(options))
        {
            var firstStore = new RefreshTokenStore(firstDb, new TestTimeProvider(Now.AddHours(1)), NullLogger<RefreshTokenStore>.Instance);
            var secondStore = new RefreshTokenStore(secondDb, new TestTimeProvider(Now.AddHours(1)), NullLogger<RefreshTokenStore>.Instance);
            barrier.Arm();
            await Task.WhenAll(
                RotationOutcomeAsync(firstStore, contestedRaw, ct),
                RotationOutcomeAsync(secondStore, contestedRaw, ct));
        }

        Assert.Equal(2, barrier.Arrivals);

        await using var replayDb = new AppDbContext(options);
        var logger = new FakeLogger<RefreshTokenStore>();
        var replayStore = new RefreshTokenStore(replayDb, new TestTimeProvider(Now.AddHours(2)), logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => replayStore.RotateAsync(contestedRaw, ct));

        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
        replayDb.ChangeTracker.Clear();
        Assert.All(
            await replayDb.RefreshTokens.AsNoTracking().ToListAsync(ct),
            token => Assert.NotNull(token.RevokedAt));
    }

    /// <summary>
    /// A rotation that read its credential while it was still active must not mint a successor
    /// after reuse revocation has taken the family down. Once replay is detected, no new credential
    /// may escape the revocation boundary.
    /// </summary>
    [Fact]
    public async Task RotateAsync_ReuseRevocationLandsMidRotation_RefusesToMintASuccessor()
    {
        var ct = TestContext.Current.CancellationToken;
        var hold = new RefreshTokenReadHold();
        var options = await postgres.CreateOptionsAsync(ct, hold);
        var userId = Guid.NewGuid();
        string activeRaw;
        await using (var setup = new AppDbContext(options))
        {
            await SeedUsersAsync(setup, ct, userId);
            var setupStore = new RefreshTokenStore(setup, new TestTimeProvider(Now), NullLogger<RefreshTokenStore>.Instance);
            activeRaw = await setupStore.IssueAsync(userId, ct);
            // A credential that has already been spent, so presenting it counts as replay.
            setup.RefreshTokens.Add(Token(userId, "already-spent", Now.AddDays(-1), Now.AddDays(1), Now.AddHours(-1)));
            await setup.SaveChangesAsync(ct);
        }

        await using var rotatingDb = new AppDbContext(options);
        await using var replayingDb = new AppDbContext(options);
        var rotatingStore = new RefreshTokenStore(rotatingDb, new TestTimeProvider(Now.AddHours(1)), NullLogger<RefreshTokenStore>.Instance);
        var replayingStore = new RefreshTokenStore(replayingDb, new TestTimeProvider(Now.AddHours(1)), NullLogger<RefreshTokenStore>.Instance);

        hold.Arm();
        var rotating = RotationOutcomeAsync(rotatingStore, activeRaw, ct);
        // The rotation is parked having read a live credential, not yet knowing it is doomed.
        await hold.ArrivedAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => replayingStore.RotateAsync("already-spent", ct));
        hold.Release();

        var refusal = Assert.IsType<InvalidOperationException>(await rotating);
        Assert.Equal("Invalid or expired refresh token.", refusal.Message);

        await using var verification = new AppDbContext(options);
        var rows = await verification.RefreshTokens.AsNoTracking().ToListAsync(ct);
        // Nothing survives the revocation, and no successor was created to outlive it.
        Assert.All(rows, token => Assert.NotNull(token.RevokedAt));
        Assert.Equal(2, rows.Count);
    }

    /// <summary>
    /// A credential that appears behind revocation's first sweep must still be revoked. The
    /// interleaving that creates one is a rotation committing inside that statement's execution
    /// window: the successor is not in its snapshot, so a single sweep would leave it active and
    /// nothing would ever revisit it — it would outlive the revocation meant to end it.
    /// </summary>
    [Fact]
    public async Task RevokeAllActiveAsync_CredentialAppearsBehindTheFirstSweep_StillRevokesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await postgres.CreateOptionsAsync(ct);
        var userId = Guid.NewGuid();
        await using (var setup = new AppDbContext(options))
        {
            await SeedUsersAsync(setup, ct, userId);
            setup.RefreshTokens.Add(Token(userId, "present-at-the-sweep", Now.AddDays(-1), Now.AddDays(1)));
            await setup.SaveChangesAsync(ct);
        }

        // The injector writes through plain options; the store runs on the same database with the
        // injector attached, so its own first sweep is what triggers the extra credential.
        var injector = new TokenInjectedBetweenSweeps(
            options, userId, Hash("committed-behind-the-sweep"), Now.AddDays(1));
        await using var db = new AppDbContext(postgres.WithInterceptors(options, injector));
        var store = new RefreshTokenStore(db, new TestTimeProvider(Now), NullLogger<RefreshTokenStore>.Instance);

        await store.RevokeAllActiveAsync(userId, ct);

        await using var verification = new AppDbContext(options);
        var rows = await verification.RefreshTokens.AsNoTracking().ToListAsync(ct);
        Assert.Equal(2, rows.Count);
        // Both the credential the sweep saw and the one that landed behind it are spent.
        Assert.All(rows, token => Assert.NotNull(token.RevokedAt));
    }

    /// <summary>
    /// Revocation gives up rather than spinning when credentials keep appearing behind every sweep.
    /// No ordinary client can cause this — minting a successor means spending the credential it
    /// replaces, and those are already revoked — so it warns and returns instead of throwing:
    /// callers run this as cleanup on a path that is itself about to reject the request, and a
    /// different exception escaping here would change what that caller reports.
    /// </summary>
    [Fact]
    public async Task RevokeAllActiveAsync_CredentialsKeepAppearing_WarnsInsteadOfSpinning()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await postgres.CreateOptionsAsync(ct);
        var userId = Guid.NewGuid();
        await using (var setup = new AppDbContext(options))
        {
            await SeedUsersAsync(setup, ct, userId);
            setup.RefreshTokens.Add(Token(userId, "present-at-the-sweep", Now.AddDays(-1), Now.AddDays(1)));
            await setup.SaveChangesAsync(ct);
        }

        // More injections than the store will ever sweep, so every pass finds something new.
        var injector = new TokenInjectedBetweenSweeps(
            options, userId, Hash("relentless"), Now.AddDays(1), injections: 25);
        await using var db = new AppDbContext(postgres.WithInterceptors(options, injector));
        var logger = new FakeLogger<RefreshTokenStore>();
        var store = new RefreshTokenStore(db, new TestTimeProvider(Now), logger);

        await store.RevokeAllActiveAsync(userId, ct);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("did not converge", entry.Message);
        // The user id is safe to log; no credential material may appear alongside it.
        Assert.Contains(userId.ToString(), entry.Message);
        Assert.DoesNotContain(Hash("relentless"), entry.Message);
    }

    private static async Task<object> RotationOutcomeAsync(
        RefreshTokenStore store,
        string rawToken,
        CancellationToken ct)
    {
        try
        {
            return await store.RotateAsync(rawToken, ct);
        }
        catch (Exception exception)
        {
            return exception;
        }
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

    private static async Task SeedUsersAsync(
        AppDbContext db,
        CancellationToken ct,
        params Guid[] userIds)
    {
        db.Users.AddRange(userIds.Select(userId => new ApplicationUser
        {
            Id = userId,
            DisplayName = "Refresh Token Test Driver",
            UserName = $"driver-{userId:N}@example.com",
            NormalizedUserName = $"DRIVER-{userId:N}@EXAMPLE.COM",
            Email = $"driver-{userId:N}@example.com",
            NormalizedEmail = $"DRIVER-{userId:N}@EXAMPLE.COM",
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        }));
        await db.SaveChangesAsync(ct);
    }

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

    private sealed class ReplacementHashCollision : SaveChangesInterceptor
    {
        private string? _tokenHash;

        public void CollideWith(string tokenHash) => _tokenHash = tokenHash;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_tokenHash is not null)
            {
                var replacement = eventData.Context!.ChangeTracker.Entries<RefreshToken>()
                    .Single(entry => entry.State == EntityState.Added);
                replacement.Entity.TokenHash = _tokenHash;
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed record SaveShape(int Added, int Modified);
}
