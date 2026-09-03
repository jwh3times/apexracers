using ApexRacers.Core.Models;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoCacheSeederScheduleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SeedBopAndWeatherAsync_FillsWeatherAndBop_Idempotently()
    {
        await using var db = DbContextFactory.Create();
        db.Seasons.Add(new Season { Id = 6115, SeriesId = 444, Active = true, Year = 2026, Quarter = 2 });
        db.Weeks.Add(new Week { Id = Guid.NewGuid(), SeasonId = 6115, RaceWeekIndex = 0, TrackId = 1 });
        db.SeasonCars.Add(new SeasonCar { SeasonId = 6115, CarId = 132 });
        await db.SaveChangesAsync(Ct);

        var seeder = new DemoCacheSeeder(db);
        await seeder.SeedBopAndWeatherAsync(Ct);
        await seeder.SeedBopAndWeatherAsync(Ct); // re-run: no duplicates / no overwrite churn

        var week = await db.Weeks.SingleAsync(w => w.SeasonId == 6115 && w.RaceWeekIndex == 0, Ct);
        Assert.False(string.IsNullOrEmpty(week.WeatherSummaryJson));
        Assert.Equal(1, await db.SeasonCarBops.CountAsync(b => b.SeasonId == 6115 && b.CarId == 132, Ct));
    }
}
