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

    // Minimal IServiceProvider whose GetService<IDataClient>() returns the supplied
    // instance (or null to simulate "iRacing not configured").
    private sealed class StubServiceProvider(object? service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => service;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetOrFetchAsync_CacheMiss_FetchesStoresAndReturns()
    {
        await using var db = DbContextFactory.Create();
        var sut = new CachedIRacingClient(db, new StubServiceProvider(Substitute.For<IDataClient>()));
        var value = new Sample(42, "fresh");
        var fetchCount = 0;

        var result = await sut.GetOrFetchAsync<Sample>(
            "sample:1", TimeSpan.FromHours(6),
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
        var sut = new CachedIRacingClient(db, new StubServiceProvider(null));

        var result = await sut.GetOrFetchAsync<Sample>(
            "sample:1", TimeSpan.FromHours(6),
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

        var sut = new CachedIRacingClient(db, new StubServiceProvider(Substitute.For<IDataClient>()));
        var fresh = new Sample(2, "fresh");
        var fetchCount = 0;

        var result = await sut.GetOrFetchAsync<Sample>(
            "sample:1", TimeSpan.FromHours(6),
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
        var sut = new CachedIRacingClient(db, new StubServiceProvider(null));

        await Assert.ThrowsAsync<IRacingNotConfiguredException>(() =>
            sut.GetOrFetchAsync<Sample>(
                "sample:1", TimeSpan.FromHours(6),
                _ => Task.FromResult(new Sample(1, "never")), Ct));

        Assert.Empty(db.ExternalDataCaches);
    }

    [Fact]
    public async Task IsConfigured_ReflectsClientRegistration()
    {
        await using var db = DbContextFactory.Create();

        Assert.True(new CachedIRacingClient(db, new StubServiceProvider(Substitute.For<IDataClient>())).IsConfigured);
        Assert.False(new CachedIRacingClient(db, new StubServiceProvider(null)).IsConfigured);
    }
}
