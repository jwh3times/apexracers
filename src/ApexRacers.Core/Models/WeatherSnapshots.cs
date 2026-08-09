using System.Text.Json.Serialization;

namespace ApexRacers.Core.Models;

/// <summary>
/// Owned snapshots of the two iRacing weather blocks this app persists as JSON:
/// <see cref="Subsession.WeatherJson"/> and <see cref="Week.WeatherSummaryJson"/>.
///
/// <para><b>Why these exist.</b> Both columns previously held <em>raw Aydsko SDK types</em>,
/// serialized straight from the wire model. The project rule — cache mapped DTOs, never raw SDK
/// types, because their wire shape drifts and they carry <c>[Obsolete]</c> fields — was enforced
/// on the <c>ExternalDataCache</c> path but not here, which is strictly worse: cache rows expire
/// within 24 hours, but these persist forever. An SDK upgrade that renames a
/// <c>[JsonPropertyName]</c> makes every historical row deserialize into a default-valued object
/// — a <em>successful</em> parse with zeros, not an exception, so the readers'
/// <c>catch (JsonException)</c> never fires and every historical race silently reports 0&#160;°C.
/// <c>ResultsWeather</c> already carries alias pairs (<c>TempValue</c>/<c>TemperatureValue</c>,
/// <c>RelHumidity</c>/<c>RelativeHumidity</c>), which is that drift mid-flight.</para>
///
/// <para><b>Why the property names are pinned to snake_case.</b> The SDK types serialize through
/// <c>[JsonPropertyName]</c> to iRacing's wire names, so every row already written is snake_case.
/// Keeping those exact names makes these records read every existing row unchanged — no migration,
/// no backfill — while moving ownership of the format from the SDK to this file. The names are now
/// ours: an SDK rename can no longer move them, and a wire-shape change surfaces as a compile
/// error in the ingest mapper instead of as silent zeros years later.</para>
///
/// <para>Do not rename these <c>JsonPropertyName</c> values. They are a persisted contract, not a
/// serialization preference.</para>
/// </summary>
public sealed record WeatherSnapshot
{
    [JsonPropertyName("temp_value")]
    public int TemperatureValue { get; init; }

    /// <summary>0 = Fahrenheit, otherwise Celsius. See <see cref="IRacingUnits.ToCelsius"/>.</summary>
    [JsonPropertyName("temp_units")]
    public int TemperatureUnits { get; init; }

    [JsonPropertyName("rel_humidity")]
    public int RelativeHumidity { get; init; }

    [JsonPropertyName("wind_value")]
    public int WindValue { get; init; }

    /// <summary>0 = mph, otherwise m/s. See <see cref="IRacingUnits.ToKph"/>.</summary>
    [JsonPropertyName("wind_units")]
    public int WindUnits { get; init; }

    [JsonPropertyName("skies")]
    public int Skies { get; init; }

    [JsonPropertyName("precip_time_pct")]
    public decimal PrecipitationTimePercentage { get; init; }
}

/// <summary>Owned snapshot of a race week's weather forecast. See <see cref="WeatherSnapshot"/>.</summary>
public sealed record WeatherForecastSnapshot
{
    [JsonPropertyName("temp_high")]
    public decimal TemperatureHigh { get; init; }

    [JsonPropertyName("temp_low")]
    public decimal TemperatureLow { get; init; }

    /// <summary>0 = Fahrenheit, otherwise Celsius.</summary>
    [JsonPropertyName("temp_units")]
    public int TemperatureUnits { get; init; }

    [JsonPropertyName("precip_chance")]
    public decimal PrecipitationChance { get; init; }

    [JsonPropertyName("wind_high")]
    public decimal WindHigh { get; init; }

    [JsonPropertyName("wind_low")]
    public decimal WindLow { get; init; }

    /// <summary>0 = mph, otherwise m/s.</summary>
    [JsonPropertyName("wind_units")]
    public int WindUnits { get; init; }

    [JsonPropertyName("skies_high")]
    public int SkiesHigh { get; init; }
}
