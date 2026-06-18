using System.Text.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Aydsko.iRacingData.Series;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// The active-season schedule for a series: per-week track, date, weather forecast,
/// and per-car Balance of Performance (all bulk-ingested by the worker), plus a
/// personal overlay marking weeks where the caller has a personal best at that track.
/// Public endpoint; the overlay is populated only when an authenticated user id is passed.
/// </summary>
public class ScheduleService(AppDbContext db)
{
    public async Task<SeasonScheduleDto> GetScheduleAsync(
        int seriesId, Guid? userId, CancellationToken ct)
    {
        var season = await db.Seasons
            .Where(s => s.SeriesId == seriesId && s.Active)
            .OrderByDescending(s => s.Year)
            .ThenByDescending(s => s.Quarter)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"No active season for series {seriesId}.");

        var seriesName = await db.Series
            .Where(s => s.Id == seriesId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var weeks = await db.Weeks
            .Where(w => w.SeasonId == season.Id)
            .OrderBy(w => w.WeekNumber)
            .Select(w => new
            {
                w.WeekNumber,
                w.TrackId,
                TrackName = w.Track.Name,
                w.Track.ConfigName,
                w.StartDate,
                w.WeatherSummaryJson,
            })
            .ToListAsync(ct);

        var bop = await db.SeasonCarBops
            .Where(b => b.SeasonId == season.Id)
            .ToListAsync(ct);
        var bopByWeek = bop.GroupBy(b => b.WeekNumber).ToDictionary(g => g.Key, g => g.ToList());

        var carIds = bop.Select(b => b.CarId).Distinct().ToList();
        var carNames = await db.Cars
            .Where(c => carIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var pbTrackIds = userId is null
            ? []
            : (await db.PersonalLaps
                .Where(p => p.UserId == userId)
                .Select(p => p.TrackId)
                .Distinct()
                .ToListAsync(ct)).ToHashSet();

        var weekDtos = weeks.Select(w => new ScheduleWeekDto(
            w.WeekNumber,
            w.TrackName,
            w.ConfigName,
            w.StartDate,
            MapWeather(w.WeatherSummaryJson),
            (bopByWeek.TryGetValue(w.WeekNumber, out var list) ? list : [])
                .Select(b => new CarBopDto(
                    b.CarId,
                    carNames.TryGetValue(b.CarId, out var name) ? name : $"Car {b.CarId}",
                    b.WeightPenaltyKg,
                    b.PowerAdjustPct,
                    b.MaxPctFuelFill,
                    b.MaxDryTireSets))
                .OrderBy(c => c.CarName)
                .ToList(),
            pbTrackIds.Contains(w.TrackId)))
            .ToList();

        return new SeasonScheduleDto(seriesId, seriesName, weekDtos);
    }

    /// <summary>Deserializes a stored schedule weather_summary into a normalized DTO.</summary>
    public static WeatherSummaryDto? MapWeather(string? weatherJson)
    {
        if (string.IsNullOrWhiteSpace(weatherJson))
            return null;

        WeatherSummary? w;
        try
        {
            w = JsonSerializer.Deserialize<WeatherSummary>(weatherJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (w is null)
            return null;

        return new WeatherSummaryDto(
            ToCelsius((double)w.TemperatureHigh, w.TemperatureUnits),
            ToCelsius((double)w.TemperatureLow, w.TemperatureUnits),
            (double)w.PrecipitationChance,
            ToKph((double)w.WindHigh, w.WindUnits),
            ToKph((double)w.WindLow, w.WindUnits),
            w.SkiesHigh);
    }

    /// <summary>iRacing temp units: 0 = Fahrenheit, otherwise Celsius. Pure + testable.</summary>
    public static double ToCelsius(double value, int units) =>
        units == 0 ? Math.Round((value - 32) * 5.0 / 9.0, 1) : Math.Round(value, 1);

    /// <summary>iRacing wind units: 0 = mph, otherwise m/s. Returns km/h. Pure + testable.</summary>
    public static double ToKph(double value, int units) =>
        units == 0 ? Math.Round(value * 1.60934, 1) : Math.Round(value * 3.6, 1);
}
