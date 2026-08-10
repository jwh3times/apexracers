using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class ExternalDataCacheCleanupServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);

    private static ExternalDataCache Row(string key, DateTimeOffset expiresAt) => new()
    {
        CacheKey = key,
        Payload = "{}",
        FetchedAt = expiresAt - TimeSpan.FromHours(1),
        ExpiresAt = expiresAt,
    };

    [Fact]
    public async Task PurgeExpiredAsync_DeletesOnlyRowsExpiredBeyondGrace()
    {
        // InMemory (not SQLite): PurgeExpiredAsync filters on a DateTimeOffset range
        // (ExpiresAt < cutoff), which Npgsql translates in production but SQLite cannot.
        await using var db = DbContextFactory.CreateInMemory();
        db.ExternalDataCaches.AddRange(
            Row("abandoned", Now - TimeSpan.FromDays(3)), // expired 3 days ago → purged
            Row("recently-expired", Now - TimeSpan.FromHours(1)), // expired but within grace → kept
            Row("fresh", Now + TimeSpan.FromHours(1))); // not expired → kept
        await db.SaveChangesAsync(Ct);

        var removed = await ExternalDataCacheCleanupService.PurgeExpiredAsync(
            db, Now, TimeSpan.FromDays(2), Ct);

        Assert.Equal(1, removed);
        var remaining = db.ExternalDataCaches.Select(c => c.CacheKey).OrderBy(k => k).ToList();
        Assert.Equal(["fresh", "recently-expired"], remaining);
    }

    [Fact]
    public async Task PurgeExpiredAsync_NothingStale_ReturnsZeroAndKeepsRows()
    {
        // InMemory (not SQLite): PurgeExpiredAsync filters on a DateTimeOffset range
        // (ExpiresAt < cutoff), which Npgsql translates in production but SQLite cannot.
        await using var db = DbContextFactory.CreateInMemory();
        db.ExternalDataCaches.Add(Row("fresh", Now + TimeSpan.FromHours(1)));
        await db.SaveChangesAsync(Ct);

        var removed = await ExternalDataCacheCleanupService.PurgeExpiredAsync(
            db, Now, TimeSpan.FromDays(2), Ct);

        Assert.Equal(0, removed);
        Assert.Single(db.ExternalDataCaches);
    }

    [Fact]
    public async Task PurgeExpiredAsync_AtSentinelThreshold_PreservesTheInclusiveSentinelRange()
    {
        // Zero grace makes the cleanup predicate's strict < boundary explicit: the row one tick
        // below the threshold is stale, while the threshold itself and the writer's Sentinel stay.
        await using var db = DbContextFactory.CreateInMemory();
        db.ExternalDataCaches.AddRange(
            Row("below-threshold", DemoCache.SentinelThreshold.AddTicks(-1)),
            Row("at-threshold", DemoCache.SentinelThreshold),
            Row("writer-sentinel", DemoCache.Sentinel));
        await db.SaveChangesAsync(Ct);

        var removed = await ExternalDataCacheCleanupService.PurgeExpiredAsync(
            db, DemoCache.SentinelThreshold, TimeSpan.Zero, Ct);

        Assert.Equal(1, removed);
        var remaining = db.ExternalDataCaches
            .Select(cache => cache.CacheKey)
            .OrderBy(key => key)
            .ToList();
        Assert.Equal(["at-threshold", "writer-sentinel"], remaining);
    }
}
