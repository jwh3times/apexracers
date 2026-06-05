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
    }
}
