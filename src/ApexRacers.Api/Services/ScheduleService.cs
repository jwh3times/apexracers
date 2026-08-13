using System.Text.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
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
        var season = await db.CurrentSeasonOrThrowAsync(seriesId, ct);
        var seriesName = await db.SeriesNameAsync(seriesId, ct);

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

        WeatherForecastSnapshot? w;
        try
        {
            w = JsonSerializer.Deserialize<WeatherForecastSnapshot>(weatherJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (w is null)
            return null;

        return new WeatherSummaryDto(
            IRacingUnits.ToCelsius((double)w.TemperatureHigh, w.TemperatureUnits),
            IRacingUnits.ToCelsius((double)w.TemperatureLow, w.TemperatureUnits),
            (double)w.PrecipitationChance,
            IRacingUnits.ToKph((double)w.WindHigh, w.WindUnits),
            IRacingUnits.ToKph((double)w.WindLow, w.WindUnits),
            w.SkiesHigh);
    }
}
