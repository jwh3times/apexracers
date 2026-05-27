using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class UserAnalyticsServiceTests
{
    [Fact]
    public async Task GetAnalyticsAsync_NoPercentileData_ReturnsEmptyList()
    {
        await using var db = DbContextFactory.Create();
        var result = await new UserAnalyticsService(db).GetAnalyticsAsync(Guid.NewGuid(), null);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAnalyticsAsync_SingleCarResult_ReturnsCorrectDto()
    {
        await using var db = DbContextFactory.Create();
        var (series, season, week, car) = SeedBaseGraph(db, seriesId: 1, weekNumber: 1);
        var userId = Guid.NewGuid();
        db.Users.Add(MakeUser(userId, iracingId: 42));
        db.CarPercentileResults.Add(new CarPercentileResult
        {
            UserId = userId, CarId = car.Id, WeekId = week.Id,
            PercentileRank = 85.0, SampleSize = 100, ComputedAt = DateTimeOffset.UtcNow,
            Car = car, Week = week,
        });
        db.LapTimeEntries.AddRange(
            new LapTimeEntry { WeekId = week.Id, CarId = car.Id, DriverCustomerId = 42, LapTimeSeconds = 62.5, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week.Id, CarId = car.Id, DriverCustomerId = 99, LapTimeSeconds = 65.0, Car = car, Week = week, RecordedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await new UserAnalyticsService(db).GetAnalyticsAsync(userId, null);

        Assert.Single(result);
        var dto = result[0];
        Assert.Equal(car.Id, dto.CarId);
        Assert.Equal(car.Name, dto.CarName);
        Assert.Equal(series.Id, dto.SeriesId);
        Assert.Equal(series.Name, dto.SeriesName);
        Assert.Equal(85.0, dto.LatestPercentileRank);
        Assert.Equal(85.0, dto.BestPercentileRank);
        Assert.Equal(62.5, dto.PersonalBestLapSeconds);
        Assert.Equal(1, dto.TotalLaps);
        Assert.Single(dto.PercentileHistory);
    }

    [Fact]
    public async Task GetAnalyticsAsync_MultipleWeeks_BuildsOrderedTrendHistory()
    {
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 10, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var car = new Car { Id = 1, Name = "Porsche 992", NameAbbreviated = "P" };
        var week1 = MakeWeek(id: Guid.NewGuid(), seasonId: 10, weekNumber: 1, trackName: "Monza", season: season);
        var week2 = MakeWeek(id: Guid.NewGuid(), seasonId: 10, weekNumber: 2, trackName: "Spa", season: season);
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Cars.Add(car);
        db.Weeks.AddRange(week1, week2);

        var userId = Guid.NewGuid();
        db.CarPercentileResults.AddRange(
            new CarPercentileResult { UserId = userId, CarId = 1, WeekId = week1.Id, PercentileRank = 60.0, SampleSize = 80, ComputedAt = DateTimeOffset.UtcNow.AddDays(-14), Car = car, Week = week1 },
            new CarPercentileResult { UserId = userId, CarId = 1, WeekId = week2.Id, PercentileRank = 80.0, SampleSize = 90, ComputedAt = DateTimeOffset.UtcNow.AddDays(-7), Car = car, Week = week2 });
        await db.SaveChangesAsync();

        var result = await new UserAnalyticsService(db).GetAnalyticsAsync(userId, null);

        Assert.Single(result);
        var dto = result[0];
        Assert.Equal(2, dto.PercentileHistory.Count);
        Assert.Equal(60.0, dto.PercentileHistory[0].PercentileRank);
        Assert.Equal(80.0, dto.PercentileHistory[1].PercentileRank);
        Assert.Equal(80.0, dto.LatestPercentileRank);
        Assert.Equal(80.0, dto.BestPercentileRank);
    }

    [Fact]
    public async Task GetAnalyticsAsync_SeriesFilter_ExcludesOtherSeries()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, week1, car1) = SeedBaseGraph(db, seriesId: 1, weekNumber: 1);
        var (_, _, week2, car2) = SeedBaseGraph(db, seriesId: 2, weekNumber: 1);

        var userId = Guid.NewGuid();
        db.CarPercentileResults.AddRange(
            new CarPercentileResult { UserId = userId, CarId = car1.Id, WeekId = week1.Id, PercentileRank = 70.0, SampleSize = 50, ComputedAt = DateTimeOffset.UtcNow, Car = car1, Week = week1 },
            new CarPercentileResult { UserId = userId, CarId = car2.Id, WeekId = week2.Id, PercentileRank = 60.0, SampleSize = 50, ComputedAt = DateTimeOffset.UtcNow, Car = car2, Week = week2 });
        await db.SaveChangesAsync();

        var result = await new UserAnalyticsService(db).GetAnalyticsAsync(userId, seriesId: 1);

        Assert.Single(result);
        Assert.Equal(car1.Id, result[0].CarId);
    }

    [Fact]
    public async Task GetAnalyticsAsync_NoIRacingId_ReturnsZeroLapsAndNullPersonalBest()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, week, car) = SeedBaseGraph(db, seriesId: 1, weekNumber: 1);
        var userId = Guid.NewGuid();
        db.Users.Add(MakeUser(userId, iracingId: null));
        db.CarPercentileResults.Add(new CarPercentileResult
        {
            UserId = userId, CarId = car.Id, WeekId = week.Id,
            PercentileRank = 75.0, SampleSize = 60, ComputedAt = DateTimeOffset.UtcNow,
            Car = car, Week = week,
        });
        await db.SaveChangesAsync();

        var result = await new UserAnalyticsService(db).GetAnalyticsAsync(userId, null);

        Assert.Single(result);
        Assert.Equal(0, result[0].TotalLaps);
        Assert.Null(result[0].PersonalBestLapSeconds);
    }

    [Fact]
    public async Task GetAnalyticsAsync_MultipleCarsSortedByBestPercentileDescending()
    {
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 10, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var week = MakeWeek(Guid.NewGuid(), 10, 1, "Spa", season);
        var car1 = new Car { Id = 1, Name = "Porsche 992", NameAbbreviated = "P" };
        var car2 = new Car { Id = 2, Name = "Ferrari GT3", NameAbbreviated = "F" };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Weeks.Add(week);
        db.Cars.AddRange(car1, car2);

        var userId = Guid.NewGuid();
        db.CarPercentileResults.AddRange(
            new CarPercentileResult { UserId = userId, CarId = 1, WeekId = week.Id, PercentileRank = 70.0, SampleSize = 50, ComputedAt = DateTimeOffset.UtcNow, Car = car1, Week = week },
            new CarPercentileResult { UserId = userId, CarId = 2, WeekId = week.Id, PercentileRank = 90.0, SampleSize = 50, ComputedAt = DateTimeOffset.UtcNow, Car = car2, Week = week });
        await db.SaveChangesAsync();

        var result = await new UserAnalyticsService(db).GetAnalyticsAsync(userId, null);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].CarId);
        Assert.Equal(1, result[1].CarId);
    }

    [Fact]
    public async Task GetAnalyticsAsync_MedianComputedFromBestPercentileWeek()
    {
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 10, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var car = new Car { Id = 1, Name = "Porsche 992", NameAbbreviated = "P" };
        var week1 = MakeWeek(Guid.NewGuid(), 10, 1, "Monza", season); // best percentile week
        var week2 = MakeWeek(Guid.NewGuid(), 10, 2, "Spa", season);
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Cars.Add(car);
        db.Weeks.AddRange(week1, week2);

        var userId = Guid.NewGuid();
        db.CarPercentileResults.AddRange(
            new CarPercentileResult { UserId = userId, CarId = 1, WeekId = week1.Id, PercentileRank = 90.0, SampleSize = 3, ComputedAt = DateTimeOffset.UtcNow, Car = car, Week = week1 },
            new CarPercentileResult { UserId = userId, CarId = 1, WeekId = week2.Id, PercentileRank = 60.0, SampleSize = 3, ComputedAt = DateTimeOffset.UtcNow, Car = car, Week = week2 });

        // week1 laps: sorted [60, 70, 80] → median = 70
        db.LapTimeEntries.AddRange(
            new LapTimeEntry { WeekId = week1.Id, CarId = 1, DriverCustomerId = 1, LapTimeSeconds = 70, Car = car, Week = week1, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week1.Id, CarId = 1, DriverCustomerId = 2, LapTimeSeconds = 80, Car = car, Week = week1, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week1.Id, CarId = 1, DriverCustomerId = 3, LapTimeSeconds = 60, Car = car, Week = week1, RecordedAt = DateTimeOffset.UtcNow });

        // week2 laps: different values — median should NOT be taken from here
        db.LapTimeEntries.AddRange(
            new LapTimeEntry { WeekId = week2.Id, CarId = 1, DriverCustomerId = 1, LapTimeSeconds = 100, Car = car, Week = week2, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week2.Id, CarId = 1, DriverCustomerId = 2, LapTimeSeconds = 110, Car = car, Week = week2, RecordedAt = DateTimeOffset.UtcNow },
            new LapTimeEntry { WeekId = week2.Id, CarId = 1, DriverCustomerId = 3, LapTimeSeconds = 120, Car = car, Week = week2, RecordedAt = DateTimeOffset.UtcNow });

        await db.SaveChangesAsync();

        var result = await new UserAnalyticsService(db).GetAnalyticsAsync(userId, null);

        Assert.Single(result);
        Assert.Equal(70.0, result[0].MedianLapSeconds);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (Series series, Season season, Week week, Car car) SeedBaseGraph(
        ApexRacers.Data.AppDbContext db, int seriesId, int weekNumber)
    {
        var series = new Series { Id = seriesId, Name = $"Series {seriesId}" };
        var season = new Season { Id = seriesId * 10, SeriesId = seriesId, Year = 2026, Quarter = 2, Active = true, Series = series };
        var week = MakeWeek(Guid.NewGuid(), season.Id, weekNumber, $"Track-{seriesId}", season);
        var car = new Car { Id = seriesId * 100 + 1, Name = $"Car {seriesId}", NameAbbreviated = $"C{seriesId}" };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Weeks.Add(week);
        db.Cars.Add(car);
        return (series, season, week, car);
    }

    private static Week MakeWeek(Guid id, int seasonId, int weekNumber, string trackName, Season season) =>
        new Week
        {
            Id = id,
            SeasonId = seasonId,
            WeekNumber = weekNumber,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7 * weekNumber)),
            TrackName = trackName,
            ConfigName = "Full",
            IracingTrackId = seasonId * 100 + weekNumber,
            Season = season,
        };

    private static ApexRacers.Data.ApplicationUser MakeUser(Guid id, long? iracingId) =>
        new ApexRacers.Data.ApplicationUser
        {
            Id = id,
            IRacingCustomerId = iracingId,
            DisplayName = "Test",
            UserName = $"{id}@test.com",
            Email = $"{id}@test.com",
            SecurityStamp = "x",
        };
}
