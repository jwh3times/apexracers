using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>
/// The Uploaded Lap side of car recommendations, which is bounded to the Race Week being
/// recommended for.
///
/// <para><b>Why PostgreSQL rather than the fast SQLite context.</b> Scoping Uploaded Laps to a
/// Race Week filters on a <see cref="DateTimeOffset"/> range, which the production provider
/// translates and SQLite does not — it fails at runtime rather than at compile time. See
/// <see cref="PercentileCalculationServiceUploadedLapTests"/>, which splits for the same reason.</para>
/// </summary>
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
            Id = Guid.NewGuid(), SeasonId = 1, WeekNumber = 1, StartDate = WeekStart,
            TrackId = 99, Track = track, Season = season,
        };
        var car1 = new Car { Id = 1, Name = "Porsche 992 GT3", NameAbbreviated = "P992" };
        var car2 = new Car { Id = 2, Name = "Ferrari 296 GT3", NameAbbreviated = "F296" };
        var carClass = new CarClass { Id = 1, Name = "GT3", ShortName = "GT3", RelativeSpeed = 52 };
        var subsession = new Subsession
        {
            Id = -1, SeasonId = 1, WeekNumber = 1, WeekId = week.Id, TrackId = 99,
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
            LapTimeSeconds = lapSeconds, IsValidLap = true, SessionType = sessionType,
            RecordedAt = recordedAt,
        });

    private static Task<List<CarRecommendationDto>> RecommendAsync(
        AppDbContext db, PersonalBestEvidence evidence) =>
        new CarRecommendationService(db).GetRecommendationsAsync(
            seriesId: 1, weekNumber: 1, customerId: 1, evidence: evidence, ct: Ct);

    // ── The Race Week bound ───────────────────────────────────────────────────

    [Fact]
    public async Task UploadedLapOutsideTheRaceWeek_DoesNotReachTheRecommendation()
    {
        // The driver raced this car at 90s and holds a 65s uploaded lap from before the week
        // opened. Recommendations must rank them on the week's evidence, not on an older lap.
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
        // The uploaded-lap path is what puts a car the driver has not raced onto the board. With
        // the lap outside the week there is no evidence for this week, so the car drops out
        // rather than being ranked on a lap from another one.
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

    // ── Cases carried over from the SQLite suite ──────────────────────────────

    [Fact]
    public async Task InWeekUploadedLap_PutsACarTheDriverDidNotRaceOnTheBoard()
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

        var dto = Assert.Single(await RecommendAsync(db, PersonalBestEvidence.FromRequest(true, null)));

        Assert.Equal(1, dto.CarId);
        Assert.Equal(65.0, dto.BestLapSeconds);
        Assert.Equal(LapEvidence.UploadedLap, dto.BestLapEvidence);
        // 65s beats all 3 in a Field of 4: 3 + 0.5, over 4 = 87.5%.
        Assert.Equal(87.5, dto.PercentileRank, tolerance: 1e-6);
        Assert.Equal(25, dto.TopSharePercent); // 1st of 4
    }

    [Fact]
    public async Task InWeekUploadedLap_ReplacesASlowerRaceLapAndSaysSo()
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

        Assert.Equal(65.0, dto.BestLapSeconds);
        // The driver did race this car this week, so the number alone cannot tell them the ranked
        // lap was an uploaded one. The evidence is what makes that legible.
        Assert.Equal(LapEvidence.UploadedLap, dto.BestLapEvidence);
    }

    [Fact]
    public async Task SessionTypeFilter_ExcludesAMismatchedUploadedLap()
    {
        // The uploaded lap is a Practice lap and the filter is Race-only, so the car drops out.
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
    public async Task SessionTypeFilter_StillIncludesAnUnknownTypedLap()
    {
        // Laps uploaded before SessionType tracking was added have SessionType=Unknown. They must
        // always be included when a filter is active, never silently excluded.
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var (_, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);
        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 80);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        AddUploadedLap(db, userId, carId: 1, lapSeconds: 65.0, recordedAt: InsideWeek,
            sessionType: LapSessionType.Unknown);
        await db.SaveChangesAsync(Ct);

        var dto = Assert.Single(await RecommendAsync(db, PersonalBestEvidence.FromRequest(true, null)));

        Assert.Equal(65.0, dto.BestLapSeconds);
    }

    [Fact]
    public async Task UploadedLapPath_UpdatesAnExistingCacheRowInPlace()
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

        var dto = result.Single(r => r.CarId == car1.Id);
        Assert.Equal(65.0, dto.BestLapSeconds);
        Assert.Equal(87.5, dto.PercentileRank); // 65s beats all 3 in a Field of 4

        var rows = db.CarPercentileResults
            .Where(r => r.UserId == userId && r.CarId == car1.Id && r.WeekId == week.Id).ToList();
        Assert.Single(rows);                  // updated in place, not duplicated
        Assert.Equal(87.5, rows[0].PercentileRank);
    }
}
