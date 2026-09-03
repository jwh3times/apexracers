using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoCacheSeederStandingsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SeedStandingsAsync_KeysPerActiveSeasonClassAndWeek()
    {
        await using var db = DbContextFactory.Create();
        db.Seasons.Add(new Season { Id = 6115, SeriesId = 444, Active = true, Year = 2026, Quarter = 2 });
        db.Seasons.Add(new Season { Id = 7000, SeriesId = 9, Active = false, Year = 2025, Quarter = 1 }); // inactive → skipped
        db.SeasonCarClasses.Add(new SeasonCarClass { SeasonId = 6115, CarClassId = 100 });
        db.Weeks.Add(new Week { Id = Guid.NewGuid(), SeasonId = 6115, RaceWeekIndex = 0, TrackId = 1 });
        db.Weeks.Add(new Week { Id = Guid.NewGuid(), SeasonId = 6115, RaceWeekIndex = 1, TrackId = 1 });
        await db.SaveChangesAsync(Ct);

        await new DemoCacheSeeder(db).SeedStandingsAsync(Ct);

        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == IRacingCacheKeys.Standings(6115, 100).Key, Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == IRacingCacheKeys.TimeTrialStandings(6115, 100).Key, Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == IRacingCacheKeys.QualifyResults(6115, 100, 0).Key, Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == IRacingCacheKeys.QualifyResults(6115, 100, 1).Key, Ct));
        Assert.False(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == IRacingCacheKeys.Standings(7000, 100).Key, Ct));
    }
}
