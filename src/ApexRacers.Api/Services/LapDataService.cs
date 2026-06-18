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
        int subsessionId, long custId, CancellationToken ct)
    {
        var raw = await cached.GetOrFetchAsync(
            $"laps:{subsessionId}:{custId}", TimeSpan.FromHours(24),
            async c => (await c.GetSingleDriverSubsessionLapsAsync(
                subsessionId, simSessionNumber: 0, (int)custId, ct)).Data.Item2,
            ct);

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
    }
}
