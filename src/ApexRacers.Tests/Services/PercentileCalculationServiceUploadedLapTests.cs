using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>Quarantined uploads never become personal evidence through a claim-only legacy API.
/// Authorized typed selection/window behavior is exercised through the shared PostgreSQL refresh.</summary>
[Collection(PostgreSqlCollection.Name)]
public class PercentileCalculationServiceUploadedLapTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Race Week 1 runs for the seven days from this date unless a test says otherwise.</summary>
    private static readonly DateOnly WeekStart = new(2026, 6, 15);

    private static readonly DateTimeOffset InsideWeek = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BeforeWeek = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    private static (Week week, Car car, CarClass carClass, Subsession subsession) SeedWeekAndCar(
        AppDbContext db, DateTimeOffset? weekEnd = null)
    {
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var track = new Track { Id = 99, Name = "Spa", ConfigName = "Full" };
        var week = new Week
        {
            Id = Guid.NewGuid(), SeasonId = 1, RaceWeekIndex = 1, StartDate = WeekStart,
            EndTime = weekEnd, TrackId = 99, Track = track, Season = season,
        };
        var car = new Car { Id = 1, Name = "Porsche 992 GT3", NameAbbreviated = "P992" };
        var carClass = new CarClass { Id = 1, Name = "GT3", ShortName = "GT3", RelativeSpeed = 52 };
        var subsession = new Subsession
        {
            Id = -1, SeasonId = 1, RaceWeekIndex = 1, WeekId = week.Id, TrackId = 99,
            StartTime = new DateTimeOffset(2026, 6, 17, 10, 0, 0, TimeSpan.Zero),
        };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Tracks.Add(track);
        db.Weeks.Add(week);
        db.Cars.Add(car);
        db.CarClasses.Add(carClass);
        db.Subsessions.Add(subsession);
        return (week, car, carClass, subsession);
    }

    private static void AddResult(
        AppDbContext db, Subsession subsession, Car car, CarClass carClass, long custId, double lapSeconds) =>
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

    private static void AddUploadedLap(
        AppDbContext db, Guid userId, Car car, Track track, double lapSeconds,
        DateTimeOffset recordedAt, LapSessionType sessionType = LapSessionType.Practice) =>
        db.UploadedLaps.Add(new UploadedLap
        {
            UserId = userId, CarId = car.Id, TrackId = track.Id,
            LapTimeSeconds = lapSeconds, SessionType = sessionType,
            RecordedAt = recordedAt, Car = car, Track = track,
        });

    private static Task<PercentileResultDto?> ComputeAsync(
        AppDbContext db, Guid userId, PersonalBestEvidence evidence) =>
        new PercentileCalculationService(db, new SubjectDriverContext(db, new FeatureFlagEligibility(db))).ComputeAndCacheAsync(
            seriesId: 1, raceWeekIndex: 1, carId: 1, customerId: 1,
            evidence: evidence, callerUserId: userId, ct: Ct);


    [Fact]
    public async Task ClaimedInWeekQuarantinedUpload_CannotChangeRaceReading()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 80);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 70);
        AddResult(db, subsession, car, carClass, custId: 4, lapSeconds: 90);
        AddResult(db, subsession, car, carClass, custId: 5, lapSeconds: 95);
        AddUploadedLap(db, userId, car, week.Track, lapSeconds: 65, recordedAt: InsideWeek);
        await db.SaveChangesAsync(Ct);

        var official = await ComputeAsync(db, userId, PersonalBestEvidence.OfficialRaceLapsOnly);
        var withUploaded = await ComputeAsync(db, userId, PersonalBestEvidence.FromRequest(true, null));

        Assert.NotNull(official);
        Assert.Equal(50.0, official.PercentileRank);
        Assert.Equal(LapEvidence.RaceLap, official.YourBestLapEvidence);

        Assert.NotNull(withUploaded);
        Assert.Equal(50.0, withUploaded.PercentileRank);
        Assert.Equal(LapEvidence.RaceLap, withUploaded.YourBestLapEvidence);
    }

    [Fact]
    public async Task UploadedLapOutsideTheRaceWeek_DoesNotCount()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 80);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 70);
        AddResult(db, subsession, car, carClass, custId: 4, lapSeconds: 90);
        AddResult(db, subsession, car, carClass, custId: 5, lapSeconds: 95);
        AddUploadedLap(db, userId, car, week.Track, lapSeconds: 65, recordedAt: BeforeWeek);
        await db.SaveChangesAsync(Ct);

        var result = await ComputeAsync(db, userId, PersonalBestEvidence.FromRequest(true, null));

        Assert.NotNull(result);
        Assert.Equal(80.0, result.YourBestLapSeconds);
        Assert.Equal(LapEvidence.RaceLap, result.YourBestLapEvidence);
        Assert.Equal(50.0, result.PercentileRank);
    }

    [Fact]
    public async Task QuarantinedUploadAfterReportedWeekEndCannotSupplyEvidence()
    {
        var reportedEnd = new DateTimeOffset(2026, 6, 21, 21, 0, 0, TimeSpan.Zero);
        var afterClose = new DateTimeOffset(2026, 6, 21, 22, 0, 0, TimeSpan.Zero);

        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (week, car, carClass, subsession) = SeedWeekAndCar(db, weekEnd: reportedEnd);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 80);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 90);
        AddUploadedLap(db, userId, car, week.Track, lapSeconds: 65, recordedAt: afterClose);
        await db.SaveChangesAsync(Ct);

        var result = await ComputeAsync(db, userId, PersonalBestEvidence.FromRequest(true, null));

        Assert.NotNull(result);
        Assert.Equal(80.0, result.YourBestLapSeconds);
        Assert.Equal(LapEvidence.RaceLap, result.YourBestLapEvidence);
    }


    [Fact]
    public async Task QuarantinedUploadDatesAndTimes_AreNotDisclosed()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 80);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 90);
        AddUploadedLap(db, userId, car, week.Track, lapSeconds: 65, recordedAt: BeforeWeek);
        await db.SaveChangesAsync(Ct);

        var result = await ComputeAsync(db, userId, PersonalBestEvidence.FromRequest(true, null));

        Assert.NotNull(result);
        var excluded = result.UploadedBestOutsideWeek;
        Assert.Null(excluded);
        Assert.Equal(80.0, result.YourBestLapSeconds);
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
    }

    [Fact]
    public async Task SlowerUploadedLapOutsideTheWeek_IsNotReported()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 80);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 90);
        AddUploadedLap(db, userId, car, week.Track, lapSeconds: 95, recordedAt: BeforeWeek);
        await db.SaveChangesAsync(Ct);

        var result = await ComputeAsync(db, userId, PersonalBestEvidence.FromRequest(true, null));

        Assert.NotNull(result);
        Assert.Null(result.UploadedBestOutsideWeek);
    }

    [Fact]
    public async Task OfficialRaceLapsOnly_ReportsNothingExcluded()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 80);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 90);
        AddUploadedLap(db, userId, car, week.Track, lapSeconds: 65, recordedAt: BeforeWeek);
        await db.SaveChangesAsync(Ct);

        var result = await ComputeAsync(db, userId, PersonalBestEvidence.OfficialRaceLapsOnly);

        Assert.NotNull(result);
        Assert.Null(result.UploadedBestOutsideWeek);
    }


    [Fact]
    public async Task DriverNotInRaceField_QuarantinedUploadCannotInventAPercentile()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 70);
        AddResult(db, subsession, car, carClass, custId: 4, lapSeconds: 80);
        AddUploadedLap(db, userId, car, week.Track, lapSeconds: 65, recordedAt: InsideWeek);
        await db.SaveChangesAsync(Ct);

        var official = await ComputeAsync(db, userId, PersonalBestEvidence.OfficialRaceLapsOnly);
        var withUploaded = await ComputeAsync(db, userId, PersonalBestEvidence.FromRequest(true, null));

        Assert.Null(official);
        Assert.Null(withUploaded);
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
    }

    [Fact]
    public async Task UnknownQuarantinedLapCannotBypassTheAuthorityBoundary()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 70);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 80);
        AddUploadedLap(db, userId, car, week.Track, lapSeconds: 65, recordedAt: InsideWeek,
            sessionType: LapSessionType.Unknown);
        await db.SaveChangesAsync(Ct);

        var result = await ComputeAsync(db, userId, PersonalBestEvidence.FromRequest(true, [LapSessionType.Race]));

        Assert.Null(result);
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
    }

    [Fact]
    public async Task UploadedLapSlowerThanTheRaceBest_KeepsRaceLapEvidence()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 80);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 90);
        AddUploadedLap(db, userId, car, week.Track, lapSeconds: 85, recordedAt: InsideWeek);
        await db.SaveChangesAsync(Ct);

        var result = await ComputeAsync(db, userId, PersonalBestEvidence.FromRequest(true, null));

        Assert.NotNull(result);
        Assert.Equal(80.0, result.YourBestLapSeconds);
        Assert.Equal(LapEvidence.RaceLap, result.YourBestLapEvidence);
    }
}
