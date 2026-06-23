using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoActivityDataTests
{
    [Fact]
    public void BuildAwards_NonEmpty_NewestFirst_WithThresholds()
    {
        var awards = DemoActivityData.BuildAwards(100_001);
        Assert.NotEmpty(awards);
        for (var i = 1; i < awards.Count; i++)
            Assert.True(awards[i - 1].AwardDate >= awards[i].AwardDate); // newest first
        Assert.All(awards, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
    }

    [Fact]
    public void BuildRecentRaces_NonEmpty_WithDeltasAndRealCarIds()
    {
        var races = DemoActivityData.BuildRecentRaces(100_001);
        Assert.NotEmpty(races);
        Assert.All(races, r => Assert.True(r.SubsessionId < 0)); // synthetic negative ids
        Assert.Contains(races, r => r.IRatingDelta != 0);
    }
}
