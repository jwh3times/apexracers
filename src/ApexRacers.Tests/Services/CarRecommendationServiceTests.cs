using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class CarRecommendationServiceTests
{
    private static (Week week, Car car1, Car car2) SeedWeekWithTwoCars(ApexRacers.Data.AppDbContext db)
    {
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var week = new Week { Id = 10, SeasonId = 1, WeekNumber = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), TrackName = "Spa", ConfigName = "Full", IracingTrackId = 99, Season = season };
        var car1 = new Car { Id = 1, Name = "Porsche 992 GT3", NameAbbreviated = "P992" };
        var car2 = new Car { Id = 2, Name = "Ferrari 296 GT3", NameAbbreviated = "F296" };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Weeks.Add(week);
        db.Cars.AddRange(car1, car2);
        return (week, car1, car2);
    }

    private static CarRecommendationService CreateService(ApexRacers.Data.AppDbContext db) =>
        new(db, new PercentileCalculationService(db));

    [Fact]
    public async Task GetRecommendationsAsync_WeekNotFound_ReturnsEmpty()
    {
        await using var db = DbContextFactory.Create();

        var result = await CreateService(db).GetRecommendationsAsync(weekId: 99, customerId: 1);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecommendationsAsync_DriverHasNoLapTimesForAnyCar_ReturnsEmpty()
    {
        await using var db = DbContextFactory.Create();
        var (week, car1, _) = SeedWeekWithTwoCars(db);
        db.LapTimeEntries.Add(new LapTimeEntry { WeekId = 10, CarId = 1, DriverCustomerId = 999, LapTimeSeconds = 70, Car = car1, Week = week, RecordedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetRecommendationsAsync(weekId: 10, customerId: 1);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecommendationsAsync_TwoCars_RanksHigherPercentileFirst()
    {
        await using var db = DbContextFactory.Create();
        var (week, car1, car2) = SeedWeekWithTwoCars(db);
        // Car1: driver is slowest (percentile = 0), Car2: driver is fastest (percentile = 100)
        db.LapTimeEntries.AddRange(
            new LapTimeEntry { WeekId = 10, CarId = 1, DriverCustomerId = 1, LapTimeSeconds = 90, Car = car1, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = 10, CarId = 1, DriverCustomerId = 2, LapTimeSeconds = 70, Car = car1, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = 10, CarId = 2, DriverCustomerId = 1, LapTimeSeconds = 60, Car = car2, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = 10, CarId = 2, DriverCustomerId = 3, LapTimeSeconds = 70, Car = car2, Week = week, RecordedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetRecommendationsAsync(weekId: 10, customerId: 1);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Rank);
        Assert.Equal(2, result[0].CarId); // Car2: driver is fastest
        Assert.Equal(2, result[1].Rank);
        Assert.Equal(1, result[1].CarId);
    }

    [Fact]
    public async Task GetRecommendationsAsync_CachedPercentileExists_UsesCachedValue()
    {
        await using var db = DbContextFactory.Create();
        var (week, car1, _) = SeedWeekWithTwoCars(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApexRacers.Data.ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        // Cached result with a known percentile
        db.CarPercentileResults.Add(new CarPercentileResult { UserId = userId, CarId = 1, WeekId = 10, PercentileRank = 88.5, SampleSize = 100, ComputedAt = DateTimeOffset.UtcNow });
        // Need at least one LapTimeEntry so the car appears in the week
        db.LapTimeEntries.Add(new LapTimeEntry { WeekId = 10, CarId = 1, DriverCustomerId = 1, LapTimeSeconds = 70, Car = car1, Week = week, RecordedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetRecommendationsAsync(weekId: 10, customerId: 1);

        var dto = Assert.Single(result);
        Assert.Equal(88.5, dto.PercentileRank);
        Assert.Equal(100, dto.SampleSize);
    }
}
