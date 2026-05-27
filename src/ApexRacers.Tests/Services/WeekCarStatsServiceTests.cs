using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class WeekCarStatsServiceTests
{
    private static (Series series, Season season, Week week, Car car) SeedBasic(ApexRacers.Data.AppDbContext db)
    {
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var week = new Week { Id = Guid.NewGuid(), SeasonId = 1, WeekNumber = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), TrackName = "Spa", ConfigName = "Full", IracingTrackId = 99, Season = season };
        var car = new Car { Id = 1, Name = "Porsche 992 GT3", NameAbbreviated = "P992" };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Weeks.Add(week);
        db.Cars.Add(car);
        return (series, season, week, car);
    }

    [Fact]
    public async Task GetCarsForWeekAsync_WeekNotInSeries_ReturnsEmpty()
    {
        await using var db = DbContextFactory.Create();
        SeedBasic(db);
        await db.SaveChangesAsync();

        var result = await new WeekCarStatsService(db).GetCarsForWeekAsync(seriesId: 99, weekNumber: 1);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCarsForWeekAsync_OddLapCount_ReturnsMiddleValueAsMedian()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, week, car) = SeedBasic(db);
        db.LapTimeEntries.AddRange(
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 1, LapTimeSeconds = 60, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 2, LapTimeSeconds = 70, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 3, LapTimeSeconds = 90, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await new WeekCarStatsService(db).GetCarsForWeekAsync(seriesId: 1, weekNumber: 1);

        var dto = Assert.Single(result);
        Assert.Equal(3, dto.EntryCount);
        Assert.Equal(60, dto.FastestLapSeconds);
        Assert.Equal(70, dto.MedianLapSeconds);
    }

    [Fact]
    public async Task GetCarsForWeekAsync_EvenLapCount_ReturnsAverageOfTwoMiddleValuesAsMedian()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, week, car) = SeedBasic(db);
        db.LapTimeEntries.AddRange(
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 1, LapTimeSeconds = 60, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 2, LapTimeSeconds = 70, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 3, LapTimeSeconds = 80, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 4, LapTimeSeconds = 90, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await new WeekCarStatsService(db).GetCarsForWeekAsync(seriesId: 1, weekNumber: 1);

        var dto = Assert.Single(result);
        Assert.Equal(75, dto.MedianLapSeconds);
    }

    [Fact]
    public async Task GetCarsForWeekAsync_MultipleCars_OrderedByFastestLapAscending()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, week, car) = SeedBasic(db);
        var car2 = new Car { Id = 2, Name = "Ferrari 296 GT3", NameAbbreviated = "F296" };
        db.Cars.Add(car2);
        db.LapTimeEntries.AddRange(
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 1, LapTimeSeconds = 70, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 2, DriverCustomerId = 2, LapTimeSeconds = 60, Car = car2, Week = week, RecordedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await new WeekCarStatsService(db).GetCarsForWeekAsync(seriesId: 1, weekNumber: 1);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].CarId); // Ferrari is faster
        Assert.Equal(1, result[1].CarId);
    }
}
