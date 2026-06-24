using ApexRacers.Api.Dtos;

namespace ApexRacers.Seeder.Demo;

/// <summary>Pure builder for a deterministic ~30-lap pace trace around a driver's best lap: an out-lap,
/// a green run with a slight positive degradation slope (fastest == best), and one incident lap. The
/// incident lap position is deterministic per subsessionId so re-seeding is stable.</summary>
public static class DemoLapData
{
    public static List<LapDto> BuildLaps(int subsessionId, double bestLap)
    {
        const int lapCount = 30;
        var incidentLap = 6 + Math.Abs(subsessionId) % 8;   // deterministic, in 6..13
        var laps = new List<LapDto>(lapCount);

        for (var n = 1; n <= lapCount; n++)
        {
            if (n == incidentLap)
            {
                // Timed but flagged: red marker on the trace, excluded from green/fastest.
                laps.Add(new LapDto(n, Math.Round(bestLap * 1.15, 3), Incident: true, Valid: false));
                continue;
            }

            // Lap 1 is an out-lap (slower); lap 2 is the fastest (== best); the rest degrade slightly.
            var seconds = n switch
            {
                1 => Math.Round(bestLap * 1.06, 3),
                2 => Math.Round(bestLap, 3),
                _ => Math.Round(bestLap + (n - 2) * 0.015 + (Math.Abs(subsessionId) + n) % 4 * 0.01, 3),
            };
            laps.Add(new LapDto(n, seconds, Incident: false, Valid: true));
        }

        return laps;
    }
}
