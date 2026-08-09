using ApexRacers.Core.Models;
using Aydsko.iRacingData.Results;
using Aydsko.iRacingData.Series;

namespace ApexRacers.Ingestion;

/// <summary>
/// Where the Aydsko SDK stops. Maps the two wire weather blocks into the owned snapshots that get
/// persisted, so nothing downstream of ingest ever sees an SDK type.
///
/// Pure and separately tested, mirroring <c>CatalogIngest</c> and <c>SubsessionIndexer</c> — the
/// worker itself is excluded from coverage as an I/O shell, so mapping that lives inline there is
/// mapping nothing verifies. This is also the point at which an SDK wire-shape change becomes a
/// compile error rather than a silent field of zeros in a column that is never rewritten.
/// </summary>
public static class WeatherIngest
{
    public static WeatherSnapshot? ToSnapshot(ResultsWeather? weather) =>
        weather is null
            ? null
            : new WeatherSnapshot
            {
                TemperatureValue = weather.TemperatureValue,
                TemperatureUnits = weather.TemperatureUnits,
                RelativeHumidity = weather.RelativeHumidity,
                WindValue = weather.WindValue,
                WindUnits = weather.WindUnits,
                Skies = weather.Skies,
                PrecipitationTimePercentage = weather.PrecipitationTimePercentage,
            };

    public static WeatherForecastSnapshot? ToSnapshot(WeatherSummary? summary) =>
        summary is null
            ? null
            : new WeatherForecastSnapshot
            {
                TemperatureHigh = summary.TemperatureHigh,
                TemperatureLow = summary.TemperatureLow,
                TemperatureUnits = summary.TemperatureUnits,
                PrecipitationChance = summary.PrecipitationChance,
                WindHigh = summary.WindHigh,
                WindLow = summary.WindLow,
                WindUnits = summary.WindUnits,
                SkiesHigh = summary.SkiesHigh,
            };
}
