using System.Text.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Reads one ingested subsession's full classified field plus session context (SOF,
/// cautions, lead changes, weather) from the local database — official race data, so
/// the endpoint is public. The stored weather block is the serialized iRacing payload;
/// it is deserialized and unit-normalized here.
/// </summary>
public class SubsessionDetailService(AppDbContext db)
{
    public async Task<SubsessionDetailDto> GetAsync(int subsessionId, CancellationToken ct)
    {
        var sub = await db.Subsessions
            .Include(s => s.Track)
            .FirstOrDefaultAsync(s => s.Id == subsessionId, ct)
            ?? throw new KeyNotFoundException($"Subsession {subsessionId} not found.");

        var seriesName = await (
            from se in db.Seasons
            join sr in db.Series on se.SeriesId equals sr.Id
            where se.Id == sub.SeasonId
            select sr.Name).FirstOrDefaultAsync(ct) ?? string.Empty;

        var results = await db.SubsessionResults
            .Where(r => r.SubsessionId == subsessionId)
            .OrderBy(r => r.FinishPosition)
            .Select(r => new SubsessionResultRowDto(
                r.CustId,
                r.DisplayName ?? string.Empty,
                r.FinishPosition,
                r.StartingPosition,
                r.BestLapSeconds,
                r.AverageLapSeconds,
                r.Interval,
                r.LapsLead,
                r.Incidents,
                r.Division,
                r.NewIRating - r.OldIRating,
                (r.NewSubLevel - r.OldSubLevel) / 100.0))
            .ToListAsync(ct);

        return new SubsessionDetailDto(
            sub.Id,
            sub.StartTime,
            seriesName,
            sub.Track.Name,
            string.IsNullOrEmpty(sub.Track.ConfigName) ? null : sub.Track.ConfigName,
            sub.EventStrengthOfField,
            sub.NumCautions,
            sub.NumLeadChanges,
            sub.CornersPerLap,
            sub.EventBestLapSeconds,
            sub.EventAverageLapSeconds,
            sub.EventLapsComplete,
            MapWeather(sub.WeatherJson),
            results);
    }

    /// <summary>
    /// Deserializes the stored iRacing weather block into a normalized <see cref="WeatherDto"/>.
    /// Returns null when there is no weather or the payload can't be parsed.
    /// </summary>
    public static WeatherDto? MapWeather(string? weatherJson)
    {
        if (string.IsNullOrWhiteSpace(weatherJson))
            return null;

        WeatherSnapshot? w;
        try
        {
            w = JsonSerializer.Deserialize<WeatherSnapshot>(weatherJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (w is null)
            return null;

        return new WeatherDto(
            IRacingUnits.ToCelsius(w.TemperatureValue, w.TemperatureUnits),
            w.RelativeHumidity,
            IRacingUnits.ToKph(w.WindValue, w.WindUnits),
            w.Skies,
            (double)w.PrecipitationTimePercentage);
    }
}
