using System.Text.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Ingestion;
using Aydsko.iRacingData.Results;
using Aydsko.iRacingData.Series;
using Xunit;

namespace ApexRacers.Tests.Ingestion;

/// <summary>
/// The round-trip these types exist for: SDK → owned snapshot → JSON → read path.
///
/// Nothing covered this before. The mapping lived inline in the worker, which is excluded from
/// coverage as an I/O shell, and the read paths deserialized the SDK type directly — so the one
/// thing most likely to break on an SDK upgrade was the one thing nothing verified.
/// </summary>
public class WeatherIngestTests
{
    [Fact]
    public void ResultsWeather_RoundTripsThroughTheSubsessionReadPath()
    {
        var wire = new ResultsWeather
        {
            TemperatureValue = 50, // °F
            TemperatureUnits = 0,
            RelativeHumidity = 62,
            WindValue = 10, // mph
            WindUnits = 0,
            Skies = 2,
            PrecipitationTimePercentage = 15m,
        };

        var json = JsonSerializer.Serialize(WeatherIngest.ToSnapshot(wire));
        var dto = SubsessionDetailService.MapWeather(json);

        Assert.NotNull(dto);
        Assert.Equal(10.0, dto.TempCelsius, precision: 1); // 50 °F
        Assert.Equal(62, dto.RelHumidity);
        Assert.Equal(16.1, dto.WindKph, precision: 1); // 10 mph
        Assert.Equal(2, dto.Skies);
        Assert.Equal(15.0, dto.PrecipChance, precision: 1);
    }

    [Fact]
    public void WeatherSummary_RoundTripsThroughTheScheduleReadPath()
    {
        var wire = new WeatherSummary
        {
            TemperatureHigh = 26.0m,
            TemperatureLow = 21.0m,
            TemperatureUnits = 1, // Celsius
            PrecipitationChance = 10m,
            WindHigh = 5m, // m/s
            WindLow = 2m,
            WindUnits = 1,
            SkiesHigh = 1,
        };

        var json = JsonSerializer.Serialize(WeatherIngest.ToSnapshot(wire));
        var dto = ScheduleService.MapWeather(json);

        Assert.NotNull(dto);
        Assert.Equal(26.0, dto.TempHighC, precision: 1);
        Assert.Equal(21.0, dto.TempLowC, precision: 1);
        Assert.Equal(10.0, dto.PrecipChancePct, precision: 1);
        Assert.Equal(18.0, dto.WindHighKph, precision: 1); // 5 m/s
        Assert.Equal(7.2, dto.WindLowKph, precision: 1);
        Assert.Equal(1, dto.Skies);
    }

    [Fact]
    public void NullWireBlocks_MapToNull()
    {
        Assert.Null(WeatherIngest.ToSnapshot((ResultsWeather?)null));
        Assert.Null(WeatherIngest.ToSnapshot((WeatherSummary?)null));
    }

    // ── The persisted contract ────────────────────────────────────────────────

    /// <summary>
    /// Rows written before these snapshots existed hold the SDK's own serialization, and rows in
    /// every deployed database are never rewritten (a subsession is ingested once). The snapshots
    /// therefore pin the SDK's wire names rather than inventing PascalCase ones — this asserts
    /// that, so a well-meaning rename cannot silently orphan every historical row.
    /// </summary>
    [Fact]
    public void SnapshotsSerializeUnderTheWireNamesAlreadyOnDisk()
    {
        var weather = JsonSerializer.Serialize(new WeatherSnapshot { TemperatureValue = 21 });
        Assert.Contains("\"temp_value\":21", weather);
        Assert.Contains("\"temp_units\"", weather);
        Assert.Contains("\"rel_humidity\"", weather);
        Assert.Contains("\"wind_value\"", weather);
        Assert.Contains("\"wind_units\"", weather);
        Assert.Contains("\"precip_time_pct\"", weather);

        var forecast = JsonSerializer.Serialize(new WeatherForecastSnapshot { SkiesHigh = 3 });
        Assert.Contains("\"skies_high\":3", forecast);
        Assert.Contains("\"temp_high\"", forecast);
        Assert.Contains("\"temp_low\"", forecast);
        Assert.Contains("\"precip_chance\"", forecast);
        Assert.Contains("\"wind_high\"", forecast);
        Assert.Contains("\"wind_low\"", forecast);
    }

    [Fact]
    public void RowsWrittenBeforeTheSnapshotsExisted_StillRead()
    {
        // Byte-for-byte what JsonSerializer.Serialize(ResultsWeather) produced, extra wire fields
        // and all — the shape sitting in Subsession.WeatherJson today.
        const string legacy = """
            {"simulated_start_time":"2026-05-01T12:00:00","allow_fog":false,"track_water":0,
             "precip_time_pct":15,"precip_mm_final_session":0,"precip_option":0,
             "precip_mm2hr_before_final_session":0,"simulated_time_multiplier":1,"version":1,
             "type":3,"temp_units":0,"temp_value":50,"rel_humidity":62,"fog":0,"wind_dir":2,
             "wind_units":0,"wind_value":10,"skies":2,"weather_var_initial":0,
             "weather_var_ongoing":0,"time_of_day":1}
            """;

        var dto = SubsessionDetailService.MapWeather(legacy);

        Assert.NotNull(dto);
        Assert.Equal(10.0, dto.TempCelsius, precision: 1);
        Assert.Equal(62, dto.RelHumidity);
        Assert.Equal(16.1, dto.WindKph, precision: 1);
    }

    [Fact]
    public void LegacyForecastRows_StillRead()
    {
        const string legacy = """
            {"max_precip_rate":0,"max_precip_rate_desc":null,"precip_chance":10,"skies_high":1,
             "skies_low":1,"temp_high":26.0,"temp_low":21.0,"temp_units":1,"wind_high":5,
             "wind_low":2,"wind_units":1}
            """;

        var dto = ScheduleService.MapWeather(legacy);

        Assert.NotNull(dto);
        Assert.Equal(26.0, dto.TempHighC, precision: 1);
        Assert.Equal(18.0, dto.WindHighKph, precision: 1);
        Assert.Equal(1, dto.Skies);
    }
}
