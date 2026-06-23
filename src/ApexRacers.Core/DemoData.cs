namespace ApexRacers.Core;

/// <summary>
/// Shared constants for the synthetic demo-data preview (the <c>iracing-demo</c> feature).
/// The demo driver is the first synthetic driver the seeder generates (custId range
/// 100001–100200); <c>MemberContext</c> resolves every demo user to this id so the
/// personalized pages render against the demo driver's synthetic results.
/// </summary>
public static class DemoData
{
    public const long DriverCustId = 100_001;
}
