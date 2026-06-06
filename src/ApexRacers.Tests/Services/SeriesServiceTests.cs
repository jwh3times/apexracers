using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
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
        await db.SaveChangesAsync();

        var result = await new SeriesService(db).GetActiveSeriesAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActiveSeriesAsync_ActiveSeasonWithStartedWeek_ReturnsCurrentWeekNumber()
    {
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var track = new Track { Id = 99, Name = "Spa", ConfigName = "Full" };
        var week = new Week { Id = Guid.NewGuid(), SeasonId = 1, WeekNumber = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), TrackId = 99, Track = track, Season = season };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Tracks.Add(track);
        db.Weeks.Add(week);
        await db.SaveChangesAsync();

        var result = await new SeriesService(db).GetActiveSeriesAsync();

        var dto = Assert.Single(result);
        Assert.Equal(1, dto.Id);
        Assert.Equal("GT3 Cup", dto.Name);
        Assert.Equal(1, dto.SeasonId);
        Assert.Equal(1, dto.CurrentWeekNumber);
        Assert.Equal("Spa", dto.TrackName);
        Assert.Equal("Full", dto.TrackConfigName);
        Assert.Equal(0, dto.CarCount);
        Assert.Equal(0, dto.DriverCount);
    }

    [Fact]
    public async Task GetActiveSeriesAsync_ActiveSeasonWithFutureWeekOnly_ReturnsNullCurrentWeekNumber()
    {
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var track = new Track { Id = 99, Name = "Spa", ConfigName = "Full" };
        var week = new Week { Id = Guid.NewGuid(), SeasonId = 1, WeekNumber = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), TrackId = 99, Track = track, Season = season };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Tracks.Add(track);
        db.Weeks.Add(week);
        await db.SaveChangesAsync();

        var result = await new SeriesService(db).GetActiveSeriesAsync();

        var dto = Assert.Single(result);
        Assert.Null(dto.CurrentWeekNumber);
    }

    [Fact]
    public async Task GetActiveSeriesAsync_MultipleStartedWeeks_ReturnsMostRecentWeekNumber()
    {
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var track1 = new Track { Id = 1, Name = "Monza", ConfigName = "Full" };
        var track2 = new Track { Id = 2, Name = "Spa", ConfigName = "Full" };
        var week1 = new Week { Id = Guid.NewGuid(), SeasonId = 1, WeekNumber = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14)), TrackId = 1, Track = track1, Season = season };
        var week2 = new Week { Id = Guid.NewGuid(), SeasonId = 1, WeekNumber = 2, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)), TrackId = 2, Track = track2, Season = season };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Tracks.AddRange(track1, track2);
        db.Weeks.AddRange(week1, week2);
        await db.SaveChangesAsync();

        var result = await new SeriesService(db).GetActiveSeriesAsync();

        Assert.Equal(2, result[0].CurrentWeekNumber);
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
        var week1 = new Week { Id = Guid.NewGuid(), SeasonId = 1, WeekNumber = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14)), TrackId = 99, Track = track, Season = season };
        var week2 = new Week { Id = Guid.NewGuid(), SeasonId = 1, WeekNumber = 2, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)), TrackId = 99, Track = track, Season = season };
        // Official subsession in current week (week2) — 2 cars, 2 drivers
        var sub2 = new Subsession { Id = 2, SeasonId = 1, WeekNumber = 2, TrackId = 99, OfficialSession = true, EventStrengthOfField = 200, StartTime = DateTimeOffset.UtcNow.AddDays(-7), SplitNum = 0, Season = season, Track = track, Week = week2 };
        // Non-official subsession in current week — should not count
        var sub3 = new Subsession { Id = 3, SeasonId = 1, WeekNumber = 2, TrackId = 99, OfficialSession = false, EventStrengthOfField = 50, StartTime = DateTimeOffset.UtcNow.AddDays(-7), SplitNum = 1, Season = season, Track = track, Week = week2 };
        // Official subsession in previous week — should not count
        var sub1 = new Subsession { Id = 1, SeasonId = 1, WeekNumber = 1, TrackId = 99, OfficialSession = true, EventStrengthOfField = 100, StartTime = DateTimeOffset.UtcNow.AddDays(-14), SplitNum = 0, Season = season, Track = track, Week = week1 };

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
        await db.SaveChangesAsync();

        var result = await new SeriesService(db).GetActiveSeriesAsync();

        Assert.Equal(2, result[0].CarCount);    // car1 + car2 from current week official sub
        Assert.Equal(3, result[0].DriverCount); // drivers 10, 20, 30
    }
}
