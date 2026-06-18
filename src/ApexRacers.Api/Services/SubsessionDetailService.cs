using System.Text.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Aydsko.iRacingData.Results;
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

        ResultsWeather? w;
        try
        {
            w = JsonSerializer.Deserialize<ResultsWeather>(weatherJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (w is null)
            return null;

        return new WeatherDto(
            TempToCelsius(w.TemperatureValue, w.TemperatureUnits),
            w.RelativeHumidity,
            WindToKph(w.WindValue, w.WindUnits),
            w.Skies,
            (double)w.PrecipitationTimePercentage);
    }

    /// <summary>iRacing temp units: 0 = Fahrenheit, otherwise Celsius. Pure + testable.</summary>
    public static double TempToCelsius(int tempValue, int tempUnits) =>
        tempUnits == 0 ? Math.Round((tempValue - 32) * 5.0 / 9.0, 1) : tempValue;

    /// <summary>iRacing wind units: 0 = mph, otherwise m/s. Returns km/h. Pure + testable.</summary>
    public static double WindToKph(int windValue, int windUnits) =>
        windUnits == 0 ? Math.Round(windValue * 1.60934, 1) : Math.Round(windValue * 3.6, 1);
}
