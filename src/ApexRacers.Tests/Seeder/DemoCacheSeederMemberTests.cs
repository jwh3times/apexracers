using System.Text.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoCacheSeederMemberTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SeedMembersAsync_WritesProfileChartCareerForDemoAndRival()
    {
        await using var db = DbContextFactory.Create();

        await new DemoCacheSeeder(db).SeedMembersAsync(Ct);

        // Demo driver profile is a hit and round-trips as the real ProfileSnapshot.
        var profileRow = await db.ExternalDataCaches
            .SingleAsync(c => c.CacheKey == IRacingCacheKeys.Profile(DemoData.DriverCustId).Key, Ct);
        var profile = JsonSerializer.Deserialize<ProfileSnapshot>(profileRow.Payload)!;
        Assert.Equal("Demo Driver", profile.DisplayName);
        Assert.Equal(3, profile.Licenses.Count);

        // Per-category iRating chart key uses (int)MemberChartType.IRating.
        var chartKey = IRacingCacheKeys.IRatingChart(DemoData.DriverCustId, 5).Key;
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == chartKey, Ct));

        // Demo-only entries.
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == IRacingCacheKeys.Summary(DemoData.DriverCustId).Key, Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == IRacingCacheKeys.Recap(DemoData.DriverCustId).Key, Ct));

        // Rival has profile + career + chart (for /compare) but NOT summary/recap.
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == IRacingCacheKeys.Profile(DemoData.RivalCustId).Key, Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == IRacingCacheKeys.Career(DemoData.RivalCustId).Key, Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == IRacingCacheKeys.IRatingChart(DemoData.RivalCustId, 5).Key, Ct));
        Assert.False(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == IRacingCacheKeys.Summary(DemoData.RivalCustId).Key, Ct));
    }
}
