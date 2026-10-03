using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

[Collection(PostgreSqlCollection.Name)]
public class ExternalDataCacheCleanupServiceTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CleanupUsesProvenanceEvenWhenDemoExpiryIsOrdinary()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var demo = Row("demo-expired", Now.AddDays(-5));
        demo.Provenance = ApexRacers.Core.DataProvenance.Demo;
        db.ExternalDataCaches.AddRange(demo, Row("real-expired", Now.AddDays(-5)));
        await db.SaveChangesAsync(Ct);
        await ExternalDataCacheCleanupService.PurgeExpiredAsync(db, Now, TimeSpan.Zero, Ct);
        Assert.Equal("demo-expired", Assert.Single(db.ExternalDataCaches).CacheKey);
    }

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
        // PostgreSQL: PurgeExpiredAsync filters on a DateTimeOffset range
        // (ExpiresAt < cutoff), which the production provider translates but SQLite cannot.
        await using var db = await postgres.CreateDbContextAsync(Ct);
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
        // PostgreSQL: PurgeExpiredAsync filters on a DateTimeOffset range
        // (ExpiresAt < cutoff), which the production provider translates but SQLite cannot.
        await using var db = await postgres.CreateDbContextAsync(Ct);
        db.ExternalDataCaches.Add(Row("fresh", Now + TimeSpan.FromHours(1)));
        await db.SaveChangesAsync(Ct);

        var removed = await ExternalDataCacheCleanupService.PurgeExpiredAsync(
            db, Now, TimeSpan.FromDays(2), Ct);

        Assert.Equal(0, removed);
        Assert.Single(db.ExternalDataCaches);
    }

    [Fact]
    public async Task PurgeExpiredAsync_FutureExpiryDoesNotClassifyRealRowsAsDemo()
    {
        // MaxValue proves preservation is an explicit predicate, not an accident of today's date:
        // every sentinel-range row is before this cutoff, but cleanup must still retain it.
        await using var db = await postgres.CreateDbContextAsync(Ct);
        db.ExternalDataCaches.AddRange(
            Row("below-threshold", new DateTimeOffset(9000, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(-1)),
            Row("at-threshold", new DateTimeOffset(9000, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Row("writer-sentinel", DemoCache.Sentinel));
        await db.SaveChangesAsync(Ct);

        var removed = await ExternalDataCacheCleanupService.PurgeExpiredAsync(
            db, DateTimeOffset.MaxValue, TimeSpan.Zero, Ct);

        Assert.Equal(3, removed);
        var remaining = db.ExternalDataCaches
            .Select(cache => cache.CacheKey)
            .OrderBy(key => key)
            .ToList();
        Assert.Empty(remaining);
    }
}
