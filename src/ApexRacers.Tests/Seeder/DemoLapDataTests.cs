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
        Assert.False(laps.Single(l => l.Incident).Valid);                   // incident lap not green
        var greenMin = laps.Where(l => l.Valid).Min(l => l.LapTimeSeconds);
        Assert.Equal(90.0, greenMin, precision: 3);                         // fastest valid == best
        Assert.All(laps.Where(l => l.Valid), l => Assert.True(l.LapTimeSeconds >= 90.0));
    }

    [Fact]
    public void BuildLaps_IsDeterministic()
    {
        Assert.Equal(DemoLapData.BuildLaps(-10, 90.0), DemoLapData.BuildLaps(-10, 90.0));
    }
}
