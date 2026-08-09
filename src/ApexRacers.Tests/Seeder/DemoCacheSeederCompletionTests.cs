using System.Text.Json;
using ApexRacers.Api.Services;
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

    [Fact]
    public async Task SeedLapDataAsync_WritesLapTracePerSubsession_ForDemoDriver()
    {
        await using var db = await SeededResultsAsync();

        await new DemoCacheSeeder(db).SeedLapDataAsync(Ct);

        var row = await db.ExternalDataCaches
            .SingleAsync(c => c.CacheKey == IRacingCacheKeys.LapData(-10, DemoData.DriverCustId).Key, Ct);
        var dto = JsonSerializer.Deserialize<ApexRacers.Api.Dtos.DriverLapsDto>(row.Payload)!;
        Assert.Equal(-10, dto.SubsessionId);
        Assert.Equal(DemoData.DriverCustId, dto.CustId);
        Assert.Equal(90.0, dto.FastestLapSeconds, precision: 3);   // demo driver's BestLapSeconds
        Assert.Equal(30, dto.Laps.Count);
        Assert.Equal(DemoCache.Sentinel, row.ExpiresAt);
    }

    [Fact]
    public async Task SeedDriverSearchAsync_WritesCuratedTermKeys()
    {
        await using var db = DbContextFactory.Create();

        await new DemoCacheSeeder(db).SeedDriverSearchAsync(Ct);

        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == "driversearch:rival", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == "driversearch:demo", Ct));
        var row = await db.ExternalDataCaches.SingleAsync(c => c.CacheKey == "driversearch:rival", Ct);
        var hits = JsonSerializer.Deserialize<List<ApexRacers.Api.Dtos.DriverSearchResultDto>>(row.Payload)!;
        Assert.Contains(hits, h => h.CustId == DemoData.RivalCustId);
        Assert.Equal(DemoCache.Sentinel, row.ExpiresAt);
    }
}
