using System.Text.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData.Results;
using Xunit;

namespace ApexRacers.Tests.Services;

public class SubsessionDetailServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string WeatherJson(
        int tempValue, int tempUnits, int relHum, int windValue, int windUnits, int skies, decimal precip) =>
        JsonSerializer.Serialize(new ResultsWeather
        {
            TemperatureValue = tempValue,
            TemperatureUnits = tempUnits,
            RelativeHumidity = relHum,
            WindValue = windValue,
            WindUnits = windUnits,
            Skies = skies,
            PrecipitationTimePercentage = precip,
        });

    private static async Task<AppDbContext> SeededDbAsync(string? weatherJson)
    {
        var db = DbContextFactory.Create();
        db.Series.Add(new Series { Id = 444, Name = "GT3 Cup" });
        db.Seasons.Add(new Season { Id = 6115, SeriesId = 444 });
        db.Tracks.Add(new Track { Id = 532, Name = "Thruxton", ConfigName = "Club" });
        db.Subsessions.Add(new Subsession
        {
            Id = 100,
            SeasonId = 6115,
            TrackId = 532,
            StartTime = DateTimeOffset.Parse("2026-05-29T17:15:00Z"),
            EventStrengthOfField = 2509,
            NumCautions = 1,
            NumLeadChanges = 2,
            CornersPerLap = 12,
            EventBestLapSeconds = 66.295,
            EventAverageLapSeconds = 66.89,
            EventLapsComplete = 18,
            WeatherJson = weatherJson,
        });
        db.SubsessionResults.AddRange(
            new SubsessionResult
            {
                SubsessionId = 100, CustId = 222, DisplayName = "Mid Pack",
                FinishPosition = 3, StartingPosition = 5, BestLapSeconds = 67.1,
                AverageLapSeconds = 67.5, Interval = 1.2, LapsLead = 0, Incidents = 4,
                Division = 2, NewIRating = 1854, OldIRating = 1907, NewSubLevel = 238, OldSubLevel = 237,
            },
            new SubsessionResult
            {
                SubsessionId = 100, CustId = 111, DisplayName = "Winner",
                FinishPosition = 1, StartingPosition = 1, BestLapSeconds = 66.295,
                AverageLapSeconds = 66.8, Interval = 0, LapsLead = 18, Incidents = 0,
                Division = 1, NewIRating = 2000, OldIRating = 1950, NewSubLevel = 415, OldSubLevel = 400,
            });
        await db.SaveChangesAsync(Ct);
        return db;
    }

    [Fact]
    public async Task GetAsync_MapsHeader_OrdersByFinish_AndComputesDeltas()
    {
        await using var db = await SeededDbAsync(WeatherJson(19, 1, 45, 5, 1, 1, 0m));
        var service = new SubsessionDetailService(db);

        var dto = await service.GetAsync(100, Ct);

        Assert.Equal(100, dto.SubsessionId);
        Assert.Equal("GT3 Cup", dto.SeriesName);
        Assert.Equal("Thruxton", dto.TrackName);
        Assert.Equal("Club", dto.TrackConfigName);
        Assert.Equal(2509, dto.StrengthOfField);
        Assert.Equal(1, dto.NumCautions);
        Assert.Equal(2, dto.NumLeadChanges);
        Assert.Equal(12, dto.CornersPerLap);
        Assert.Equal(66.295, dto.EventBestLapSeconds, precision: 3);
        Assert.Equal(18, dto.EventLapsComplete);

        // Ordered by finish: winner (P1) first.
        Assert.Equal(2, dto.Results.Count);
        Assert.Equal(1, dto.Results[0].FinishPosition);
        Assert.Equal("Winner", dto.Results[0].DriverName);
        Assert.Equal(2000 - 1950, dto.Results[0].IRatingDelta); // +50
        Assert.Equal((415 - 400) / 100.0, dto.Results[0].SrDelta, precision: 4); // +0.15

        Assert.Equal(3, dto.Results[1].FinishPosition);
        Assert.Equal(1854 - 1907, dto.Results[1].IRatingDelta); // -53
    }

    [Fact]
    public async Task GetAsync_MapsWeather()
    {
        await using var db = await SeededDbAsync(WeatherJson(19, 1, 45, 5, 1, 1, 0m));
        var service = new SubsessionDetailService(db);

        var weather = (await service.GetAsync(100, Ct)).Weather;

        Assert.NotNull(weather);
        Assert.Equal(19, weather!.TempCelsius, precision: 1); // units 1 = Celsius
        Assert.Equal(45, weather.RelHumidity);
        Assert.Equal(18.0, weather.WindKph, precision: 1); // 5 m/s → 18 km/h
        Assert.Equal(1, weather.Skies);
    }

    [Fact]
    public async Task GetAsync_NoWeatherJson_NullWeather()
    {
        await using var db = await SeededDbAsync(weatherJson: null);
        var service = new SubsessionDetailService(db);

        Assert.Null((await service.GetAsync(100, Ct)).Weather);
    }

    [Fact]
    public async Task GetAsync_UnknownSubsession_ThrowsKeyNotFound()
    {
        await using var db = DbContextFactory.Create();
        var service = new SubsessionDetailService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(999, Ct));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void MapWeather_MissingOrInvalid_ReturnsNull(string? json) =>
        Assert.Null(SubsessionDetailService.MapWeather(json));
}
