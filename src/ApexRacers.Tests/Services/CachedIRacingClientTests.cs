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
    public async Task GetOrFetchAsync_CacheMiss_FetchesStoresAndReturns()
    {
        await using var db = DbContextFactory.Create();
        var sut = new CachedIRacingClient(db, Substitute.For<IDataClient>());
        var value = new Sample(42, "fresh");
        var fetchCount = 0;

        var result = await sut.GetOrFetchAsync<Sample>(
            Spec,
            _ => { fetchCount++; return Task.FromResult(value); }, Ct);

        Assert.Equal(value, result);
        Assert.Equal(1, fetchCount);

        var row = Assert.Single(db.ExternalDataCaches);
        Assert.Equal("sample:1", row.CacheKey);
        Assert.Equal(value, JsonSerializer.Deserialize<Sample>(row.Payload));
        Assert.True(row.ExpiresAt > DateTimeOffset.UtcNow);
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
    public async Task GetOrFetchAsync_ExpiredEntry_RefetchesAndUpdatesInPlace()
    {
        await using var db = DbContextFactory.Create();
        var stale = new Sample(1, "stale");
        db.ExternalDataCaches.Add(new ExternalDataCache
        {
            CacheKey = "sample:1",
            Payload = JsonSerializer.Serialize(stale),
            FetchedAt = DateTimeOffset.UtcNow.AddHours(-7),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1), // already expired
        });
        await db.SaveChangesAsync(Ct);

        var sut = new CachedIRacingClient(db, Substitute.For<IDataClient>());
        var fresh = new Sample(2, "fresh");
        var fetchCount = 0;

        var result = await sut.GetOrFetchAsync<Sample>(
            Spec,
            _ => { fetchCount++; return Task.FromResult(fresh); }, Ct);

        Assert.Equal(fresh, result);
        Assert.Equal(1, fetchCount);

        // Same row updated in place, not duplicated.
        var row = Assert.Single(db.ExternalDataCaches);
        Assert.Equal(fresh, JsonSerializer.Deserialize<Sample>(row.Payload));
        Assert.True(row.ExpiresAt > DateTimeOffset.UtcNow);
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
    public async Task GetOrFetchAsync_ConcurrentColdMiss_LoserStillReturnsAndOnlyOneRowExists()
    {
        await using var shared = DbContextFactory.CreateShared();
        await using var dbSlow = shared.NewContext();
        await using var dbFast = shared.NewContext();

        var slowMayFinish = new TaskCompletionSource();
        var slow = new CachedIRacingClient(dbSlow, Substitute.For<IDataClient>());
        var fast = new CachedIRacingClient(dbFast, Substitute.For<IDataClient>());

        var slowCall = slow.GetOrFetchAsync<Sample>(
            Spec,
            async _ =>
            {
                await slowMayFinish.Task;
                return new Sample(1, "slow");
            },
            Ct);

        // The winner commits the row the loser is about to try to insert.
        var fastResult = await fast.GetOrFetchAsync<Sample>(
            Spec, _ => Task.FromResult(new Sample(2, "fast")), Ct);
        slowMayFinish.SetResult();

        var slowResult = await slowCall;

        // Both callers get the value they fetched — neither sees an exception.
        Assert.Equal(new Sample(2, "fast"), fastResult);
        Assert.Equal(new Sample(1, "slow"), slowResult);

        await using var verify = shared.NewContext();
        Assert.Single(verify.ExternalDataCaches);
    }
}
