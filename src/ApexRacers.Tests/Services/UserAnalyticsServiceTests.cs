using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class UserAnalyticsServiceTests
{
    [Fact]
    public async Task GetAnalyticsAsync_NoPercentileData_ReturnsEmptyList()
    {
        await using var db = DbContextFactory.Create();
        var result = await new UserAnalyticsService(db).GetAnalyticsAsync(Guid.NewGuid(), null, TestContext.Current.CancellationToken);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAnalyticsAsync_SingleCarResult_ReturnsCorrectDto()
    {
        await using var db = DbContextFactory.Create();
        var (series, season, week, car) = SeedBaseGraph(db, seriesId: 1, weekNumber: 1);
        var carClass = AddCarClass(db, id: 1);
        var subsession = AddSubsession(db, id: -1, seasonId: season.Id, weekId: week.Id, trackId: week.TrackId);
        var userId = Guid.NewGuid();
        db.Users.Add(MakeUser(userId, iracingId: 42));
        db.CarPercentileResults.Add(new CarPercentileResult
        {
            UserId = userId, CarId = car.Id, SeriesId = 1, WeekId = week.Id,
            PercentileRank = 85.0, SampleSize = 100, ComputedAt = DateTimeOffset.UtcNow,
            Car = car, Week = week,
        });
        // Driver 42's race best
        AddResult(db, subsession, car, carClass, custId: 42, lapSeconds: 62.5);
        // Another driver
        AddResult(db, subsession, car, carClass, custId: 99, lapSeconds: 65.0);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new UserAnalyticsService(db).GetAnalyticsAsync(userId, null, TestContext.Current.CancellationToken);

        Assert.Single(result);
        var dto = result[0];
        Assert.Equal(car.Id, dto.CarId);
        Assert.Equal(car.Name, dto.CarName);
        Assert.Equal(series.Id, dto.SeriesId);
        Assert.Equal(series.Name, dto.SeriesName);
        Assert.Equal(85.0, dto.LatestPercentileRank);
        Assert.Equal(85.0, dto.BestPercentileRank);
        Assert.Equal(62.5, dto.PersonalBestLapSeconds);
        Assert.Equal(1, dto.TotalWeeks);
        Assert.Single(dto.PercentileHistory);
    }

    [Fact]
    public async Task GetAnalyticsAsync_PersonalLapBeatsRaceBest_OverridesPersonalBest()
    {
        // A telemetry PersonalLap at the same car+track is faster than the race best, so the
        // analytics personal-best is overlaid from it (UserAnalyticsService lines 95-99).
        await using var db = DbContextFactory.Create();
        var (_, season, week, car) = SeedBaseGraph(db, seriesId: 1, weekNumber: 1);
        var carClass = AddCarClass(db, id: 1);
        var subsession = AddSubsession(db, id: -1, seasonId: season.Id, weekId: week.Id, trackId: week.TrackId);
        var userId = Guid.NewGuid();
        db.Users.Add(MakeUser(userId, iracingId: 42));
        db.CarPercentileResults.Add(new CarPercentileResult
        {
            UserId = userId, CarId = car.Id, SeriesId = 1, WeekId = week.Id,
            PercentileRank = 85.0, SampleSize = 100, ComputedAt = DateTimeOffset.UtcNow,
            Car = car, Week = week,
        });
        AddResult(db, subsession, car, carClass, custId: 42, lapSeconds: 62.5); // race best
        db.PersonalLaps.Add(new PersonalLap
        {
            UserId = userId, CarId = car.Id, TrackId = week.TrackId, LapTimeSeconds = 60.0,
            IsValidLap = true, SessionType = LapSessionType.Race, RecordedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new UserAnalyticsService(db).GetAnalyticsAsync(
            userId, null, TestContext.Current.CancellationToken);

        Assert.Equal(60.0, result[0].PersonalBestLapSeconds); // telemetry lap beats the 62.5 race best
    }

    [Fact]
    public async Task GetAnalyticsAsync_MultipleWeeks_BuildsOrderedTrendHistory()
    {
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 10, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var car = new Car { Id = 1, Name = "Porsche 992", NameAbbreviated = "P" };
        var week1 = MakeWeek(id: Guid.NewGuid(), seasonId: 10, weekNumber: 1, trackName: "Monza", season: season, trackId: 101);
        var week2 = MakeWeek(id: Guid.NewGuid(), seasonId: 10, weekNumber: 2, trackName: "Spa", season: season, trackId: 102);
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Cars.Add(car);
        db.Tracks.AddRange(week1.Track, week2.Track);
        db.Weeks.AddRange(week1, week2);

        var userId = Guid.NewGuid();
        db.CarPercentileResults.AddRange(
            new CarPercentileResult { UserId = userId, CarId = 1, SeriesId = 1, WeekId = week1.Id, PercentileRank = 60.0, SampleSize = 80, ComputedAt = DateTimeOffset.UtcNow.AddDays(-14), Car = car, Week = week1 },
            new CarPercentileResult { UserId = userId, CarId = 1, SeriesId = 1, WeekId = week2.Id, PercentileRank = 80.0, SampleSize = 90, ComputedAt = DateTimeOffset.UtcNow.AddDays(-7), Car = car, Week = week2 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new UserAnalyticsService(db).GetAnalyticsAsync(userId, null, TestContext.Current.CancellationToken);

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
            new CarPercentileResult { UserId = userId, CarId = car1.Id, SeriesId = 1, WeekId = week1.Id, PercentileRank = 70.0, SampleSize = 50, ComputedAt = DateTimeOffset.UtcNow, Car = car1, Week = week1 },
            new CarPercentileResult { UserId = userId, CarId = car2.Id, SeriesId = 2, WeekId = week2.Id, PercentileRank = 60.0, SampleSize = 50, ComputedAt = DateTimeOffset.UtcNow, Car = car2, Week = week2 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new UserAnalyticsService(db).GetAnalyticsAsync(userId, seriesId: 1, TestContext.Current.CancellationToken);

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
            UserId = userId, CarId = car.Id, SeriesId = 1, WeekId = week.Id,
            PercentileRank = 75.0, SampleSize = 60, ComputedAt = DateTimeOffset.UtcNow,
            Car = car, Week = week,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new UserAnalyticsService(db).GetAnalyticsAsync(userId, null, TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(0, result[0].TotalWeeks);
        Assert.Null(result[0].PersonalBestLapSeconds);
    }

    [Fact]
    public async Task GetAnalyticsAsync_MultipleCarsSortedByBestPercentileDescending()
    {
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 10, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var week = MakeWeek(Guid.NewGuid(), 10, 1, "Spa", season, trackId: 201);
        var car1 = new Car { Id = 1, Name = "Porsche 992", NameAbbreviated = "P" };
        var car2 = new Car { Id = 2, Name = "Ferrari GT3", NameAbbreviated = "F" };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Tracks.Add(week.Track);
        db.Weeks.Add(week);
        db.Cars.AddRange(car1, car2);

        var userId = Guid.NewGuid();
        db.CarPercentileResults.AddRange(
            new CarPercentileResult { UserId = userId, CarId = 1, SeriesId = 1, WeekId = week.Id, PercentileRank = 70.0, SampleSize = 50, ComputedAt = DateTimeOffset.UtcNow, Car = car1, Week = week },
            new CarPercentileResult { UserId = userId, CarId = 2, SeriesId = 1, WeekId = week.Id, PercentileRank = 90.0, SampleSize = 50, ComputedAt = DateTimeOffset.UtcNow, Car = car2, Week = week });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new UserAnalyticsService(db).GetAnalyticsAsync(userId, null, TestContext.Current.CancellationToken);

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
        var week1 = MakeWeek(Guid.NewGuid(), 10, 1, "Monza", season, trackId: 301); // best percentile week
        var week2 = MakeWeek(Guid.NewGuid(), 10, 2, "Spa", season, trackId: 302);
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Cars.Add(car);
        db.Tracks.AddRange(week1.Track, week2.Track);
        db.Weeks.AddRange(week1, week2);
        var carClass = AddCarClass(db, id: 1);
        var subsession1 = AddSubsession(db, id: -1, seasonId: 10, weekId: week1.Id, trackId: 301);
        var subsession2 = AddSubsession(db, id: -2, seasonId: 10, weekId: week2.Id, trackId: 302);

        var userId = Guid.NewGuid();
        db.CarPercentileResults.AddRange(
            new CarPercentileResult { UserId = userId, CarId = 1, SeriesId = 1, WeekId = week1.Id, PercentileRank = 90.0, SampleSize = 3, ComputedAt = DateTimeOffset.UtcNow, Car = car, Week = week1 },
            new CarPercentileResult { UserId = userId, CarId = 1, SeriesId = 1, WeekId = week2.Id, PercentileRank = 60.0, SampleSize = 3, ComputedAt = DateTimeOffset.UtcNow, Car = car, Week = week2 });

        // week1 field laps: [60, 70, 80] → median = 70
        AddResult(db, subsession1, car, carClass, custId: 1, lapSeconds: 70);
        AddResult(db, subsession1, car, carClass, custId: 2, lapSeconds: 80);
        AddResult(db, subsession1, car, carClass, custId: 3, lapSeconds: 60);

        // week2 laps: different values — median should NOT be taken from here
        AddResult(db, subsession2, car, carClass, custId: 1, lapSeconds: 100);
        AddResult(db, subsession2, car, carClass, custId: 2, lapSeconds: 110);
        AddResult(db, subsession2, car, carClass, custId: 3, lapSeconds: 120);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new UserAnalyticsService(db).GetAnalyticsAsync(userId, null, TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(70.0, result[0].MedianLapSeconds);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (Series series, Season season, Week week, Car car) SeedBaseGraph(
        AppDbContext db, int seriesId, int weekNumber)
    {
        var series = new Series { Id = seriesId, Name = $"Series {seriesId}" };
        var season = new Season { Id = seriesId * 10, SeriesId = seriesId, Year = 2026, Quarter = 2, Active = true, Series = series };
        var trackId = seriesId * 100 + weekNumber;
        var week = MakeWeek(Guid.NewGuid(), season.Id, weekNumber, $"Track-{seriesId}", season, trackId);
        var car = new Car { Id = seriesId * 100 + 1, Name = $"Car {seriesId}", NameAbbreviated = $"C{seriesId}" };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Tracks.Add(week.Track);
        db.Weeks.Add(week);
        db.Cars.Add(car);
        return (series, season, week, car);
    }

    private static Week MakeWeek(Guid id, int seasonId, int weekNumber, string trackName, Season season, int trackId = 0)
    {
        var resolvedTrackId = trackId == 0 ? seasonId * 100 + weekNumber : trackId;
        var track = new Track { Id = resolvedTrackId, Name = trackName, ConfigName = "Full" };
        return new Week
        {
            Id = id,
            SeasonId = seasonId,
            WeekNumber = weekNumber,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7 * weekNumber)),
            TrackId = resolvedTrackId,
            Track = track,
            Season = season,
        };
    }

    private static CarClass AddCarClass(AppDbContext db, int id)
    {
        var carClass = new CarClass { Id = id, Name = "GT3", ShortName = "GT3", RelativeSpeed = 52 };
        db.CarClasses.Add(carClass);
        return carClass;
    }

    private static Subsession AddSubsession(AppDbContext db, int id, int seasonId, Guid weekId, int trackId)
    {
        var subsession = new Subsession
        {
            Id          = id,
            SeasonId    = seasonId,
            WeekNumber  = 0,
            WeekId      = weekId,
            TrackId     = trackId,
            StartTime   = DateTimeOffset.UtcNow.AddHours(-2),
        };
        db.Subsessions.Add(subsession);
        return subsession;
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

    private static ApplicationUser MakeUser(Guid id, long? iracingId) =>
        new ApplicationUser
        {
            Id = id,
            IRacingCustomerId = iracingId,
            DisplayName = "Test",
            UserName = $"{id}@test.com",
            Email = $"{id}@test.com",
            SecurityStamp = "x",
        };
}
