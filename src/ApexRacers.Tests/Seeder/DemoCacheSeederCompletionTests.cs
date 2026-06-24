using System.Text.Json;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoCacheSeederCompletionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // One synthetic subsession (negative id) on track 532 with the demo driver's result.
    private static async Task<AppDbContext> SeededResultsAsync()
    {
        var db = DbContextFactory.Create();
        db.Subsessions.Add(new Subsession { Id = -10, SeasonId = 6115, WeekNumber = 0, TrackId = 532 });
        db.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId = -10, CustId = DemoData.DriverCustId, CarId = 132, BestLapSeconds = 90.0,
        });
        db.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId = -10, CustId = 100_050, CarId = 132, BestLapSeconds = 88.0, // field best
        });
        await db.SaveChangesAsync(Ct);
        return db;
    }

    [Fact]
    public async Task SeedWorldRecordsAsync_WritesWrPerCarTrack_BelowFieldBest()
    {
        await using var db = await SeededResultsAsync();

        await new DemoCacheSeeder(db).SeedWorldRecordsAsync(Ct);

        var row = await db.ExternalDataCaches.SingleAsync(c => c.CacheKey == "wr:132:532", Ct);
        var wr = JsonSerializer.Deserialize<double?>(row.Payload);
        Assert.Equal(86.24, wr!.Value, precision: 2);              // 88.0 * 0.98
        Assert.Equal(DemoCache.Sentinel, row.ExpiresAt);
    }
}
