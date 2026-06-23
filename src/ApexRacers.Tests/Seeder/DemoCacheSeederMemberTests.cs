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
            .SingleAsync(c => c.CacheKey == $"profile:{DemoData.DriverCustId}", Ct);
        var profile = JsonSerializer.Deserialize<ProfileSnapshot>(profileRow.Payload)!;
        Assert.Equal("Demo Driver", profile.DisplayName);
        Assert.Equal(3, profile.Licenses.Count);

        // Per-category iRating chart key uses (int)MemberChartType.IRating.
        var chartKey = $"chart:{DemoData.DriverCustId}:5:{(int)Aydsko.iRacingData.Member.MemberChartType.IRating}";
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == chartKey, Ct));

        // Demo-only entries.
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == $"summary:{DemoData.DriverCustId}", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == $"recap:{DemoData.DriverCustId}", Ct));

        // Rival has profile + career + chart (for /compare) but NOT summary/recap.
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == $"profile:{DemoData.RivalCustId}", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == $"career:{DemoData.RivalCustId}", Ct));
        Assert.False(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == $"summary:{DemoData.RivalCustId}", Ct));
    }
}
