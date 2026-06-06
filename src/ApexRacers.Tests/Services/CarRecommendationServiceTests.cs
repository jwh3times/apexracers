using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class CarRecommendationServiceTests
{
    private static (Week week, Car car1, Car car2, CarClass carClass, Subsession subsession) SeedWeekWithTwoCars(AppDbContext db)
    {
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var track = new Track { Id = 99, Name = "Spa", ConfigName = "Full" };
        var week = new Week { Id = Guid.NewGuid(), SeasonId = 1, WeekNumber = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), TrackId = 99, Track = track, Season = season };
        var car1 = new Car { Id = 1, Name = "Porsche 992 GT3", NameAbbreviated = "P992" };
        var car2 = new Car { Id = 2, Name = "Ferrari 296 GT3", NameAbbreviated = "F296" };
        var carClass = new CarClass { Id = 1, Name = "GT3", ShortName = "GT3", RelativeSpeed = 52 };
        var subsession = new Subsession { Id = -1, SeasonId = 1, WeekNumber = 1, WeekId = week.Id, TrackId = 99, StartTime = DateTimeOffset.UtcNow.AddHours(-2) };
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

        var result = await CreateService(db).GetRecommendationsAsync(seriesId: 99, weekNumber: 99, customerId: 1, ct: TestContext.Current.CancellationToken);

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

        var result = await CreateService(db).GetRecommendationsAsync(seriesId: 1, weekNumber: 1, customerId: 1, ct: TestContext.Current.CancellationToken);

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

        var subsession2 = new Subsession { Id = -2, SeasonId = 1, WeekNumber = 1, WeekId = week.Id, TrackId = 99, StartTime = DateTimeOffset.UtcNow.AddHours(-1) };
        db.Subsessions.Add(subsession2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        AddResult(db, subsession2, car2, carClass, custId: 1, lapSeconds: 60);
        AddResult(db, subsession2, car2, carClass, custId: 3, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).GetRecommendationsAsync(seriesId: 1, weekNumber: 1, customerId: 1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Rank);
        Assert.Equal(2, result[0].CarId);        // Car2: fastest actual lap (60 s)
        Assert.Equal(60.0, result[0].BestLapSeconds);
        Assert.NotNull(result[0].BestLapSeconds);
        Assert.Equal(2, result[1].Rank);
        Assert.Equal(1, result[1].CarId);        // Car1: slower actual lap (90 s)
        Assert.Equal(90.0, result[1].BestLapSeconds);
        Assert.NotNull(result[1].BestLapSeconds);
    }

    [Fact]
    public async Task GetRecommendationsAsync_ComputesHistoricalPercentileWhenNoCacheExists()
    {
        await using var db = DbContextFactory.Create();
        var (week, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);

        // Week 1 = current week (no result for driver 1 in car1)
        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 80);
        AddResult(db, subsession, car1, carClass, custId: 300, lapSeconds: 90);

        // Previous week — driver 1 ran 60 s and beat all 3 others (100%)
        var series2 = new Series { Id = 2, Name = "Other Series" };
        var season2 = new Season { Id = 2, SeriesId = 2, Year = 2026, Quarter = 2, Active = true, Series = series2 };
        var prevTrack = new Track { Id = 88, Name = "Mugello", ConfigName = "GP" };
        var prevWeek = new Week { Id = Guid.NewGuid(), SeasonId = 2, WeekNumber = 5, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14)), TrackId = 88, Track = prevTrack, Season = season2 };
        var prevSubsession = new Subsession { Id = -2, SeasonId = 2, WeekNumber = 5, WeekId = prevWeek.Id, TrackId = 88, StartTime = DateTimeOffset.UtcNow.AddDays(-14) };
        db.Series.Add(series2);
        db.Seasons.Add(season2);
        db.Tracks.Add(prevTrack);
        db.Weeks.Add(prevWeek);
        db.Subsessions.Add(prevSubsession);
        AddResult(db, prevSubsession, car1, carClass, custId: 1, lapSeconds: 60);
        AddResult(db, prevSubsession, car1, carClass, custId: 101, lapSeconds: 65);
        AddResult(db, prevSubsession, car1, carClass, custId: 102, lapSeconds: 70);
        AddResult(db, prevSubsession, car1, carClass, custId: 103, lapSeconds: 75);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).GetRecommendationsAsync(seriesId: 1, weekNumber: 1, customerId: 1, ct: TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Null(dto.BestLapSeconds);
        Assert.Equal(100.0, dto.PercentileRank); // beat all 3 others in prev week
        // 100th percentile in [70, 80, 90]: pos = 0 → fastest = 70 s
        Assert.Equal(70.0, dto.ProjectedLapSeconds, tolerance: 1e-6);
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

        var result = await CreateService(db).GetRecommendationsAsync(seriesId: 1, weekNumber: 1, customerId: 1, ct: TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Equal(1, dto.CarId);
        Assert.Null(dto.BestLapSeconds);
        Assert.Equal(50.0, dto.PercentileRank);
        // 50th percentile in [70, 80, 90]: pos = (3-1) * (1-0.5) = 1.0 → index 1 = 80 s
        Assert.Equal(80.0, dto.ProjectedLapSeconds, tolerance: 1e-6);
    }

    [Fact]
    public async Task GetRecommendationsAsync_PersonalLapPath_IncludesCarWhenNoSubsessionResult()
    {
        // Driver has no SubsessionResult for car1 this week but uploaded a personal lap.
        // With includePersonalLaps=true the car should appear using the personal lap for percentile.
        await using var db = DbContextFactory.Create();
        var (week, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);

        // Three other drivers in the field with sorted laps: 70, 80, 90
        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 80);
        AddResult(db, subsession, car1, carClass, custId: 300, lapSeconds: 90);

        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        // Personal lap at the same track — 65s beats all three others
        db.PersonalLaps.Add(new PersonalLap
        {
            UserId = userId, CarId = 1, TrackId = 99,
            LapTimeSeconds = 65.0, IsValidLap = true,
            SessionType = LapSessionType.Race,
            RecordedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).GetRecommendationsAsync(
            seriesId: 1, weekNumber: 1, customerId: 1, includePersonalLaps: true, ct: TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Equal(1, dto.CarId);
        Assert.Equal(65.0, dto.BestLapSeconds); // personal lap shown as best
        // 65s beats all 3 → slowerCount = 3, total = 3 → 100%
        Assert.Equal(100.0, dto.PercentileRank, tolerance: 1e-6);
    }

    [Fact]
    public async Task GetRecommendationsAsync_PersonalLapPath_ExcludesCarWhenFlagOff()
    {
        // Same setup as above but includePersonalLaps=false → car should not appear.
        await using var db = DbContextFactory.Create();
        var (week, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);

        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 80);

        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        db.PersonalLaps.Add(new PersonalLap
        {
            UserId = userId, CarId = 1, TrackId = 99,
            LapTimeSeconds = 65.0, IsValidLap = true,
            SessionType = LapSessionType.Race,
            RecordedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).GetRecommendationsAsync(
            seriesId: 1, weekNumber: 1, customerId: 1, includePersonalLaps: false, ct: TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecommendationsAsync_PersonalLapPath_SessionTypeFilter_ExcludesMismatch()
    {
        // Personal lap is a Practice lap; filter is Race-only → car should not appear.
        await using var db = DbContextFactory.Create();
        var (week, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);

        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 80);

        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        db.PersonalLaps.Add(new PersonalLap
        {
            UserId = userId, CarId = 1, TrackId = 99,
            LapTimeSeconds = 65.0, IsValidLap = true,
            SessionType = LapSessionType.Practice, // practice lap, not race
            RecordedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).GetRecommendationsAsync(
            seriesId: 1, weekNumber: 1, customerId: 1,
            includePersonalLaps: true,
            personalLapTypes: [LapSessionType.Race], TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecommendationsAsync_PersonalLapPath_SessionTypeFilter_IncludesUnknownType()
    {
        // Laps uploaded before SessionType tracking was added have SessionType=Unknown.
        // They must always be included when a filter is active, never silently excluded.
        await using var db = DbContextFactory.Create();
        var (week, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);

        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 70);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 80);

        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        db.PersonalLaps.Add(new PersonalLap
        {
            UserId = userId, CarId = 1, TrackId = 99,
            LapTimeSeconds = 65.0, IsValidLap = true,
            SessionType = LapSessionType.Unknown, // pre-migration default
            RecordedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).GetRecommendationsAsync(
            seriesId: 1, weekNumber: 1, customerId: 1,
            includePersonalLaps: true,
            personalLapTypes: [LapSessionType.Race], TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Equal(65.0, dto.BestLapSeconds);
    }

    [Fact]
    public async Task GetRecommendationsAsync_ActualPath_BestLapSeconds_ReflectsPersonalLapWhenFaster()
    {
        // Driver has a SubsessionResult (90s) and a personal lap (65s, race).
        // BestLapSeconds in the DTO should be 65s.
        await using var db = DbContextFactory.Create();
        var (week, car1, _, carClass, subsession) = SeedWeekWithTwoCars(db);

        AddResult(db, subsession, car1, carClass, custId: 1,   lapSeconds: 90); // driver's race lap
        AddResult(db, subsession, car1, carClass, custId: 100, lapSeconds: 80);
        AddResult(db, subsession, car1, carClass, custId: 200, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        db.PersonalLaps.Add(new PersonalLap
        {
            UserId = userId, CarId = 1, TrackId = 99,
            LapTimeSeconds = 65.0, IsValidLap = true,
            SessionType = LapSessionType.Race,
            RecordedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).GetRecommendationsAsync(
            seriesId: 1, weekNumber: 1, customerId: 1, includePersonalLaps: true, ct: TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Equal(65.0, dto.BestLapSeconds); // personal lap, not race lap
    }
}
