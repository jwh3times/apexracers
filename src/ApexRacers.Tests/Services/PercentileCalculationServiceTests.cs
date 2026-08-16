using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using NSubstitute;
using Xunit;

namespace ApexRacers.Tests.Services;

public class PercentileCalculationServiceTests
{
    private static PersonalBestEvidence OfficialEvidence => PersonalBestEvidence.OfficialRaceLapsOnly;

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

        var result = await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, evidence: OfficialEvidence, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_OnlyDriverInField_IsThatFieldsMedian()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, evidence: OfficialEvidence, ct: TestContext.Current.CancellationToken);

        // A Field of one: (0 slower + 0.5 x 1 tied) / 1. Alone, the driver is its median —
        // reporting 100 would claim they had beaten a field that does not exist.
        Assert.NotNull(result);
        Assert.Equal(50.0, result.PercentileRank);
        Assert.Equal(1, result.SampleSize);
        Assert.Equal(1, result.FieldPosition);
        Assert.Equal(100, result.TopSharePercent);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_DriverBeats3Of4Others_Returns70Percentile()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 65);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 70);
        AddResult(db, subsession, car, carClass, custId: 4, lapSeconds: 80);
        AddResult(db, subsession, car, carClass, custId: 5, lapSeconds: 90);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, evidence: OfficialEvidence, ct: TestContext.Current.CancellationToken);

        // Field of 5: (3 slower + 0.5 x 1 tied) / 5 = 70. Second of five is the top 40%.
        Assert.NotNull(result);
        Assert.Equal(70.0, result.PercentileRank);
        Assert.Equal(5, result.SampleSize);
        Assert.Equal(2, result.FieldPosition);
        Assert.Equal(40, result.TopSharePercent);
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

        await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, evidence: OfficialEvidence, callerUserId: userId, ct: TestContext.Current.CancellationToken);

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

        await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, evidence: OfficialEvidence, callerUserId: userId, ct: TestContext.Current.CancellationToken);

        Assert.Single(db.CarPercentileResults);
        Assert.Equal(50.0, db.CarPercentileResults.Single().PercentileRank);
        Assert.Equal(100, db.CarPercentileResults.Single().TopSharePercent);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_NoUserProfile_DoesNotCreateCacheRow()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await new PercentileCalculationService(db).ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1, evidence: OfficialEvidence, ct: TestContext.Current.CancellationToken);

        Assert.Empty(db.CarPercentileResults);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_UploadedEvidenceCannotOverlayDifferentSubjectDriver()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Caller" });
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 80);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 90);
        db.PersonalLaps.Add(new PersonalLap
        {
            UserId = userId,
            CarId = car.Id,
            TrackId = week.TrackId,
            LapTimeSeconds = 60,
            IsValidLap = true,
            RecordedAt = DateTimeOffset.UtcNow,
            Car = car,
            Track = week.Track,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new PercentileCalculationService(db).ComputeAndCacheAsync(
            seriesId: 1,
            weekNumber: 1,
            carId: car.Id,
            customerId: 2,
            evidence: PersonalBestEvidence.FromRequest(true, null),
            callerUserId: userId,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(80, result.YourBestLapSeconds);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_IncludePersonalLaps_PersonalLapFasterThanRace_ImprovesPercentile()
    {
        // Race field: custId=1 (80s), 2 (60s), 3 (70s), 4 (90s), 5 (95s)
        // custId=1 also has a personal lap of 65s.
        // Field of 5 either way; the driver's own race lap (80s) is excluded from the comparison.
        // Without personal laps: driverBest=80, 2 others slower → (2 + 0.5) / 5 = 50th percentile.
        // With personal laps:    driverBest=65, 3 others slower → (3 + 0.5) / 5 = 70th percentile.
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
            .ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1,
                evidence: PersonalBestEvidence.OfficialRaceLapsOnly,
                callerUserId: userId, ct: TestContext.Current.CancellationToken);
        var withPersonal = await new PercentileCalculationService(db)
            .ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1,
                evidence: PersonalBestEvidence.FromRequest(true, null),
                callerUserId: userId, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(withoutPersonal);
        Assert.Equal(50.0, withoutPersonal.PercentileRank);
        Assert.NotNull(withPersonal);
        Assert.Equal(70.0, withPersonal.PercentileRank);
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
            .ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1,
                evidence: OfficialEvidence, ct: TestContext.Current.CancellationToken);

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
        // Personal lap 65s joins a field of 3, making 4: (2 slower + 0.5) / 4 = 62.5th percentile.
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
            .ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1,
                evidence: OfficialEvidence, callerUserId: userId, ct: TestContext.Current.CancellationToken);
        var withPersonal = await new PercentileCalculationService(db)
            .ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1,
                evidence: PersonalBestEvidence.FromRequest(true, null), callerUserId: userId,
                ct: TestContext.Current.CancellationToken);

        Assert.Null(withoutPersonal);
        Assert.NotNull(withPersonal);
        Assert.Equal(62.5, withPersonal.PercentileRank, tolerance: 1e-10);
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
                evidence: PersonalBestEvidence.FromRequest(true, [LapSessionType.Race]),
                callerUserId: userId,
                ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        // 65s beats both others in a Field of 3: (2 + 0.5) / 3.
        Assert.Equal(250.0 / 3, result.PercentileRank, tolerance: 1e-10);
    }

    // ── World-record overlay ──────────────────────────────────────────────────


    [Fact]
    public async Task ComputeAndCacheAsync_PopulatesWorldRecordLapAndGap()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var client = Substitute.For<IDataClient>();
        client.GetWorldRecordsAsync(
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new Aydsko.iRacingData.Common.DataResponse<(Aydsko.iRacingData.Stats.WorldRecordsHeader, Aydsko.iRacingData.Stats.WorldRecordEntry[])>
            {
                Data = (new Aydsko.iRacingData.Stats.WorldRecordsHeader(),
                    [new Aydsko.iRacingData.Stats.WorldRecordEntry { QualifyLapTime = TimeSpan.FromSeconds(66) }]),
            });
        var worldRecords = new WorldRecordService(new CachedIRacingClient(db, client));

        var result = await new PercentileCalculationService(db, worldRecords)
            .ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1,
                evidence: OfficialEvidence,
                ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(66.0, result.WorldRecordLapSeconds);
        Assert.Equal(4.0, result.WorldRecordGapSeconds!.Value, precision: 3); // 70 - 66
    }

    [Fact]
    public async Task ComputeAndCacheAsync_NoWorldRecordService_LeavesWorldRecordNull()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new PercentileCalculationService(db)
            .ComputeAndCacheAsync(seriesId: 1, weekNumber: 1, carId: 1, customerId: 1,
                evidence: OfficialEvidence,
                ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Null(result.WorldRecordLapSeconds);
        Assert.Null(result.WorldRecordGapSeconds);
    }
}
