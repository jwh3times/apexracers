using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoCacheSeederAllTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SeedAllAsync_AllRowsUseTheSentinelExpiry()
    {
        await using var db = DbContextFactory.Create();
        db.Seasons.Add(new Season { Id = 6115, SeriesId = 444, Active = true, Year = 2026, Quarter = 2 });
        db.SeasonCarClasses.Add(new SeasonCarClass { SeasonId = 6115, CarClassId = 100 });
        db.Weeks.Add(new Week { Id = Guid.NewGuid(), SeasonId = 6115, WeekNumber = 0, TrackId = 1 });
        db.SeasonCars.Add(new SeasonCar { SeasonId = 6115, CarId = 132 });
        db.Subsessions.Add(new Subsession { Id = -10, SeasonId = 6115, WeekNumber = 0, TrackId = 1 });
        db.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId = -10, CustId = DemoData.DriverCustId, CarId = 132, BestLapSeconds = 90.0,
        });
        await db.SaveChangesAsync(Ct);

        await new DemoCacheSeeder(db).SeedAllAsync(Ct);

        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == $"profile:{DemoData.DriverCustId}", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == "leaderboard:5", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == "race-guide", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == "wr:132:1", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == $"laps:-10:{DemoData.DriverCustId}", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == "driversearch:rival", Ct));
        // Every seeded cache row carries the far-future sentinel (purge marker + never-miss).
        Assert.All(await db.ExternalDataCaches.ToListAsync(Ct), r => Assert.Equal(DemoCache.Sentinel, r.ExpiresAt));
    }
}
