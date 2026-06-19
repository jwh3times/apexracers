using ApexRacers.Api.Dtos;
using Aydsko.iRacingData;

namespace ApexRacers.Api.Services;

/// <summary>
/// A single driver's per-lap pace for one race, fetched on demand from iRacing through
/// <see cref="CachedIRacingClient"/> (24-hour TTL — completed race data is immutable).
/// The SDK auto-downloads the chunked lap rows; we map them and compute pace stats.
/// </summary>
public class LapDataService(CachedIRacingClient cached)
{
    public async Task<DriverLapsDto> GetDriverLapsAsync(
        int subsessionId, long custId, CancellationToken ct) =>
        // Cache the fully-mapped DTO (laps + computed pace stats), not the raw SDK lap rows, so the
        // cached JSON is decoupled from the Aydsko wire shape. Race data is immutable → 24-hour TTL.
        await cached.GetOrFetchAsync(
            $"laps:{subsessionId}:{custId}", TimeSpan.FromHours(24),
            async c =>
            {
                var raw = (await c.GetSingleDriverSubsessionLapsAsync(
                    subsessionId, simSessionNumber: 0, (int)custId, ct)).Data.Item2;

                var laps = (raw ?? [])
                    .Select(l =>
                    {
                        var seconds = l.LapTime?.TotalSeconds ?? -1;
                        return new LapDto(l.LapNumber, seconds, l.Incident, seconds > 0);
                    })
                    .OrderBy(l => l.LapNumber)
                    .ToList();

                var (mean, std, fastest, deg) = LapAnalysis.Compute(laps);
                return new DriverLapsDto(subsessionId, custId, mean, std, fastest, deg, laps);
            },
            ct);
}
