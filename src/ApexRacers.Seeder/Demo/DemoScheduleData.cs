using System.Text.Json;
using ApexRacers.Core.Models;
using Aydsko.iRacingData.Series;

namespace ApexRacers.Seeder.Demo;

/// <summary>Pure builders for the persisted schedule gaps: per-week weather (a serialized
/// owned WeatherForecastSnapshot, matching ScheduleService.MapWeather) and per-car BoP rows.</summary>
public static class DemoScheduleData
{
    /// <summary>A fixed warm-dry forecast serialized exactly as the worker stores it.</summary>
    public static string WeatherJson() => JsonSerializer.Serialize(new WeatherForecastSnapshot
    {
        TemperatureHigh = 26.0m,
        TemperatureLow = 21.0m,
        TemperatureUnits = 1, // Celsius
        WindHigh = 4.5m,
        WindLow = 2.0m,
        WindUnits = 1, // m/s
        PrecipitationChance = 10m,
        SkiesHigh = 1,
    });

    /// <summary>Deterministic per-car BoP: spreads weight/power a little by car id.</summary>
    public static SeasonCarBop BuildBop(int seasonId, int week, int carId) => new()
    {
        SeasonId = seasonId,
        WeekNumber = week,
        CarId = carId,
        WeightPenaltyKg = carId % 3 * 5,        // 0/5/10
        PowerAdjustPct = -(carId % 4) * 0.5,    // 0 to -1.5
        MaxPctFuelFill = 100,
        MaxDryTireSets = 0,
    };
}
