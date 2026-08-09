using Aydsko.iRacingData;
using Aydsko.iRacingData.Stats;

namespace ApexRacers.Api.Services;

/// <summary>
/// The world-record lap for a car+track, fetched on demand through
/// <see cref="CachedIRacingClient"/> (24-hour TTL; the SDK auto-downloads the chunked
/// records). Resolves to the single fastest valid lap across every record holder and
/// session type. Returns null when iRacing isn't configured and the value isn't cached
/// (or no record exists), so the percentile path degrades gracefully rather than failing.
/// A seeded cache row is honored even without creds (the demo-data preview seeds wr: rows).
/// </summary>
public class WorldRecordService(CachedIRacingClient cached)
{
    public async Task<double?> GetWorldRecordLapSecondsAsync(
        int carId, int trackId, CancellationToken ct)
    {
        try
        {
            return await cached.GetOrFetchAsync<double?>(
                IRacingCacheKeys.WorldRecord(carId, trackId),
                async c => FastestLapSeconds(
                    (await c.GetWorldRecordsAsync(carId, trackId, null, null, ct)).Data.Item2),
                ct);
        }
        catch (IRacingNotConfiguredException)
        {
            return null;
        }
    }

    /// <summary>
    /// The fastest valid lap (seconds) across all record holders and session types,
    /// or null when there are none. Pure + testable.
    /// </summary>
    public static double? FastestLapSeconds(IEnumerable<WorldRecordEntry>? entries)
    {
        if (entries is null)
            return null;

        double? best = null;
        foreach (var e in entries)
        {
            foreach (var lap in new[]
                     {
                         e.QualifyLapTime, e.RaceLapTime, e.TimeTrialLapTime, e.PracticeLapTime,
                     })
            {
                if (lap is { TotalSeconds: > 0 } && (best is null || lap.Value.TotalSeconds < best))
                    best = lap.Value.TotalSeconds;
            }
        }

        return best is null ? null : Math.Round(best.Value, 4);
    }
}
