using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class PercentileCalculationServiceTests
{
    private static (Week week, Car car) SeedWeekAndCar(ApexRacers.Data.AppDbContext db)
    {
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var week = new Week { Id = Guid.NewGuid(), SeasonId = 1, WeekNumber = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), TrackName = "Spa", ConfigName = "Full", IracingTrackId = 99, Season = season };
        var car = new Car { Id = 1, Name = "Porsche 992 GT3", NameAbbreviated = "P992" };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Weeks.Add(week);
        db.Cars.Add(car);
        return (week, car);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_DriverHasNoLapTime_ReturnsNull()
    {
        await using var db = DbContextFactory.Create();
        var (week, car) = SeedWeekAndCar(db);
        db.LapTimeEntries.Add(new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 999, LapTimeSeconds = 70, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1);

        Assert.Null(result);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_OnlyDriverInField_Returns100Percentile()
    {
        await using var db = DbContextFactory.Create();
        var (week, car) = SeedWeekAndCar(db);
        db.LapTimeEntries.Add(new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 1, LapTimeSeconds = 70, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1);

        Assert.NotNull(result);
        Assert.Equal(100.0, result.PercentileRank);
        Assert.Equal(1, result.SampleSize);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_DriverBeats3Of4Others_Returns75Percentile()
    {
        await using var db = DbContextFactory.Create();
        var (week, car) = SeedWeekAndCar(db);
        db.LapTimeEntries.AddRange(
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 1, LapTimeSeconds = 65, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 2, LapTimeSeconds = 60, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 3, LapTimeSeconds = 70, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 4, LapTimeSeconds = 80, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 5, LapTimeSeconds = 90, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1);

        Assert.NotNull(result);
        Assert.Equal(75.0, result.PercentileRank);
        Assert.Equal(5, result.SampleSize);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_UserProfileExists_CreatesCarPercentileResult()
    {
        await using var db = DbContextFactory.Create();
        var (week, car) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApexRacers.Data.ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        db.LapTimeEntries.Add(new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 1, LapTimeSeconds = 70, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1);

        Assert.Single(db.CarPercentileResults);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_CachedResultExists_UpdatesExistingRow()
    {
        await using var db = DbContextFactory.Create();
        var (week, car) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApexRacers.Data.ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        db.LapTimeEntries.Add(new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 1, LapTimeSeconds = 70, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow });
        db.CarPercentileResults.Add(new CarPercentileResult { UserId = userId, CarId = 1, WeekId = week.Id, PercentileRank = 50, SampleSize = 2, ComputedAt = DateTimeOffset.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();

        await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1);

        Assert.Single(db.CarPercentileResults);
        Assert.Equal(100.0, db.CarPercentileResults.Single().PercentileRank);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_NoUserProfile_DoesNotCreateCacheRow()
    {
        await using var db = DbContextFactory.Create();
        var (week, car) = SeedWeekAndCar(db);
        db.LapTimeEntries.Add(new LapTimeEntry { WeekId = week.Id, CarId = 1, DriverCustomerId = 1, LapTimeSeconds = 70, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1);

        Assert.Empty(db.CarPercentileResults);
    }
}
