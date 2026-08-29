using ApexRacers.Api.Dtos;

namespace ApexRacers.Api.Services;

/// <summary>
/// Pure pace statistics over a driver's laps. Summary metrics use the Clean Laps
/// (timed and incident-free) so a single off-track lap doesn't distort the picture;
/// the fastest lap considers any timed lap. Extracted as a pure helper so the math is
/// unit-tested directly (mirrors <c>SubsessionIndexer</c>).
/// </summary>
public static class LapAnalysis
{
    public static (double Mean, double StdDev, double Fastest, double Deg) Compute(
        IReadOnlyList<LapDto> laps)
    {
        var timed = laps.Where(l => l.Timed).ToList();
        var fastest = timed.Count > 0 ? timed.Min(l => l.LapTimeSeconds) : 0d;

        var clean = timed.Where(l => !l.Incident).ToList();
        if (clean.Count == 0)
            return (0d, 0d, Math.Round(fastest, 3), 0d);

        var times = clean.Select(l => l.LapTimeSeconds).ToList();
        var mean = times.Average();
        var variance = times.Sum(t => (t - mean) * (t - mean)) / times.Count;
        var std = Math.Sqrt(variance);
        var deg = clean.Count >= 2 ? Slope(clean) : 0d;

        return (Math.Round(mean, 3), Math.Round(std, 3), Math.Round(fastest, 3), Math.Round(deg, 4));
    }

    /// <summary>Least-squares slope of lap time (seconds) vs lap number.</summary>
    private static double Slope(IReadOnlyList<LapDto> laps)
    {
        var xMean = laps.Average(l => (double)l.LapNumber);
        var yMean = laps.Average(l => l.LapTimeSeconds);

        double num = 0, den = 0;
        foreach (var l in laps)
        {
            var dx = l.LapNumber - xMean;
            num += dx * (l.LapTimeSeconds - yMean);
            den += dx * dx;
        }

        return den == 0 ? 0d : num / den;
    }
}
