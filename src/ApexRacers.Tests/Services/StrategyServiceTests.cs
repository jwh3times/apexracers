using System.Text.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData.Series;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Services;

public class StrategyServiceTests
{
    private const int SeriesId = 444;
    private const int SeasonId = 6115;
    private const int TargetWeek = 2;
    private const int Bmw = 132;
    private const int Porsche = 119;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string WeatherJson(decimal precip) => JsonSerializer.Serialize(new WeatherSummary
    {
        TemperatureHigh = 24m,
        TemperatureLow = 20m,
        TemperatureUnits = 1,
        WindHigh = 4m,
        WindLow = 2m,
        WindUnits = 1,
        PrecipitationChance = precip,
        SkiesHigh = 1,
        SkiesLow = 1,
    });

    /// <summary>
    /// Seeds a series with an active season, a target week (with previous week for BoP shift),
    /// a richly-specced track, two cars (BMW rain-capable, Porsche not), and BoP for both weeks.
    /// </summary>
    private static async Task<AppDbContext> SeededAsync(
        decimal precip = 5m, bool active = true, bool withWeather = true)
    {
        var db = DbContextFactory.Create();
        var series = new Series { Id = SeriesId, Name = "GT Sprint" };
        var season = new Season { Id = SeasonId, SeriesId = SeriesId, Active = active, Year = 2026, Quarter = 2, Series = series };
        var track = new Track
        {
            Id = 532, Name = "Thruxton", ConfigName = "Club", TrackConfigLength = 2.36,
            CornersPerLap = 10, NumberPitstalls = 40, PitRoadSpeedLimit = 60, NightLighting = false,
        };
        var prevTrack = new Track { Id = 47, Name = "Laguna Seca", ConfigName = "" };

        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Tracks.AddRange(track, prevTrack);
        db.Weeks.Add(new Week
        {
            Id = Guid.NewGuid(), SeasonId = SeasonId, WeekNumber = TargetWeek, TrackId = 532,
            StartDate = new DateOnly(2026, 6, 5), Season = season, Track = track,
            WeatherSummaryJson = withWeather ? WeatherJson(precip) : null,
        });
        db.Weeks.Add(new Week
        {
            Id = Guid.NewGuid(), SeasonId = SeasonId, WeekNumber = 1, TrackId = 47,
            StartDate = new DateOnly(2026, 5, 29), Season = season, Track = prevTrack,
        });

        db.Cars.Add(new Car { Id = Bmw, Name = "BMW M4 GT3", NameAbbreviated = "BMW", RainEnabled = true });
        db.Cars.Add(new Car { Id = Porsche, Name = "Porsche 718 GT4", NameAbbreviated = "POR", RainEnabled = false });

        db.SeasonCarBops.AddRange(
            // Target week.
            new SeasonCarBop { SeasonId = SeasonId, WeekNumber = TargetWeek, CarId = Bmw, WeightPenaltyKg = 10, PowerAdjustPct = -1.5, MaxPctFuelFill = 50, MaxDryTireSets = 0 },
            new SeasonCarBop { SeasonId = SeasonId, WeekNumber = TargetWeek, CarId = Porsche, WeightPenaltyKg = 0, PowerAdjustPct = 0, MaxPctFuelFill = 100, MaxDryTireSets = 2 },
            // Previous week — BMW was lighter / less restricted (so target = Nerfed); Porsche unchanged.
            new SeasonCarBop { SeasonId = SeasonId, WeekNumber = 1, CarId = Bmw, WeightPenaltyKg = 5, PowerAdjustPct = -1.0 },
            new SeasonCarBop { SeasonId = SeasonId, WeekNumber = 1, CarId = Porsche, WeightPenaltyKg = 0, PowerAdjustPct = 0 });

        await db.SaveChangesAsync(Ct);
        return db;
    }

    private static StrategyService CreateService(AppDbContext db) =>
        new(db, new CarRecommendationService(db), new MemberContext(db));

    [Fact]
    public async Task GetStrategyAsync_Anonymous_MapsTrackWeatherRiskAndBopContext()
    {
        await using var db = await SeededAsync(precip: 5m);

        var dto = await CreateService(db).GetStrategyAsync(SeriesId, TargetWeek, userId: null, Ct);

        Assert.Equal(SeriesId, dto.SeriesId);
        Assert.Equal("GT Sprint", dto.SeriesName);
        Assert.Equal(TargetWeek, dto.WeekNumber);
        Assert.Equal("Thruxton", dto.TrackName);
        Assert.Equal("Club", dto.ConfigName);
        Assert.Equal(2.36, dto.TrackLengthMiles);
        Assert.Equal(10, dto.CornersPerLap);
        Assert.Equal(40, dto.NumberPitstalls);
        Assert.Equal(60, dto.PitRoadSpeedLimit);
        Assert.False(dto.NightLighting);

        Assert.NotNull(dto.Weather);
        Assert.Equal("Low", dto.WeatherRisk.Level); // precip 5% → Low
        Assert.True(dto.WeatherRisk.FieldRainCapable); // BMW is rain-capable
        Assert.False(dto.Personalized);

        // Anonymous → no personal overlay → alphabetical order (BMW before Porsche).
        Assert.Equal(new[] { "BMW M4 GT3", "Porsche 718 GT4" }, dto.Cars.Select(c => c.CarName));

        var bmw = dto.Cars[0];
        Assert.Equal("Nerfed", bmw.BopTrend);   // heavier (5→10) and down on power (-1.0→-1.5)
        Assert.Equal(5, bmw.WeightDeltaKg);
        Assert.Equal(-0.5, bmw.PowerDeltaPct);
        Assert.True(bmw.FuelCapped);             // 50% fuel cap
        Assert.False(bmw.LimitedTireSets);       // 0 = unlimited
        Assert.True(bmw.RainEnabled);
        Assert.Null(bmw.PercentileRank);
        Assert.Null(bmw.ProjectedLapSeconds);
        Assert.Null(bmw.OptimalRank);

        var porsche = dto.Cars[1];
        Assert.Equal("Unchanged", porsche.BopTrend);
        Assert.False(porsche.FuelCapped);        // 100% = no restriction
        Assert.True(porsche.LimitedTireSets);    // 2 dry sets
        Assert.False(porsche.RainEnabled);
    }

    [Fact]
    public async Task GetStrategyAsync_HighPrecip_ReturnsHighWeatherRisk()
    {
        await using var db = await SeededAsync(precip: 60m);

        var dto = await CreateService(db).GetStrategyAsync(SeriesId, TargetWeek, null, Ct);

        Assert.Equal("High", dto.WeatherRisk.Level);
        Assert.Equal(60, dto.WeatherRisk.PrecipChancePct);
        Assert.Contains("prioritize a rain setup", dto.WeatherRisk.Note);
    }

    [Fact]
    public async Task GetStrategyAsync_NoWeatherJson_NullWeatherAndLowRisk()
    {
        await using var db = await SeededAsync(withWeather: false);

        var dto = await CreateService(db).GetStrategyAsync(SeriesId, TargetWeek, null, Ct);

        Assert.Null(dto.Weather);
        Assert.Equal("Low", dto.WeatherRisk.Level);
        Assert.Equal(0, dto.WeatherRisk.PrecipChancePct);
    }

    [Fact]
    public async Task GetStrategyAsync_LinkedCaller_OverlaysRecommendationAndOrdersByOptimalRank()
    {
        await using var db = await SeededAsync();

        // A linked user who raced the BMW this week and beat the field.
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Driver" });
        var carClass = new CarClass { Id = 1, Name = "GT3", ShortName = "GT3", RelativeSpeed = 52 };
        db.CarClasses.Add(carClass);
        var weekId = await db.Weeks
            .Where(w => w.SeasonId == SeasonId && w.WeekNumber == TargetWeek)
            .Select(w => w.Id).FirstAsync(Ct);
        var subsession = new Subsession { Id = -1, SeasonId = SeasonId, WeekNumber = TargetWeek, WeekId = weekId, TrackId = 532, StartTime = DateTimeOffset.UtcNow.AddHours(-2) };
        db.Subsessions.Add(subsession);
        await db.SaveChangesAsync(Ct);

        AddResult(db, subsession, Bmw, carClass, custId: 1, lapSeconds: 60);   // caller — fastest
        AddResult(db, subsession, Bmw, carClass, custId: 50, lapSeconds: 70);
        AddResult(db, subsession, Bmw, carClass, custId: 60, lapSeconds: 80);
        await db.SaveChangesAsync(Ct);

        var dto = await CreateService(db).GetStrategyAsync(SeriesId, TargetWeek, userId, Ct);

        Assert.True(dto.Personalized);

        // BMW got a recommendation (rank 1) → it sorts ahead of the un-raced Porsche.
        var bmw = dto.Cars[0];
        Assert.Equal("BMW M4 GT3", bmw.CarName);
        Assert.Equal(1, bmw.OptimalRank);
        Assert.Equal(100.0, bmw.PercentileRank); // 60 s beat both other drivers
        Assert.NotNull(bmw.ProjectedLapSeconds);

        // Porsche had no caller lap → no overlay, sorts last.
        var porsche = dto.Cars[1];
        Assert.Equal("Porsche 718 GT4", porsche.CarName);
        Assert.Null(porsche.OptimalRank);
        Assert.Null(porsche.PercentileRank);
    }

    [Fact]
    public async Task GetStrategyAsync_UnknownCarInBop_FallsBackToCarIdLabel()
    {
        await using var db = await SeededAsync();
        db.SeasonCarBops.Add(new SeasonCarBop { SeasonId = SeasonId, WeekNumber = TargetWeek, CarId = 999 });
        await db.SaveChangesAsync(Ct);

        var dto = await CreateService(db).GetStrategyAsync(SeriesId, TargetWeek, null, Ct);

        Assert.Contains(dto.Cars, c => c.CarName == "Car 999");
    }

    [Fact]
    public async Task GetStrategyAsync_NoActiveSeason_ThrowsKeyNotFound()
    {
        await using var db = await SeededAsync(active: false);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => CreateService(db).GetStrategyAsync(SeriesId, TargetWeek, null, Ct));
    }

    [Fact]
    public async Task GetStrategyAsync_UnknownWeek_ThrowsKeyNotFound()
    {
        await using var db = await SeededAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => CreateService(db).GetStrategyAsync(SeriesId, weekNumber: 99, null, Ct));
    }

    private static void AddResult(AppDbContext db, Subsession subsession, int carId, CarClass carClass, long custId, double lapSeconds)
    {
        db.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId            = subsession.Id,
            CustId                  = custId,
            CarId                   = carId,
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
}
