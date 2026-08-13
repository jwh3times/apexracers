using System.Text.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData.Series;
using Xunit;

namespace ApexRacers.Tests.Services;

public class ScheduleServiceTests
{
    private const int SeriesId = 444;
    private const int SeasonId = 6115;
    private static readonly Guid UserId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string WeatherJson() => JsonSerializer.Serialize(new WeatherSummary
    {
        TemperatureHigh = 28.9m,
        TemperatureLow = 28.8m,
        TemperatureUnits = 1, // Celsius
        WindHigh = 5.6m,
        WindLow = 4.8m,
        WindUnits = 1, // m/s
        PrecipitationChance = 0m,
        SkiesHigh = 1,
        SkiesLow = 1,
    });

    private static async Task<AppDbContext> SeededAsync(bool active = true)
    {
        var db = DbContextFactory.Create();
        db.Series.Add(new Series { Id = SeriesId, Name = "GT3 Cup" });
        db.Seasons.Add(new Season { Id = SeasonId, SeriesId = SeriesId, Active = active, Year = 2026, Quarter = 2 });
        db.Tracks.Add(new Track { Id = 532, Name = "Thruxton", ConfigName = "Club" });
        db.Tracks.Add(new Track { Id = 47, Name = "Laguna Seca", ConfigName = "" });
        db.Weeks.Add(new Week
        {
            Id = Guid.NewGuid(), SeasonId = SeasonId, WeekNumber = 1, TrackId = 532,
            StartDate = new DateOnly(2026, 5, 29), WeatherSummaryJson = WeatherJson(),
        });
        db.Weeks.Add(new Week
        {
            Id = Guid.NewGuid(), SeasonId = SeasonId, WeekNumber = 2, TrackId = 47,
            StartDate = new DateOnly(2026, 6, 5), WeatherSummaryJson = null,
        });
        db.Cars.Add(new Car { Id = 132, Name = "BMW M4 GT3", NameAbbreviated = "BMW" });
        db.Cars.Add(new Car { Id = 119, Name = "Porsche 718 GT4", NameAbbreviated = "POR" });
        db.SeasonCarBops.AddRange(
            new SeasonCarBop
            {
                SeasonId = SeasonId, WeekNumber = 1, CarId = 132,
                WeightPenaltyKg = 10, PowerAdjustPct = -1.5, MaxPctFuelFill = 50, MaxDryTireSets = 0,
            },
            new SeasonCarBop
            {
                SeasonId = SeasonId, WeekNumber = 1, CarId = 119,
                WeightPenaltyKg = 0, PowerAdjustPct = 0, MaxPctFuelFill = 100, MaxDryTireSets = 2,
            });
        // User has a personal best at Thruxton (track 532 → week 1).
        db.PersonalLaps.Add(new PersonalLap { Id = Guid.NewGuid(), UserId = UserId, TrackId = 532, CarId = 132 });
        await db.SaveChangesAsync(Ct);
        return db;
    }

    [Fact]
    public async Task GetScheduleAsync_MapsWeeksWeatherBopAndOrders()
    {
        await using var db = await SeededAsync();
        var service = new ScheduleService(db);

        var dto = await service.GetScheduleAsync(SeriesId, UserId, Ct);

        Assert.Equal(SeriesId, dto.SeriesId);
        Assert.Equal("GT3 Cup", dto.SeriesName);
        Assert.Equal(new[] { 1, 2 }, dto.Weeks.Select(w => w.WeekNumber)); // ordered

        var w1 = dto.Weeks[0];
        Assert.Equal("Thruxton", w1.TrackName);
        Assert.NotNull(w1.Weather);
        Assert.Equal(28.9, w1.Weather!.TempHighC, precision: 1);
        Assert.Equal(20.2, w1.Weather.WindHighKph, precision: 1); // 5.6 m/s → 20.16 km/h
        Assert.Equal(1, w1.Weather.Skies);

        // BoP ordered by car name → BMW before Porsche.
        Assert.Equal(new[] { "BMW M4 GT3", "Porsche 718 GT4" }, w1.Bop.Select(b => b.CarName));
        Assert.Equal(10, w1.Bop[0].WeightPenaltyKg);
        Assert.Equal(-1.5, w1.Bop[0].PowerAdjustPct);

        Assert.True(w1.HasPersonalBest); // user has a PB at Thruxton

        var w2 = dto.Weeks[1];
        Assert.Null(w2.Weather);
        Assert.Empty(w2.Bop);
        Assert.False(w2.HasPersonalBest);
    }

    [Fact]
    public async Task GetScheduleAsync_AnonymousCaller_NoPersonalBestOverlay()
    {
        await using var db = await SeededAsync();
        var service = new ScheduleService(db);

        var dto = await service.GetScheduleAsync(SeriesId, userId: null, Ct);

        Assert.All(dto.Weeks, w => Assert.False(w.HasPersonalBest));
    }

    [Fact]
    public async Task GetScheduleAsync_UnknownCarBop_FallsBackToCarIdLabel()
    {
        await using var db = await SeededAsync();
        db.SeasonCarBops.Add(new SeasonCarBop { SeasonId = SeasonId, WeekNumber = 2, CarId = 999 });
        await db.SaveChangesAsync(Ct);
        var service = new ScheduleService(db);

        var dto = await service.GetScheduleAsync(SeriesId, null, Ct);

        Assert.Equal("Car 999", dto.Weeks.Single(w => w.WeekNumber == 2).Bop.Single().CarName);
    }

    [Fact]
    public async Task GetScheduleAsync_NoSeasonAtAll_ThrowsKeyNotFound()
    {
        await using var db = DbContextFactory.Create();
        db.Series.Add(new Series { Id = SeriesId, Name = "GT3 Cup" });
        await db.SaveChangesAsync(Ct);
        var service = new ScheduleService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetScheduleAsync(SeriesId, null, Ct));
    }

    [Fact]
    public async Task GetScheduleAsync_SeasonDeactivatedUpstreamButAlreadyRaced_StillServesIt()
    {
        // The inter-season gap: iRacing can clear the active flag on the season drivers just
        // finished before the next one starts. It stays the current season until a later season's
        // first race week begins, so the schedule must keep resolving instead of 404-ing.
        await using var db = await SeededAsync(active: false);
        var service = new ScheduleService(db);

        var dto = await service.GetScheduleAsync(SeriesId, null, Ct);

        Assert.Equal(2, dto.Weeks.Count);
        Assert.Equal("Thruxton", dto.Weeks.Single(w => w.WeekNumber == 1).TrackName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void MapWeather_MissingOrInvalid_ReturnsNull(string? json) =>
        Assert.Null(ScheduleService.MapWeather(json));
}
