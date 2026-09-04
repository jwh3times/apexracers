using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class SeriesServiceTests
{
    [Fact]
    public async Task GetActiveSeriesAsync_NoActiveSeasons_ReturnsEmpty()
    {
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        db.Series.Add(series);
        db.Seasons.Add(new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = false, Series = series });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new SeriesService(db).GetActiveSeriesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActiveSeriesAsync_ActiveSeasonWithStartedWeek_ReturnsCurrentRaceWeekIndex()
    {
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var track = new Track { Id = 99, Name = "Spa", ConfigName = "Full" };
        var week = new Week { Id = Guid.NewGuid(), SeasonId = 1, RaceWeekIndex = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), TrackId = 99, Track = track, Season = season };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Tracks.Add(track);
        db.Weeks.Add(week);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new SeriesService(db).GetActiveSeriesAsync(TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Equal(1, dto.Id);
        Assert.Equal("GT3 Cup", dto.Name);
        Assert.Equal(1, dto.SeasonId);
        Assert.Equal(1, dto.CurrentRaceWeekIndex);
        Assert.Equal("Spa", dto.TrackName);
        Assert.Equal("Full", dto.TrackConfigName);
        Assert.Equal(0, dto.CarCount);
        Assert.Equal(0, dto.DriverCount);
    }

    [Fact]
    public async Task GetActiveSeriesAsync_ActiveSeasonWithFutureWeekOnly_ReturnsNullCurrentRaceWeekIndex()
    {
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var track = new Track { Id = 99, Name = "Spa", ConfigName = "Full" };
        var week = new Week { Id = Guid.NewGuid(), SeasonId = 1, RaceWeekIndex = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), TrackId = 99, Track = track, Season = season };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Tracks.Add(track);
        db.Weeks.Add(week);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new SeriesService(db).GetActiveSeriesAsync(TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Null(dto.CurrentRaceWeekIndex);
    }

    [Fact]
    public async Task GetActiveSeriesAsync_MultipleStartedWeeks_ReturnsMostRecentRaceWeekIndex()
    {
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var track1 = new Track { Id = 1, Name = "Monza", ConfigName = "Full" };
        var track2 = new Track { Id = 2, Name = "Spa", ConfigName = "Full" };
        var week1 = new Week { Id = Guid.NewGuid(), SeasonId = 1, RaceWeekIndex = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14)), TrackId = 1, Track = track1, Season = season };
        var week2 = new Week { Id = Guid.NewGuid(), SeasonId = 1, RaceWeekIndex = 2, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)), TrackId = 2, Track = track2, Season = season };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Tracks.AddRange(track1, track2);
        db.Weeks.AddRange(week1, week2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new SeriesService(db).GetActiveSeriesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result[0].CurrentRaceWeekIndex);
        Assert.Equal("Spa", result[0].TrackName);
    }

    [Fact]
    public async Task GetActiveSeriesAsync_CountsDistinctCarsAndDriversFromCurrentWeekOfficialSessionsOnly()
    {
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var track = new Track { Id = 99, Name = "Spa", ConfigName = "Full" };
        var car1 = new Car { Id = 1, Name = "Ferrari 296", NameAbbreviated = "Ferrari" };
        var car2 = new Car { Id = 2, Name = "Porsche 992", NameAbbreviated = "Porsche" };
        var carClass = new CarClass { Id = 10, Name = "GT3", ShortName = "GT3", RelativeSpeed = 100 };
        var week1 = new Week { Id = Guid.NewGuid(), SeasonId = 1, RaceWeekIndex = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14)), TrackId = 99, Track = track, Season = season };
        var week2 = new Week { Id = Guid.NewGuid(), SeasonId = 1, RaceWeekIndex = 2, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)), TrackId = 99, Track = track, Season = season };
        // Official subsession in current week (week2) — 2 cars, 2 drivers
        var sub2 = new Subsession { Id = 2, SeasonId = 1, RaceWeekIndex = 2, TrackId = 99, OfficialSession = true, EventStrengthOfField = 200, StartTime = DateTimeOffset.UtcNow.AddDays(-7), SplitIndex = 0, SplitCount = 1, Season = season, Track = track, Week = week2 };
        // Non-official subsession in current week — should not count
        var sub3 = new Subsession { Id = 3, SeasonId = 1, RaceWeekIndex = 2, TrackId = 99, OfficialSession = false, EventStrengthOfField = 50, StartTime = DateTimeOffset.UtcNow.AddDays(-7), SplitIndex = 1, SplitCount = 2, Season = season, Track = track, Week = week2 };
        // Official subsession in previous week — should not count
        var sub1 = new Subsession { Id = 1, SeasonId = 1, RaceWeekIndex = 1, TrackId = 99, OfficialSession = true, EventStrengthOfField = 100, StartTime = DateTimeOffset.UtcNow.AddDays(-14), SplitIndex = 0, SplitCount = 1, Season = season, Track = track, Week = week1 };

        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Tracks.Add(track);
        db.Cars.AddRange(car1, car2);
        db.CarClasses.Add(carClass);
        db.Weeks.AddRange(week1, week2);
        db.Subsessions.AddRange(sub1, sub2, sub3);
        // Results for current-week official sub: 2 distinct cars, 3 drivers (driver 10 appears twice with car1 — same driver, deduplicated)
        db.SubsessionResults.AddRange(
            new SubsessionResult { SubsessionId = 2, CustId = 10, CarId = 1, CarClassId = 10, BestLapSeconds = 90, Subsession = sub2, Car = car1, CarClass = carClass },
            new SubsessionResult { SubsessionId = 2, CustId = 20, CarId = 1, CarClassId = 10, BestLapSeconds = 91, Subsession = sub2, Car = car1, CarClass = carClass },
            new SubsessionResult { SubsessionId = 2, CustId = 30, CarId = 2, CarClassId = 10, BestLapSeconds = 92, Subsession = sub2, Car = car2, CarClass = carClass }
        );
        // Results for prev-week sub — different driver/car counts that should be ignored
        db.SubsessionResults.Add(
            new SubsessionResult { SubsessionId = 1, CustId = 99, CarId = 1, CarClassId = 10, BestLapSeconds = 88, Subsession = sub1, Car = car1, CarClass = carClass }
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new SeriesService(db).GetActiveSeriesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result[0].CarCount);    // car1 + car2 from current week official sub
        Assert.Equal(3, result[0].DriverCount); // drivers 10, 20, 30
    }

    /// <summary>
    /// Seeds one series with two active seasons — Q2 racing since <paramref name="q2StartDaysAgo"/>
    /// days ago, Q3 flagged active but not starting until <paramref name="q3StartsInDays"/> days
    /// from now — each on its own track so the returned card names which season backs it.
    /// </summary>
    private static async Task<AppDbContext> ChangeoverAsync(int q2StartDaysAgo, int q3StartsInDays)
    {
        var db = DbContextFactory.Create();
        var today = DateTime.UtcNow;
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var q2 = new Season { Id = 100, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var q3 = new Season { Id = 200, SeriesId = 1, Year = 2026, Quarter = 3, Active = true, Series = series };
        var monza = new Track { Id = 1, Name = "Monza", ConfigName = "Full" };
        var spa = new Track { Id = 2, Name = "Spa", ConfigName = "Full" };

        db.Series.Add(series);
        db.Seasons.AddRange(q2, q3);
        db.Tracks.AddRange(monza, spa);
        db.Weeks.AddRange(
            new Week
            {
                Id = Guid.NewGuid(), SeasonId = 100, RaceWeekIndex = 0, TrackId = 1, Track = monza, Season = q2,
                StartDate = DateOnly.FromDateTime(today.AddDays(-q2StartDaysAgo)),
            },
            new Week
            {
                Id = Guid.NewGuid(), SeasonId = 200, RaceWeekIndex = 0, TrackId = 2, Track = spa, Season = q3,
                StartDate = DateOnly.FromDateTime(today.AddDays(q3StartsInDays)),
            });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }

    [Fact]
    public async Task GetActiveSeriesAsync_TwoActiveSeasonsForOneSeries_ReturnsASingleCardOnTheStartedSeason()
    {
        // The duplicate-card bug: iRacing flags the incoming season active while the outgoing one
        // is still racing, and every active season used to become its own card.
        await using var db = await ChangeoverAsync(q2StartDaysAgo: 14, q3StartsInDays: 7);

        var result = await new SeriesService(db).GetActiveSeriesAsync(TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Equal(100, dto.SeasonId);
        Assert.Equal(0, dto.CurrentRaceWeekIndex);
        Assert.Equal("Monza", dto.TrackName);
    }

    [Fact]
    public async Task GetActiveSeriesAsync_OnTheLaterSeasonsFirstWeekStartDate_CardSwitchesToIt()
    {
        // Same fixture, one day later in effect: Q3's first week starts today, so the single card
        // hands over rather than duplicating.
        await using var db = await ChangeoverAsync(q2StartDaysAgo: 21, q3StartsInDays: 0);

        var result = await new SeriesService(db).GetActiveSeriesAsync(TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Equal(200, dto.SeasonId);
        Assert.Equal(0, dto.CurrentRaceWeekIndex);
        Assert.Equal("Spa", dto.TrackName);
    }

    [Fact]
    public async Task GetActiveSeriesAsync_UpcomingSeasonOnly_StillReturnsACard()
    {
        // Explicitly out of scope for the duplicate-card fix: a series whose only season has not
        // begun keeps its prior behaviour — one card, no current week.
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 300, SeriesId = 1, Year = 2026, Quarter = 3, Active = true, Series = series };
        var track = new Track { Id = 1, Name = "Spa", ConfigName = "Full" };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Tracks.Add(track);
        db.Weeks.Add(new Week
        {
            Id = Guid.NewGuid(), SeasonId = 300, RaceWeekIndex = 0, TrackId = 1, Track = track, Season = season,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new SeriesService(db).GetActiveSeriesAsync(TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Equal(300, dto.SeasonId);
        Assert.Null(dto.CurrentRaceWeekIndex);
    }

    [Fact]
    public async Task GetActiveSeriesAsync_StartedSeasonDeactivatedUpstream_StillBacksTheCard()
    {
        // Q2 has been deactivated but Q3 has not begun. The series stays in the browser on Q3's
        // active flag, and the card shows the quarter drivers are actually racing.
        await using var db = await ChangeoverAsync(q2StartDaysAgo: 14, q3StartsInDays: 7);
        var q2 = await db.Seasons.FindAsync([100], TestContext.Current.CancellationToken);
        q2!.Active = false;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new SeriesService(db).GetActiveSeriesAsync(TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Equal(100, dto.SeasonId);
        Assert.Equal("Monza", dto.TrackName);
    }
}
