using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>Legacy uploaded rows and unclassified history cannot select a User or a Personal Best.
/// Positive typed upload calculations belong to the authorized shared refresh tests.</summary>
[Collection(PostgreSqlCollection.Name)]
public class CarRecommendationServiceUploadedLapTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly WeekStart = new(2026, 6, 15);
    private static readonly DateTimeOffset InsideWeek = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BeforeWeek = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    private static (Week week, Car car1, Car car2, CarClass carClass, Subsession subsession)
        SeedWeekWithTwoCars(AppDbContext db)
    {
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var track = new Track { Id = 99, Name = "Spa", ConfigName = "Full" };
        var week = new Week
        {
            Id = Guid.NewGuid(), SeasonId = 1, RaceWeekIndex = 1, StartDate = WeekStart,
            TrackId = 99, Track = track, Season = season,
        };
        var car1 = new Car { Id = 1, Name = "Porsche 992 GT3", NameAbbreviated = "P992" };
        var car2 = new Car { Id = 2, Name = "Ferrari 296 GT3", NameAbbreviated = "F296" };
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
        db.Cars.AddRange(car1, car2);
        db.CarClasses.Add(carClass);
        db.Subsessions.Add(subsession);
        return (week, car1, car2, carClass, subsession);
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
        AppDbContext db, Guid userId, int carId, double lapSeconds, DateTimeOffset recordedAt,
        LapSessionType sessionType = LapSessionType.Race) =>
        db.UploadedLaps.Add(new UploadedLap
        {
            UserId = userId, CarId = carId, TrackId = 99,
            LapTimeSeconds = lapSeconds, SessionType = sessionType,
            RecordedAt = recordedAt,
        });

    private static async Task<List<CarRecommendationDto>> RecommendAsync(
        AppDbContext db, PersonalBestEvidence evidence) =>
        await new CarRecommendationService(db).GetRecommendationsAsync(
            1, 1, 1, evidence, await db.Users.Select(u => u.Id).SingleAsync(Ct), Ct);

    private static async Task<List<WeekCarPercentileDto>> GetMyPercentilesAsync(
        AppDbContext db, PersonalBestEvidence evidence) =>
        await new CarRecommendationService(db).GetMyPercentilesAsync(
            1, 1, 1, evidence, await db.Users.Select(u => u.Id).SingleAsync(Ct), Ct);


    [Fact]
    public async Task UploadedLapOutsideTheRaceWeek_DoesNotReachTheRecommendation()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (_, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);
        AddResult(db, subsession, car1, carClass, custId: 1, lapSeconds: 90);
        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 80);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 70);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        AddUploadedLap(db, userId, carId: 1, lapSeconds: 65.0, recordedAt: BeforeWeek);
        await db.SaveChangesAsync(Ct);

        var dto = Assert.Single(await RecommendAsync(db, PersonalBestEvidence.FromRequest(true, null)));

        Assert.Equal(90.0, dto.BestLapSeconds);
        Assert.Equal(LapEvidence.RaceLap, dto.BestLapEvidence);
    }

    [Fact]
    public async Task CarWithOnlyAnOutOfWeekUploadedLap_DoesNotAppearAtAll()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (_, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);
        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 80);
        AddResult(db, subsession, car1, carClass, custId: 300, lapSeconds: 90);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        AddUploadedLap(db, userId, carId: 1, lapSeconds: 65.0, recordedAt: BeforeWeek);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await RecommendAsync(db, PersonalBestEvidence.FromRequest(true, null)));
    }


    [Fact]
    public async Task InWeekQuarantinedUploadCannotPutAnUnracedCarOnTheBoard()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (_, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);
        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 80);
        AddResult(db, subsession, car1, carClass, custId: 300, lapSeconds: 90);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        AddUploadedLap(db, userId, carId: 1, lapSeconds: 65.0, recordedAt: InsideWeek);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await RecommendAsync(db, PersonalBestEvidence.FromRequest(true, null)));
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
    }

    [Fact]
    public async Task InWeekQuarantinedUploadCannotReplaceRaceLap()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (_, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);
        AddResult(db, subsession, car1, carClass, custId: 1, lapSeconds: 90); // the driver's race lap
        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 80);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 70);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        AddUploadedLap(db, userId, carId: 1, lapSeconds: 65.0, recordedAt: InsideWeek);
        await db.SaveChangesAsync(Ct);

        var dto = Assert.Single(await RecommendAsync(db, PersonalBestEvidence.FromRequest(true, null)));

        Assert.Equal(90.0, dto.BestLapSeconds);
        Assert.Equal(LapEvidence.RaceLap, dto.BestLapEvidence);
    }

    [Fact]
    public async Task GetMyPercentilesAsync_QuarantinedUploadRetainsRaceEvidence()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (_, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);
        AddResult(db, subsession, car1, carClass, custId: 1, lapSeconds: 90);
        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 80);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 300, lapSeconds: 75);
        AddResult(db, subsession, car1, carClass, custId: 400, lapSeconds: 85);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        AddUploadedLap(db, userId, carId: car1.Id, lapSeconds: 65.0, recordedAt: InsideWeek);
        await db.SaveChangesAsync(Ct);

        var dto = Assert.Single(await GetMyPercentilesAsync(
            db, PersonalBestEvidence.FromRequest(true, null)));

        Assert.Equal(LapEvidence.RaceLap, dto.PersonalBestLapEvidence);
    }

    [Fact]
    public async Task SessionTypeFilter_ExcludesAMismatchedUploadedLap()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (_, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);
        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 80);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        AddUploadedLap(db, userId, carId: 1, lapSeconds: 65.0, recordedAt: InsideWeek,
            sessionType: LapSessionType.Practice);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await RecommendAsync(db, PersonalBestEvidence.FromRequest(true, [LapSessionType.Race])));
    }

    [Fact]
    public async Task UnknownQuarantinedUploadCannotInventPersonalEvidence()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (_, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);
        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 80);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        AddUploadedLap(db, userId, carId: 1, lapSeconds: 65.0, recordedAt: InsideWeek,
            sessionType: LapSessionType.Unknown);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await RecommendAsync(db, PersonalBestEvidence.FromRequest(true, [LapSessionType.Race])));
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
    }

    [Fact]
    public async Task QuarantinedUploadAndLegacyCacheCannotProduceOrRefreshAReading()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (week, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);
        await db.SaveChangesAsync(Ct);

        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 80);
        AddResult(db, subsession, car1, carClass, custId: 300, lapSeconds: 90);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        AddUploadedLap(db, userId, carId: car1.Id, lapSeconds: 65.0, recordedAt: InsideWeek);
        db.CarPercentileResults.Add(new CarPercentileResult
        {
            UserId = userId, CarId = car1.Id, SeriesId = 1, WeekId = week.Id,
            PercentileRank = 50.0, SampleSize = 10,
            ComputedAt = new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync(Ct);

        var result = await RecommendAsync(db, PersonalBestEvidence.FromRequest(true, null));

        Assert.Empty(result);
        var legacy = Assert.Single(await db.CarPercentileResults.ToListAsync(Ct));
        Assert.Equal(50, legacy.PercentileRank);
        Assert.Equal(new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero), legacy.ComputedAt);
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
    }
}
