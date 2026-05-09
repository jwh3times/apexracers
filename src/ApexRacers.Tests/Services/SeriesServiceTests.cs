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
    public async Task GetActiveSeriesAsync_ActiveSeasonWithStartedWeek_ReturnsCurrentWeekId()
    {
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var week = new Week { Id = 10, SeasonId = 1, WeekNumber = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), TrackName = "Spa", ConfigName = "Full", IracingTrackId = 99, Season = season };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Weeks.Add(week);
        await db.SaveChangesAsync();

        var result = await new SeriesService(db).GetActiveSeriesAsync();

        var dto = Assert.Single(result);
        Assert.Equal(1, dto.Id);
        Assert.Equal("GT3 Cup", dto.Name);
        Assert.Equal(1, dto.SeasonId);
        Assert.Equal(10, dto.CurrentWeekId);
    }

    [Fact]
    public async Task GetActiveSeriesAsync_ActiveSeasonWithFutureWeekOnly_ReturnsNullCurrentWeekId()
    {
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var week = new Week { Id = 10, SeasonId = 1, WeekNumber = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), TrackName = "Spa", ConfigName = "Full", IracingTrackId = 99, Season = season };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Weeks.Add(week);
        await db.SaveChangesAsync();

        var result = await new SeriesService(db).GetActiveSeriesAsync();

        var dto = Assert.Single(result);
        Assert.Null(dto.CurrentWeekId);
    }

    [Fact]
    public async Task GetActiveSeriesAsync_MultipleStartedWeeks_ReturnsMostRecentWeekId()
    {
        await using var db = DbContextFactory.Create();
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var week1 = new Week { Id = 10, SeasonId = 1, WeekNumber = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14)), TrackName = "Monza", ConfigName = "Full", IracingTrackId = 1, Season = season };
        var week2 = new Week { Id = 11, SeasonId = 1, WeekNumber = 2, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)), TrackName = "Spa", ConfigName = "Full", IracingTrackId = 2, Season = season };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Weeks.AddRange(week1, week2);
        await db.SaveChangesAsync();

        var result = await new SeriesService(db).GetActiveSeriesAsync();

        Assert.Equal(11, result[0].CurrentWeekId);
    }
}
