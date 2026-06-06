using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class WeekCarStatsServiceTests
{
    private static (Series series, Season season, Week week, Car car, CarClass carClass, Subsession subsession) SeedBasic(AppDbContext db)
    {
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var track = new Track { Id = 99, Name = "Spa", ConfigName = "Full" };
        var week = new Week { Id = Guid.NewGuid(), SeasonId = 1, WeekNumber = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), TrackId = 99, Track = track, Season = season };
        var car = new Car { Id = 1, Name = "Porsche 992 GT3", NameAbbreviated = "P992" };
        var carClass = new CarClass { Id = 1, Name = "GT3", ShortName = "GT3", RelativeSpeed = 52 };
        var subsession = new Subsession { Id = -1, SeasonId = 1, WeekNumber = 1, WeekId = week.Id, TrackId = 99, StartTime = DateTimeOffset.UtcNow.AddHours(-2) };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Tracks.Add(track);
        db.Weeks.Add(week);
        db.Cars.Add(car);
        db.CarClasses.Add(carClass);
        db.Subsessions.Add(subsession);
        return (series, season, week, car, carClass, subsession);
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

    [Fact]
    public async Task GetCarsForWeekAsync_WeekNotInSeries_ReturnsEmpty()
    {
        await using var db = DbContextFactory.Create();
        SeedBasic(db);
        await db.SaveChangesAsync();

        var result = await new WeekCarStatsService(db).GetCarsForWeekAsync(seriesId: 99, weekNumber: 1);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCarsForWeekAsync_OddLapCount_ReturnsMiddleValueAsMedian()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, week, car, carClass, subsession) = SeedBasic(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 70);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 90);
        await db.SaveChangesAsync();

        var result = await new WeekCarStatsService(db).GetCarsForWeekAsync(seriesId: 1, weekNumber: 1);

        var dto = Assert.Single(result);
        Assert.Equal(3, dto.EntryCount);
        Assert.Equal(60, dto.FastestLapSeconds);
        Assert.Equal(70, dto.MedianLapSeconds);
    }

    [Fact]
    public async Task GetCarsForWeekAsync_EvenLapCount_ReturnsAverageOfTwoMiddleValuesAsMedian()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, week, car, carClass, subsession) = SeedBasic(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 70);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 80);
        AddResult(db, subsession, car, carClass, custId: 4, lapSeconds: 90);
        await db.SaveChangesAsync();

        var result = await new WeekCarStatsService(db).GetCarsForWeekAsync(seriesId: 1, weekNumber: 1);

        var dto = Assert.Single(result);
        Assert.Equal(75, dto.MedianLapSeconds);
    }

    [Fact]
    public async Task GetCarsForWeekAsync_IncludesClassName()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, _, car, carClass, subsession) = SeedBasic(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 60);
        await db.SaveChangesAsync();

        var result = await new WeekCarStatsService(db).GetCarsForWeekAsync(seriesId: 1, weekNumber: 1);

        var dto = Assert.Single(result);
        Assert.Equal("GT3", dto.ClassName);
    }

    [Fact]
    public async Task GetWeekDetailAsync_ReturnsSeriesNameAndTrackInfo()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, _, car, carClass, subsession) = SeedBasic(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 90);
        await db.SaveChangesAsync();

        var result = await new WeekCarStatsService(db).GetWeekDetailAsync(seriesId: 1, weekNumber: 1);

        Assert.NotNull(result);
        Assert.Equal("GT3 Cup", result.SeriesName);
        Assert.Equal("Spa", result.TrackName);
        Assert.Equal("Full", result.TrackConfigName);
        Assert.Single(result.Cars);
    }

    [Fact]
    public async Task GetWeekDetailAsync_UnknownSeries_ReturnsNull()
    {
        await using var db = DbContextFactory.Create();
        SeedBasic(db);
        await db.SaveChangesAsync();

        var result = await new WeekCarStatsService(db).GetWeekDetailAsync(seriesId: 99, weekNumber: 1);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCarsForWeekAsync_MultipleCars_OrderedByFastestLapAscending()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, week, car, carClass, subsession) = SeedBasic(db);
        var car2 = new Car { Id = 2, Name = "Ferrari 296 GT3", NameAbbreviated = "F296" };
        db.Cars.Add(car2);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        AddResult(db, subsession, car2, carClass, custId: 2, lapSeconds: 60);
        await db.SaveChangesAsync();

        var result = await new WeekCarStatsService(db).GetCarsForWeekAsync(seriesId: 1, weekNumber: 1);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].CarId); // Ferrari is faster
        Assert.Equal(1, result[1].CarId);
    }
}
