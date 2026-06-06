using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class PercentileCalculationServiceTests
{
    private static (Week week, Car car, CarClass carClass, Subsession subsession) SeedWeekAndCar(AppDbContext db)
    {
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var track = new Track { Id = 99, Name = "Spa", ConfigName = "Full" };
        var week = new Week { Id = Guid.NewGuid(), SeasonId = 1, WeekNumber = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), TrackId = 99, Track = track, Season = season };
        var car = new Car { Id = 1, Name = "Porsche 992 GT3", NameAbbreviated = "P992" };
        var carClass = new CarClass { Id = 1, Name = "GT3", ShortName = "GT3", RelativeSpeed = 52 };
        var subsession = new Subsession { Id = -1, SeasonId = 1, WeekNumber = 1, WeekId = week.Id, TrackId = 99, StartTime = DateTimeOffset.UtcNow.AddHours(-2) };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Tracks.Add(track);
        db.Weeks.Add(week);
        db.Cars.Add(car);
        db.CarClasses.Add(carClass);
        db.Subsessions.Add(subsession);
        return (week, car, carClass, subsession);
    }

    private static void AddResult(AppDbContext db, Subsession subsession, Car car, CarClass carClass, long custId, double lapSeconds)
    {
        db.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId            = subsession.Id,
            CustId                  = custId,
            CarId                   = car.Id,
            CarClassId              = carClass.Id,
            BestLapSeconds          = lapSeconds,
            AverageLapSeconds       = lapSeconds * 1.01,
            FinishPosition          = 0,
            FinishPositionInClass   = 0,
            StartingPosition        = 0,
            StartingPositionInClass = 0,
            Incidents               = 0,
            LapsComplete            = 5,
            LapsLead                = 0,
            ChampPoints             = 0,
            AggregateChampPoints    = 0,
            NewIRating              = 1500,
            OldIRating              = 1500,
            NewCpi                  = 2.0,
            OldCpi                  = 2.0,
            ReasonOutId             = 0,
            Division                = 1,
        });
    }

    [Fact]
    public async Task ComputeAndCacheAsync_DriverHasNoLapTime_ReturnsNull()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 999, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_OnlyDriverInField_Returns100Percentile()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(100.0, result.PercentileRank);
        Assert.Equal(1, result.SampleSize);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_DriverBeats3Of4Others_Returns75Percentile()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 65);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 70);
        AddResult(db, subsession, car, carClass, custId: 4, lapSeconds: 80);
        AddResult(db, subsession, car, carClass, custId: 5, lapSeconds: 90);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(75.0, result.PercentileRank);
        Assert.Equal(5, result.SampleSize);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_UserProfileExists_CreatesCarPercentileResult()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, callerUserId: userId, ct: TestContext.Current.CancellationToken);

        Assert.Single(db.CarPercentileResults);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_CachedResultExists_UpdatesExistingRow()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        db.CarPercentileResults.Add(new CarPercentileResult { UserId = userId, CarId = 1, SeriesId = 1, WeekId = week.Id, PercentileRank = 50, SampleSize = 2, ComputedAt = DateTimeOffset.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, callerUserId: userId, ct: TestContext.Current.CancellationToken);

        Assert.Single(db.CarPercentileResults);
        Assert.Equal(100.0, db.CarPercentileResults.Single().PercentileRank);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_NoUserProfile_DoesNotCreateCacheRow()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, ct: TestContext.Current.CancellationToken);

        Assert.Empty(db.CarPercentileResults);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_IncludePersonalLaps_PersonalLapFasterThanRace_ImprovesPercentile()
    {
        // Race field: custId=1 (80s), 2 (60s), 3 (70s), 4 (90s), 5 (95s)
        // custId=1 also has a personal lap of 65s.
        // Without personal laps: driverBest=80, beats 90+95 among others → 2/4 = 50th percentile.
        // With personal laps:    driverBest=65, beats 70+90+95 among others → 3/4 = 75th percentile.
        // Driver's own race lap (80s) is excluded from comparison in both cases.
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 80);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 70);
        AddResult(db, subsession, car, carClass, custId: 4, lapSeconds: 90);
        AddResult(db, subsession, car, carClass, custId: 5, lapSeconds: 95);
        db.PersonalLaps.Add(new PersonalLap
        {
            UserId = userId, CarId = car.Id, TrackId = week.TrackId,
            LapTimeSeconds = 65, IsValidLap = true,
            RecordedAt = DateTimeOffset.UtcNow, Car = car, Track = week.Track,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var withoutPersonal = await new PercentileCalculationService(db)
            .ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, callerUserId: userId, includePersonalLaps: false, ct: TestContext.Current.CancellationToken);
        var withPersonal = await new PercentileCalculationService(db)
            .ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, callerUserId: userId, includePersonalLaps: true, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(withoutPersonal);
        Assert.Equal(50.0, withoutPersonal.PercentileRank);
        Assert.NotNull(withPersonal);
        Assert.Equal(75.0, withPersonal.PercentileRank);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_ReturnsSeriesNameTrackNameAndDistribution()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 65);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new PercentileCalculationService(db)
            .ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("GT3 Cup", result.SeriesName);
        Assert.Equal("Spa", result.TrackName);
        Assert.Equal("Full", result.TrackConfigName);
        Assert.Equal(65.0, result.YourBestLapSeconds);
        Assert.Equal(60.0, result.FieldBestLapSeconds);
        Assert.Equal(65.0, result.FieldMedianLapSeconds);
        Assert.Equal(20, result.Distribution.Count);
        Assert.Contains(result.Distribution, b => b.ContainsUser);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_IncludePersonalLaps_DriverNotInRaceField_PersonalLapYieldsPercentile()
    {
        // custId=1 never raced this week but has a personal lap.
        // Race field only has custId=2 (60s), 3 (70s), 4 (80s).
        // Personal lap 65s → slower than 70 and 80 (2 of 3) → 66.67th percentile.
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 70);
        AddResult(db, subsession, car, carClass, custId: 4, lapSeconds: 80);
        db.PersonalLaps.Add(new PersonalLap
        {
            UserId = userId, CarId = car.Id, TrackId = week.TrackId,
            LapTimeSeconds = 65, IsValidLap = true,
            RecordedAt = DateTimeOffset.UtcNow, Car = car, Track = week.Track,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var withoutPersonal = await new PercentileCalculationService(db)
            .ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, callerUserId: userId, includePersonalLaps: false, ct: TestContext.Current.CancellationToken);
        var withPersonal = await new PercentileCalculationService(db)
            .ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, callerUserId: userId, includePersonalLaps: true, ct: TestContext.Current.CancellationToken);

        Assert.Null(withoutPersonal);
        Assert.NotNull(withPersonal);
        Assert.Equal(200.0 / 3, withPersonal.PercentileRank, tolerance: 1e-10);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_IncludePersonalLaps_UnknownTypedLapIncludedWhenFilterActive()
    {
        // Laps uploaded before SessionType was tracked default to Unknown.
        // They must always pass through any session-type filter so pre-migration
        // data is never silently dropped.
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 70);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 80);
        db.PersonalLaps.Add(new PersonalLap
        {
            UserId = userId, CarId = car.Id, TrackId = week.TrackId,
            LapTimeSeconds = 65, IsValidLap = true,
            SessionType = LapSessionType.Unknown, // pre-migration default
            RecordedAt = DateTimeOffset.UtcNow, Car = car, Track = week.Track,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // custId=1 has no race result but has an Unknown-typed personal lap.
        // With a Race filter active the Unknown lap must still be included.
        var result = await new PercentileCalculationService(db)
            .ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1,
                callerUserId: userId,
                includePersonalLaps: true,
                personalLapTypes: [LapSessionType.Race], TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(100.0, result.PercentileRank, tolerance: 1e-10); // 65s beats 70 and 80
    }
}
