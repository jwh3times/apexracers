using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoLapDataTests
{
    [Fact]
    public void BuildLaps_30Laps_FastestEqualsBest_OneIncident_AscendingNumbers()
    {
        var laps = DemoLapData.BuildLaps(-10, 90.0);

        Assert.Equal(30, laps.Count);
        Assert.Equal(Enumerable.Range(1, 30), laps.Select(l => l.LapNumber));
        Assert.Equal(1, laps.Count(l => l.Incident));                       // exactly one incident lap
        Assert.True(laps.Single(l => l.Incident).Timed);                     // timed and incident are orthogonal
        Assert.All(laps, l => Assert.True(l.Timed));
        var cleanMin = laps.Where(l => !l.Incident).Min(l => l.LapTimeSeconds);
        Assert.Equal(90.0, cleanMin, precision: 3);                          // fastest clean == best
        Assert.All(laps, l => Assert.True(l.LapTimeSeconds >= 90.0));
    }

    [Fact]
    public void BuildLaps_IsDeterministic()
    {
        Assert.Equal(DemoLapData.BuildLaps(-10, 90.0), DemoLapData.BuildLaps(-10, 90.0));
    }
}
