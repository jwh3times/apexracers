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
        var week = new Week { Id = Guid.NewGuid(), SeasonId = 1, WeekNumber = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), TrackName = "Spa", ConfigName = "Full", IracingTrackId = 99, Season = season };
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

        var result = await CreateService(db).GetRecommendationsAsync(seriesId: 99, weekNumber: 99, customerId: 1);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecommendationsAsync_NoLapsAndNoCachedPercentile_ReturnsEmpty()
    {
        await using var db = DbContextFactory.Create();
        var (week, car1, _) = SeedWeekWithTwoCars(db);
        // Only driver 999 has a lap; customer 1 has no lap and no cached percentile
        db.LapTimeEntries.Add(new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 999, LapTimeSeconds = 70, Car = car1, Week = week, RecordedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetRecommendationsAsync(seriesId: 1, weekNumber: 1, customerId: 1);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecommendationsAsync_TwoCars_RanksByFastestActualLap()
    {
        await using var db = DbContextFactory.Create();
        var (week, car1, car2) = SeedWeekWithTwoCars(db);
        db.LapTimeEntries.AddRange(
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 1, LapTimeSeconds = 90, Car = car1, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 2, LapTimeSeconds = 70, Car = car1, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 2, DriverCustomerId = 1, LapTimeSeconds = 60, Car = car2, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 2, DriverCustomerId = 3, LapTimeSeconds = 70, Car = car2, Week = week, RecordedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetRecommendationsAsync(seriesId: 1, weekNumber: 1, customerId: 1);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Rank);
        Assert.Equal(2, result[0].CarId);        // Car2: fastest actual lap (60 s)
        Assert.Equal(60.0, result[0].EstimatedLapSeconds);
        Assert.False(result[0].IsProjected);
        Assert.Equal(2, result[1].Rank);
        Assert.Equal(1, result[1].CarId);        // Car1: slower actual lap (90 s)
        Assert.Equal(90.0, result[1].EstimatedLapSeconds);
        Assert.False(result[1].IsProjected);
    }

    [Fact]
    public async Task GetRecommendationsAsync_ComputesHistoricalPercentileWhenNoCacheExists()
    {
        await using var db = DbContextFactory.Create();
        var (week, car1, _) = SeedWeekWithTwoCars(db);

        // Week 1 = current week (no lap for driver 1)
        db.LapTimeEntries.AddRange(
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 100, LapTimeSeconds = 70, Car = car1, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 200, LapTimeSeconds = 80, Car = car1, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 300, LapTimeSeconds = 90, Car = car1, Week = week, RecordedAt = DateTimeOffset.UtcNow });

        // Previous week — driver 1 ran 60 s and beat all 3 others (100%)
        var series2 = new Series { Id = 2, Name = "Other Series" };
        var season2 = new Season { Id = 2, SeriesId = 2, Year = 2026, Quarter = 2, Active = true, Series = series2 };
        var prevWeek = new Week { Id = Guid.NewGuid(), SeasonId = 2, WeekNumber = 5, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14)), TrackName = "Mugello", ConfigName = "GP", IracingTrackId = 88, Season = season2 };
        db.Series.Add(series2);
        db.Seasons.Add(season2);
        db.Weeks.Add(prevWeek);
        db.LapTimeEntries.AddRange(
            new LapTimeEntry { WeekId = prevWeek.Id, CarId = 1, DriverCustomerId = 1, LapTimeSeconds = 60, Car = car1, Week = prevWeek, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = prevWeek.Id, CarId = 1, DriverCustomerId = 101, LapTimeSeconds = 65, Car = car1, Week = prevWeek, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = prevWeek.Id, CarId = 1, DriverCustomerId = 102, LapTimeSeconds = 70, Car = car1, Week = prevWeek, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = prevWeek.Id, CarId = 1, DriverCustomerId = 103, LapTimeSeconds = 75, Car = car1, Week = prevWeek, RecordedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetRecommendationsAsync(seriesId: 1, weekNumber: 1, customerId: 1);

        var dto = Assert.Single(result);
        Assert.True(dto.IsProjected);
        Assert.Equal(100.0, dto.PercentileRank); // beat all 3 others in prev week
        // 100th percentile in [70, 80, 90]: pos = 0 → fastest = 70 s
        Assert.Equal(70.0, dto.EstimatedLapSeconds, precision: 6);
    }

    [Fact]
    public async Task GetRecommendationsAsync_ProjectsLapTimeFromCachedPercentile_WhenNoLapThisWeek()
    {
        await using var db = DbContextFactory.Create();
        var (week, car1, _) = SeedWeekWithTwoCars(db);

        // Three other drivers in week 1 / car1 with sorted laps: 70, 80, 90
        db.LapTimeEntries.AddRange(
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 100, LapTimeSeconds = 70, Car = car1, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 200, LapTimeSeconds = 80, Car = car1, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 300, LapTimeSeconds = 90, Car = car1, Week = week, RecordedAt = DateTimeOffset.UtcNow });

        // Customer 1 is linked to a user account with a cached 50th-percentile result for car1
        var userId = Guid.NewGuid();
        db.Users.Add(new ApexRacers.Data.ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        db.CarPercentileResults.Add(new ApexRacers.Core.Models.CarPercentileResult
        {
            UserId = userId, CarId = 1, WeekId = Guid.NewGuid(), // some previous week's guid
            PercentileRank = 50.0, SampleSize = 100, ComputedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetRecommendationsAsync(seriesId: 1, weekNumber: 1, customerId: 1);

        var dto = Assert.Single(result);
        Assert.Equal(1, dto.CarId);
        Assert.True(dto.IsProjected);
        Assert.Equal(50.0, dto.PercentileRank);
        // 50th percentile in [70, 80, 90]: pos = (3-1) * (1-0.5) = 1.0 → index 1 = 80 s
        Assert.Equal(80.0, dto.EstimatedLapSeconds, precision: 6);
    }
}
