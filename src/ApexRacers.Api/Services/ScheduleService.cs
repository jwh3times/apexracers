using System.Text.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// The active-season schedule for a series: per-week track, date, weather forecast,
/// and per-car Balance of Performance (all bulk-ingested by the worker).
/// Private upload familiarity remains unavailable until it joins verified ownership
/// and protected personal publication; this independent endpoint reads no Uploaded Laps.
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
            .OrderBy(w => w.RaceWeekIndex)
            .Select(w => new
            {
                w.RaceWeekIndex,
                TrackName = w.Track.Name,
                w.Track.ConfigName,
                w.StartDate,
                WeatherSummaryJson = db.Provenance == DataProvenance.Demo && db.EvidenceCopyMarkers.Any(c => c.Id == w.DemoWeatherEvidenceCopyId
                    && c.Provenance == DataProvenance.Demo && c.UnavailableAt == null && c.VerifiedRemovedAt == null
                    && db.EvidencePurposes.Any(p => p.Id == c.PurposeId && p.Generation == c.Generation && p.OriginalEndedAt == null
                        && p.Provenance == c.Provenance && (p.Kind == EvidencePurposeKind.SyntheticPreview
                            || p.Kind == EvidencePurposeKind.IndependentOfficial || p.Kind == EvidencePurposeKind.AuthorizedHistory))) ? w.DemoWeatherSummaryJson
                    : db.Provenance == DataProvenance.Real && w.WeatherProvenance == DataProvenance.Real
                        && db.EvidenceCopyMarkers.Any(c => c.Id == w.WeatherEvidenceCopyId && c.Provenance == DataProvenance.Real
                            && c.UnavailableAt == null && c.VerifiedRemovedAt == null && db.EvidencePurposes.Any(p => p.Id == c.PurposeId
                                && p.Generation == c.Generation && p.OriginalEndedAt == null && p.Provenance == c.Provenance
                                && (p.Kind == EvidencePurposeKind.IndependentOfficial || p.Kind == EvidencePurposeKind.AuthorizedHistory)))
                        ? w.WeatherSummaryJson : null,
            })
            .ToListAsync(ct);

        var bop = await db.SeasonCarBops
            .Where(b => b.SeasonId == season.Id)
            .ToListAsync(ct);
        var bopByRaceWeekIndex = bop
            .GroupBy(b => b.RaceWeekIndex)
            .ToDictionary(g => g.Key, g => g.ToList());

        var carIds = bop.Select(b => b.CarId).Distinct().ToList();
        var carNames = await db.Cars
            .Where(c => carIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var weekDtos = weeks.Select(w => new ScheduleWeekDto(
            w.RaceWeekIndex,
            w.TrackName,
            ConfigurationName.NullIfAbsent(w.ConfigName),
            w.StartDate,
            MapWeather(w.WeatherSummaryJson),
            (bopByRaceWeekIndex.TryGetValue(w.RaceWeekIndex, out var list) ? list : [])
                .Select(b => new CarBopDto(
                    b.CarId,
                    carNames.TryGetValue(b.CarId, out var name) ? name : $"Car {b.CarId}",
                    b.WeightPenaltyKg,
                    b.PowerAdjustPct,
                    b.MaxPctFuelFill,
                    b.MaxDryTireSets))
                .OrderBy(c => c.CarName)
                .ToList(),
            HasUploadedLapAtTrack: false))
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
