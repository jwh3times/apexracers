using System.Text.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using NSubstitute;
using Xunit;

namespace ApexRacers.Tests.Services;

public class CachedIRacingClientTests
{
    private sealed record Sample(int N, string S);

    private static readonly CacheSpec Spec = new("sample:1", TimeSpan.FromHours(6));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetOrFetchAsync_CacheMiss_WithoutPurposeNeverFetchesOrStores()
    {
        await using var db = DbContextFactory.Create();
        var sut = new CachedIRacingClient(db, Substitute.For<IDataClient>());
        var reached = false;
        await Assert.ThrowsAsync<IRacingNotConfiguredException>(() => sut.GetOrFetchAsync<Sample>(Spec,
            _ => { reached = true; return Task.FromResult(new Sample(42, "fresh")); }, Ct));
        Assert.False(reached);
        Assert.Empty(db.ExternalDataCaches);
    }
    [Fact]
    public async Task GetOrFetchAsync_UnexpiredCacheHit_ReturnsStoredWithoutFetchingOrClient()
    {
        await using var db = DbContextFactory.Create();
        var stored = new Sample(7, "cached");
        db.ExternalDataCaches.Add(new ExternalDataCache
        {
            CacheKey = "sample:1",
            Payload = JsonSerializer.Serialize(stored),
            FetchedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        });
        await db.SaveChangesAsync(Ct);

        // Null provider proves a cache hit needs neither the client nor the fetch delegate.
        var sut = new CachedIRacingClient(db, null);

        var result = await sut.GetOrFetchAsync<Sample>(
            Spec,
            _ => throw new InvalidOperationException("fetch must not run on a cache hit"), Ct);

        Assert.Equal(stored, result);
        Assert.Single(db.ExternalDataCaches);
    }

    [Fact]
    public async Task GetOrFetchAsync_ExpiredEntry_WithoutPurposeDoesNotRenewOriginalClocks()
    {
        await using var db = DbContextFactory.Create();
        var fetched = DateTimeOffset.UtcNow.AddHours(-7);
        var expired = DateTimeOffset.UtcNow.AddHours(-1);
        db.ExternalDataCaches.Add(new ExternalDataCache
        {
            CacheKey = Spec.Key,
            Payload = "{\"N\":1,\"S\":\"stale\"}",
            FetchedAt = fetched,
            ExpiresAt = expired
        });
        await db.SaveChangesAsync(Ct);
        var sut = new CachedIRacingClient(db, Substitute.For<IDataClient>());
        await Assert.ThrowsAsync<IRacingNotConfiguredException>(() => sut.GetOrFetchAsync<Sample>(Spec,
            _ => throw new InvalidOperationException("Unapproved fetch must not run"), Ct));
        var row = Assert.Single(db.ExternalDataCaches);
        Assert.Equal(fetched, row.FetchedAt);
        Assert.Equal(expired, row.ExpiresAt);
    }
    [Fact]
    public async Task GetOrFetchAsync_NotConfigured_ThrowsAndStoresNothing()
    {
        await using var db = DbContextFactory.Create();
        var sut = new CachedIRacingClient(db, null);

        await Assert.ThrowsAsync<IRacingNotConfiguredException>(() =>
            sut.GetOrFetchAsync<Sample>(
                Spec,
                _ => Task.FromResult(new Sample(1, "never")), Ct));

        Assert.Empty(db.ExternalDataCaches);
    }

    /// <summary>
    /// The cold-start race: two callers both read-miss a key that has no row yet, so both reach
    /// the insert. <c>CacheKey</c> is unique, so the loser used to take a DbUpdateException that
    /// nothing mapped — surfacing as a 500, most exposed on the public /live board whose single
    /// global "race-guide" key is uncached on a cold or freshly-purged cache.
    ///
    /// Interleaved deterministically rather than by racing threads: the first caller's fetch
    /// blocks until the second has fully committed, which puts the first in exactly the state the
    /// bug needs — a null row read, now stale.
    /// </summary>
    [Fact]
    public async Task GetOrFetchAsync_ConcurrentColdMissesWithoutPurposeCannotAcquire()
    {
        await using var shared = DbContextFactory.CreateShared();
        await using var one = shared.NewContext();
        await using var two = shared.NewContext();
        foreach (var db in new[] { one, two })
            await Assert.ThrowsAsync<IRacingNotConfiguredException>(() => new CachedIRacingClient(db, Substitute.For<IDataClient>())
                .GetOrFetchAsync<Sample>(Spec, _ => throw new InvalidOperationException("No purpose"), Ct));
        Assert.Empty(one.ExternalDataCaches);
    }
    // ── Over-long keys (GHSA-jv96-89xc-98h2) ─────────────────────────────────

    [Fact]
    public async Task GetOrFetchAsync_KeyLongerThanTheColumn_ThrowsWithoutFetching()
    {
        // The failure this prevents is a quiet one: without the guard the insert throws, the
        // cold-start-race catch above swallows it, the caller still gets a value, and every
        // later request for that key silently repeats the live fetch. Nothing looks broken.
        await using var db = DbContextFactory.Create();
        var sut = new CachedIRacingClient(db, Substitute.For<IDataClient>());
        var oversized = new CacheSpec(
            new string('k', ExternalDataCache.CacheKeyMaxLength + 1), TimeSpan.FromHours(1));
        var fetchCount = 0;

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => sut.GetOrFetchAsync<Sample>(
                oversized,
                _ => { fetchCount++; return Task.FromResult(new Sample(1, "x")); }, Ct));

        // Refused before the upstream call, which is the point — the quota is the resource
        // being protected, not the cache table.
        Assert.Equal(0, fetchCount);
        Assert.Empty(db.ExternalDataCaches);
        Assert.Contains(ExternalDataCache.CacheKeyMaxLength.ToString(), ex.Message);
    }

    [Fact]
    public async Task GetOrFetchAsync_KeyExactlyAtTheColumnLimit_IsAccepted()
    {
        await using var db = DbContextFactory.Create();
        var sut = new CachedIRacingClient(db, Substitute.For<IDataClient>());
        var atLimit = new CacheSpec(
            new string('k', ExternalDataCache.CacheKeyMaxLength), TimeSpan.FromHours(1));

        db.ExternalDataCaches.Add(new ExternalDataCache
        {
            CacheKey = atLimit.Key,
            Payload = "{\"N\":9,\"S\":\"edge\"}",
            FetchedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync(Ct);
        var result = await sut.GetOrFetchAsync<Sample>(atLimit, _ => throw new InvalidOperationException("Warm hit"), Ct);

        Assert.Equal(new Sample(9, "edge"), result);
        Assert.Single(db.ExternalDataCaches);
    }
}
