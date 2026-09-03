using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class CarRecommendationServiceTests
{
    private static PersonalBestEvidence OfficialEvidence => PersonalBestEvidence.OfficialRaceLapsOnly;

    private static (Week week, Car car1, Car car2, CarClass carClass, Subsession subsession) SeedWeekWithTwoCars(AppDbContext db)
    {
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var track = new Track { Id = 99, Name = "Spa", ConfigName = "Full" };
        var week = new Week { Id = Guid.NewGuid(), SeasonId = 1, RaceWeekIndex = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), TrackId = 99, Track = track, Season = season };
        var car1 = new Car { Id = 1, Name = "Porsche 992 GT3", NameAbbreviated = "P992" };
        var car2 = new Car { Id = 2, Name = "Ferrari 296 GT3", NameAbbreviated = "F296" };
        var carClass = new CarClass { Id = 1, Name = "GT3", ShortName = "GT3", RelativeSpeed = 52 };
        var subsession = new Subsession { Id = -1, SeasonId = 1, RaceWeekIndex = 1, WeekId = week.Id, TrackId = 99, StartTime = DateTimeOffset.UtcNow.AddHours(-2) };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Tracks.Add(track);
        db.Weeks.Add(week);
        db.Cars.AddRange(car1, car2);
        db.CarClasses.Add(carClass);
        db.Subsessions.Add(subsession);
        return (week, car1, car2, carClass, subsession);
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

    private static CarRecommendationService CreateService(AppDbContext db) => new(db);

    [Fact]
    public async Task GetRecommendationsAsync_WeekNotFound_ReturnsEmpty()
    {
        await using var db = DbContextFactory.Create();

        var result = await CreateService(db).GetRecommendationsAsync(seriesId: 99, raceWeekIndex: 99, customerId: 1, evidence: OfficialEvidence, ct: TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecommendationsAsync_NoLapsAndNoCachedPercentile_ReturnsEmpty()
    {
        await using var db = DbContextFactory.Create();
        var (week, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);
        // Only driver 999 has a result; customer 1 has no lap and no cached percentile
        AddResult(db, subsession, car1, carClass, custId: 999, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).GetRecommendationsAsync(seriesId: 1, raceWeekIndex: 1, customerId: 1, evidence: OfficialEvidence, ct: TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecommendationsAsync_TwoCars_RanksByFastestActualLap()
    {
        await using var db = DbContextFactory.Create();
        var (week, car1, car2, carClass, subsession) = SeedWeekWithTwoCars(db);
        // car1: driver 1 ran 90s, driver 2 ran 70s
        // car2: driver 11 ran 60s, driver 1 ran another race — driver 1 needs a car2 result
        // Since composite key is (SubsessionId, CustId), driver 1 can only appear once per
        // subsession. Use a second subsession for car2.
        await db.SaveChangesAsync(TestContext.Current.CancellationToken); // flush seed entities first
        AddResult(db, subsession, car1, carClass, custId: 1, lapSeconds: 90);
        AddResult(db, subsession, car1, carClass, custId: 2, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var subsession2 = new Subsession { Id = -2, SeasonId = 1, RaceWeekIndex = 1, WeekId = week.Id, TrackId = 99, StartTime = DateTimeOffset.UtcNow.AddHours(-1) };
        db.Subsessions.Add(subsession2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        AddResult(db, subsession2, car2, carClass, custId: 1, lapSeconds: 60);
        AddResult(db, subsession2, car2, carClass, custId: 3, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).GetRecommendationsAsync(seriesId: 1, raceWeekIndex: 1, customerId: 1, evidence: OfficialEvidence, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].RecommendationRank);
        Assert.Equal(2, result[0].CarId);        // Car2: fastest actual lap (60 s)
        Assert.Equal(60.0, result[0].BestLapSeconds);
        Assert.NotNull(result[0].BestLapSeconds);
        Assert.Equal(LapEvidence.RaceLap, result[0].BestLapEvidence);
        Assert.Equal(2, result[1].RecommendationRank);
        Assert.Equal(1, result[1].CarId);        // Car1: slower actual lap (90 s)
        Assert.Equal(90.0, result[1].BestLapSeconds);
        Assert.NotNull(result[1].BestLapSeconds);
        Assert.Equal(LapEvidence.RaceLap, result[1].BestLapEvidence);
    }

    [Fact]
    public async Task GetRecommendationsAsync_ComputesAverageExpectedPercentileWithinSeriesWhenNoCacheExists()
    {
        await using var db = DbContextFactory.Create();
        var (week, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);

        // Week 1 = current week (no result for driver 1 in car1)
        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 80);
        AddResult(db, subsession, car1, carClass, custId: 300, lapSeconds: 90);
        AddResult(db, subsession, car1, carClass, custId: 400, lapSeconds: 100);

        // Two prior weeks in this Series: ranks 90 and 50, so Expected Percentile is 70.
        var prevTrack = new Track { Id = 88, Name = "Mugello", ConfigName = "GP" };
        var prevWeek = new Week { Id = Guid.NewGuid(), SeasonId = 1, RaceWeekIndex = 2, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14)), TrackId = 88, Track = prevTrack, Season = week.Season };
        var prevWeek2 = new Week { Id = Guid.NewGuid(), SeasonId = 1, RaceWeekIndex = 3, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)), TrackId = 88, Track = prevTrack, Season = week.Season };
        var prevSubsession = new Subsession { Id = -2, SeasonId = 1, RaceWeekIndex = 2, WeekId = prevWeek.Id, TrackId = 88, StartTime = DateTimeOffset.UtcNow.AddDays(-14) };
        var prevSubsession2 = new Subsession { Id = -3, SeasonId = 1, RaceWeekIndex = 3, WeekId = prevWeek2.Id, TrackId = 88, StartTime = DateTimeOffset.UtcNow.AddDays(-7) };
        db.Tracks.Add(prevTrack);
        db.Weeks.AddRange(prevWeek, prevWeek2);
        db.Subsessions.AddRange(prevSubsession, prevSubsession2);
        AddResult(db, prevSubsession, car1, carClass, custId: 1, lapSeconds: 60);
        AddResult(db, prevSubsession, car1, carClass, custId: 101, lapSeconds: 65);
        AddResult(db, prevSubsession, car1, carClass, custId: 102, lapSeconds: 70);
        AddResult(db, prevSubsession, car1, carClass, custId: 103, lapSeconds: 75);
        AddResult(db, prevSubsession, car1, carClass, custId: 104, lapSeconds: 80);
        AddResult(db, prevSubsession2, car1, carClass, custId: 1, lapSeconds: 75);
        AddResult(db, prevSubsession2, car1, carClass, custId: 201, lapSeconds: 65);
        AddResult(db, prevSubsession2, car1, carClass, custId: 202, lapSeconds: 70);
        AddResult(db, prevSubsession2, car1, carClass, custId: 203, lapSeconds: 80);
        AddResult(db, prevSubsession2, car1, carClass, custId: 204, lapSeconds: 85);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).GetRecommendationsAsync(seriesId: 1, raceWeekIndex: 1, customerId: 1, evidence: OfficialEvidence, ct: TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Null(dto.BestLapSeconds);
        // No lap, so no evidence produced one — the two are absent together.
        Assert.Null(dto.BestLapEvidence);
        Assert.Null(dto.PercentileRank);
        Assert.Equal(70, dto.ExpectedPercentile);
        Assert.Null(dto.TopSharePercent);
        Assert.Null(dto.FieldSize);
        Assert.False(dto.IsPercentilePresentable);
        // 70th percentile in [70, 80, 90, 100]: pos = 3 x 0.3 = 0.9 → 79 s.
        Assert.Equal(79, dto.ProjectedLapSeconds, tolerance: 1e-6);
    }

    [Fact]
    public async Task GetRecommendationsAsync_ProjectsLapTimeFromCachedPercentile_WhenNoLapThisWeek()
    {
        await using var db = DbContextFactory.Create();
        var (week, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);

        // Three other drivers in week 1 / car1 with sorted laps: 70, 80, 90
        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 80);
        AddResult(db, subsession, car1, carClass, custId: 300, lapSeconds: 90);

        // Customer 1 is linked to a user account with a cached 50th-percentile result for car1
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        db.CarPercentileResults.Add(new CarPercentileResult
        {
            UserId = userId, CarId = 1, SeriesId = 1, WeekId = Guid.NewGuid(), // some previous week's guid
            PercentileRank = 50.0, SampleSize = 100, ComputedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).GetRecommendationsAsync(seriesId: 1, raceWeekIndex: 1, customerId: 1, evidence: OfficialEvidence, ct: TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Equal(1, dto.CarId);
        Assert.Null(dto.BestLapSeconds);
        Assert.Null(dto.BestLapEvidence);
        Assert.Null(dto.PercentileRank);
        Assert.Equal(50.0, dto.ExpectedPercentile);
        Assert.Null(dto.FieldSize);
        Assert.False(dto.IsPercentilePresentable);
        // 50th percentile in [70, 80, 90]: pos = (3-1) * (1-0.5) = 1.0 → index 1 = 80 s
        Assert.Equal(80.0, dto.ProjectedLapSeconds, tolerance: 1e-6);
    }

    [Fact]
    public async Task GetRecommendationsAsync_UploadedLapPath_ExcludesCarWhenFlagOff()
    {
        // Same setup as above but includeUploadedLaps=false → car should not appear.
        await using var db = DbContextFactory.Create();
        var (week, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);

        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 80);

        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        db.UploadedLaps.Add(new UploadedLap
        {
            UserId = userId, CarId = 1, TrackId = 99,
            LapTimeSeconds = 65.0,
            SessionType = LapSessionType.Race,
            RecordedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).GetRecommendationsAsync(
            seriesId: 1, raceWeekIndex: 1, customerId: 1,
            evidence: OfficialEvidence,
            ct: TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecommendationsAsync_ActualPath_UpdatesExistingCacheRow_AndSwapsRunningAverage()
    {
        // A cache row already exists for THIS week, so the running-average takes the swap branch
        // (prior.Sum - oldReading + new) and the cache is updated in place — not duplicated.
        await using var db = DbContextFactory.Create();
        var (week, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        AddResult(db, subsession, car1, carClass, custId: 1, lapSeconds: 60); // caller — fastest
        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 80);
        AddResult(db, subsession, car1, carClass, custId: 300, lapSeconds: 90);
        AddResult(db, subsession, car1, carClass, custId: 400, lapSeconds: 100);

        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        db.CarPercentileResults.Add(new CarPercentileResult
        {
            UserId = userId, CarId = car1.Id, SeriesId = 1, WeekId = week.Id,
            PercentileRank = 40.0, SampleSize = 99, ComputedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).GetRecommendationsAsync(
            seriesId: 1, raceWeekIndex: 1, customerId: 1,
            evidence: OfficialEvidence,
            ct: TestContext.Current.CancellationToken);

        var dto = result.Single(r => r.CarId == car1.Id);
        // Field of 5: (4 slower + 0.5 tied) / 5.
        Assert.Equal(90, dto.PercentileRank!.Value, tolerance: 1e-10);
        Assert.Equal(90, dto.ExpectedPercentile!.Value, tolerance: 1e-10);
        Assert.Equal(5, dto.FieldSize);

        var rows = db.CarPercentileResults
            .Where(r => r.UserId == userId && r.CarId == car1.Id && r.WeekId == week.Id).ToList();
        Assert.Single(rows);                  // updated in place, not duplicated
        Assert.Equal(90, rows[0].PercentileRank, tolerance: 1e-10);
        Assert.Equal(20, rows[0].TopSharePercent); // 1st of 5
        Assert.Equal(5, rows[0].SampleSize);  // refreshed to the current field size
    }

    [Fact]
    public async Task GetMyPercentilesAsync_ReturnsOnlyCarsTheCallerRaced()
    {
        await using var db = DbContextFactory.Create();
        var (_, car1, car2, carClass, subsession) = SeedWeekWithTwoCars(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // car1: caller raced it (60 s) plus two others → actual path (BestLapSeconds set).
        AddResult(db, subsession, car1, carClass, custId: 1, lapSeconds: 60);
        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 80);
        AddResult(db, subsession, car1, carClass, custId: 500, lapSeconds: 90);
        AddResult(db, subsession, car1, carClass, custId: 600, lapSeconds: 100);
        // car2: only other drivers raced it (distinct cust_ids — the composite key is
        // (SubsessionId, CustId)); the caller has a cached percentile from another week, so it
        // surfaces as a projected-only recommendation (BestLapSeconds null) → excluded.
        AddResult(db, subsession, car2, carClass, custId: 300, lapSeconds: 75);
        AddResult(db, subsession, car2, carClass, custId: 400, lapSeconds: 85);

        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        db.UploadedLaps.Add(new UploadedLap
        {
            UserId = userId,
            CarId = car2.Id,
            TrackId = 99,
            LapTimeSeconds = 65,
            SessionType = LapSessionType.Race,
            RecordedAt = DateTimeOffset.UtcNow,
        });
        db.CarPercentileResults.Add(new CarPercentileResult
        {
            UserId = userId, CarId = car2.Id, SeriesId = 1, WeekId = Guid.NewGuid(),
            PercentileRank = 50.0, SampleSize = 10, ComputedAt = DateTimeOffset.UtcNow.AddDays(-7),
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).GetMyPercentilesAsync(
            seriesId: 1, raceWeekIndex: 1, customerId: 1,
            evidence: PersonalBestEvidence.OfficialRaceLapsOnly,
            ct: TestContext.Current.CancellationToken);

        var entry = Assert.Single(result); // only car1 (raced); car2 is projected-only → excluded
        Assert.Equal(car1.Id, entry.CarId);
        // Field of 5: (4 slower + 0.5 tied) / 5.
        Assert.Equal(90, entry.PercentileRank, tolerance: 1e-10);
        Assert.Equal(20, entry.TopSharePercent); // 1st of 5
    }

    [Fact]
    public async Task GetRecommendationsAsync_UndersizedCurrentField_HasNoExpectedPercentileWithoutHistory()
    {
        await using var db = DbContextFactory.Create();
        var (_, car, _, carClass, subsession) = SeedWeekWithTwoCars(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 70);
        db.Users.Add(new ApplicationUser { Id = Guid.NewGuid(), IRacingCustomerId = 1, DisplayName = "Driver" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var dto = Assert.Single(await CreateService(db).GetRecommendationsAsync(
            1, 1, 1, OfficialEvidence, TestContext.Current.CancellationToken));

        Assert.NotNull(dto.PercentileRank);
        Assert.Null(dto.ExpectedPercentile);
        Assert.Equal(2, dto.FieldSize);
        Assert.False(dto.IsPercentilePresentable);
        Assert.True(dto.ProjectedLapSeconds > 0);
    }

    [Fact]
    public async Task GetRecommendationsAsync_UndersizedRowBecomesPresentable_AddsInsteadOfSubtractingOldReading()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, _, carClass, subsession) = SeedWeekWithTwoCars(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 70);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 80);
        AddResult(db, subsession, car, carClass, custId: 4, lapSeconds: 90);
        AddResult(db, subsession, car, carClass, custId: 5, lapSeconds: 100);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        db.CarPercentileResults.AddRange(
            new CarPercentileResult { UserId = userId, CarId = car.Id, SeriesId = 1, WeekId = week.Id, PercentileRank = 40, SampleSize = 3, ComputedAt = DateTimeOffset.UtcNow.AddDays(-1) },
            new CarPercentileResult { UserId = userId, CarId = car.Id, SeriesId = 1, WeekId = Guid.NewGuid(), PercentileRank = 80, SampleSize = 5, ComputedAt = DateTimeOffset.UtcNow.AddDays(-7) });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var dto = Assert.Single(await CreateService(db).GetRecommendationsAsync(
            1, 1, 1, OfficialEvidence, TestContext.Current.CancellationToken));

        Assert.Equal(90, dto.PercentileRank);
        Assert.Equal(85, dto.ExpectedPercentile); // (prior 80 + current 90) / 2; old 40 was excluded.
    }

    [Fact]
    public async Task GetRecommendationsAsync_PresentableRowBecomesUndersized_RemovesOldReadingFromExpectedPercentile()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, _, carClass, subsession) = SeedWeekWithTwoCars(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 70);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        db.CarPercentileResults.AddRange(
            new CarPercentileResult { UserId = userId, CarId = car.Id, SeriesId = 1, WeekId = week.Id, PercentileRank = 40, SampleSize = 5, ComputedAt = DateTimeOffset.UtcNow.AddDays(-1) },
            new CarPercentileResult { UserId = userId, CarId = car.Id, SeriesId = 1, WeekId = Guid.NewGuid(), PercentileRank = 80, SampleSize = 5, ComputedAt = DateTimeOffset.UtcNow.AddDays(-7) });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var dto = Assert.Single(await CreateService(db).GetRecommendationsAsync(
            1, 1, 1, OfficialEvidence, TestContext.Current.CancellationToken));

        Assert.NotNull(dto.PercentileRank);
        Assert.Equal(80, dto.ExpectedPercentile); // old current-week 40 was removed; new rank is undersized.
        Assert.Equal(2, dto.FieldSize);
        Assert.False(dto.IsPercentilePresentable);
    }

    // ── RunningAveragePercentile (pure helper) ───────────────────────────────

    [Fact]
    public void RunningAveragePercentile_NoPriorHistory_ReturnsReadingItself()
    {
        Assert.Equal(80.0, CarRecommendationService.RunningAveragePercentile(80.0, prior: null, oldReading: null));
    }

    [Fact]
    public void RunningAveragePercentile_NewWeekRow_AddsToSumAndGrowsCount()
    {
        // prior = (Sum 120 over 2 readings), new reading 90 → (120 + 90) / (2 + 1) = 70.
        var result = CarRecommendationService.RunningAveragePercentile(90.0, prior: (120.0, 2), oldReading: null);
        Assert.Equal(70.0, result, tolerance: 1e-9);
    }

    [Fact]
    public void RunningAveragePercentile_ExistingWeekRow_SwapsOldReadingKeepingCount()
    {
        // prior = (Sum 150 over 3 readings); this week's row already contributed 40, now reads 100.
        // (150 - 40 + 100) / 3 = 70.
        var result = CarRecommendationService.RunningAveragePercentile(100.0, prior: (150.0, 3), oldReading: 40.0);
        Assert.Equal(70.0, result, tolerance: 1e-9);
    }

    [Fact]
    public void RunningAveragePercentile_ExistingWeekRow_WithZeroCount_FallsBackToReading()
    {
        // Guards against divide-by-zero when an existing-week row is present but the prior count is 0
        // (unreachable via the integration path, where the GroupBy always yields Count >= 1).
        var result = CarRecommendationService.RunningAveragePercentile(55.0, prior: (0.0, 0), oldReading: 40.0);
        Assert.Equal(55.0, result, tolerance: 1e-9);
    }
}
