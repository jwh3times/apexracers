namespace ApexRacers.Core;

/// <summary>
/// Shared constants for the synthetic demo-data preview (the <c>iracing-demo</c> feature).
/// The demo driver is the first synthetic driver the seeder generates (custId range
/// 100001–100200); <c>SubjectDriverContext</c> resolves every demo User to this id so the
/// personalized pages render against the demo driver's synthetic results.
/// </summary>
public static class DemoData
{
    public const long DriverCustId = 100_001;

    /// <summary>A second synthetic pool driver used as the demo /compare rival.</summary>
    public const long RivalCustId = 100_002;

    /// <summary>Inclusive lower bound identifying synthetic demo cache rows.</summary>
    public static readonly DateTimeOffset CacheSentinelThreshold =
        new(9000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Far-future expiry written to every synthetic demo cache row.</summary>
    public static readonly DateTimeOffset CacheSentinel =
        new(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
