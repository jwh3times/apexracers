namespace ApexRacers.Seeder.Demo;

/// <summary>Pure helper for the synthetic world-record lap: a realistic small margin (2%) below the
/// fastest synthetic field lap for a car+track, so the percentile page's WR gap is positive.</summary>
public static class DemoWorldRecord
{
    public static double RecordSeconds(double fieldBest) => Math.Round(fieldBest * 0.98, 4);
}
