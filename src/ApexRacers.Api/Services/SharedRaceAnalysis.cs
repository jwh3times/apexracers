using ApexRacers.Api.Dtos;

namespace ApexRacers.Api.Services;

/// <summary>One race both drivers ran, projected from the local subsession results.</summary>
public record SharedRaceInput(
    int SubsessionId,
    DateTimeOffset StartTime,
    string TrackName,
    int YourFinish,
    int RivalFinish,
    int YourIRatingDelta,
    int RivalIRatingDelta,
    int YourIncidents,
    int RivalIncidents,
    double YourBestLapSeconds,
    double RivalBestLapSeconds);

/// <summary>
/// Pure head-to-head summary over the races two drivers share. Finish positions are overall
/// (lower is better); best-lap-per-track ignores sentinel (-1/0) laps. Extracted as a pure
/// helper so the tally/pace math is unit-tested directly (mirrors <c>LapAnalysis</c>).
/// </summary>
public static class SharedRaceAnalysis
{
    public static SharedRaceSummaryDto Summarize(IReadOnlyList<SharedRaceInput> races)
    {
        var ordered = races.OrderByDescending(r => r.StartTime).ToList();

        var rows = ordered
            .Select(r => new SharedRaceRowDto(
                r.SubsessionId, r.StartTime, r.TrackName, r.YourFinish, r.RivalFinish,
                r.YourIRatingDelta, r.RivalIRatingDelta, r.YourIncidents, r.RivalIncidents))
            .ToList();

        var trackPace = races
            .GroupBy(r => r.TrackName)
            .Select(g => new SharedTrackPaceDto(
                g.Key,
                BestLap(g.Select(x => x.YourBestLapSeconds)),
                BestLap(g.Select(x => x.RivalBestLapSeconds))))
            .OrderBy(p => p.TrackName, StringComparer.Ordinal)
            .ToList();

        return new SharedRaceSummaryDto(
            ordered.Count,
            ordered.Count(r => r.YourFinish < r.RivalFinish),
            ordered.Count(r => r.RivalFinish < r.YourFinish),
            rows,
            trackPace);
    }

    /// <summary>Fastest valid lap in a set; -1 when none is valid (lap times use -1/0 sentinels).</summary>
    private static double BestLap(IEnumerable<double> laps)
    {
        var valid = laps.Where(l => l > 0).ToList();
        return valid.Count > 0 ? Math.Round(valid.Min(), 3) : -1d;
    }
}
