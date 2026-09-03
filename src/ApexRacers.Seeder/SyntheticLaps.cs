namespace ApexRacers.Seeder;

/// <summary>
/// Synthetic lap-time generation shared by the main seeder (Program.cs) and the CI
/// catalog seeder. Randomness is seeded from ids (stable within a process run);
/// idempotency comes from the callers' already-seeded checks, not from these values.
/// </summary>
public static class SyntheticLaps
{
    /// <summary>Deterministic skill factor for a driver: 0 = fastest, 1 = slowest.</summary>
    public static double ComputeSkillFactor(long driverId)
    {
        var rng = new Random((int)(driverId ^ (driverId >> 16)));
        return Math.Clamp(NextGaussian(rng, 0.55, 0.20), 0.0, 1.0);
    }

    /// <summary>Per-car offset: spreads cars ±1.5 s within the class.</summary>
    public static double GetCarOffset(int carId)
    {
        var rng = new Random(HashCode.Combine(carId, 0x5F3759DF));
        return (rng.NextDouble() - 0.5) * 3.0;
    }

    /// <summary>Generates a lap time from a base time, skill factor, and gaussian noise.</summary>
    public static double GenerateLapTime(
        long driverId, int carId, int raceWeekIndex,
        double baseLapSeconds, double skillFactor, double stdDev)
    {
        int seed = HashCode.Combine((int)(driverId & 0x7FFFFFFF), carId, raceWeekIndex);
        var rng = new Random(seed);
        double lapTime = baseLapSeconds
            + ((skillFactor - 0.5) * stdDev * 5.0)
            + NextGaussian(rng, 0.0, stdDev * 0.3);
        return Math.Max(lapTime, baseLapSeconds * 0.97);
    }

    /// <summary>Box-Muller transform.</summary>
    public static double NextGaussian(Random rng, double mean, double stdDev)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        double z  = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * z;
    }
}
