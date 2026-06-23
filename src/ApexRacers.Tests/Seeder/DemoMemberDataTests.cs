using ApexRacers.Api.Services;
using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoMemberDataTests
{
    [Fact]
    public void BuildProfile_DemoDriver_HasThreeLicensesAndIdentity()
    {
        var p = DemoMemberData.BuildProfile(100_001);
        Assert.False(string.IsNullOrWhiteSpace(p.DisplayName));
        Assert.Equal(3, p.Licenses.Count);
        Assert.Contains(p.Licenses, l => l.CategoryId == 5); // Sports Car
        Assert.All(p.Licenses, l => Assert.True(l.Irating > 0));
    }

    [Fact]
    public void BuildChart_IsAscendingDatedHistory()
    {
        var pts = DemoMemberData.BuildChart(100_001, 5);
        Assert.True(pts.Count >= 8);
        Assert.All(pts, p => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", p.When));
        Assert.True(pts[^1].Value >= pts[0].Value); // trends up
    }

    [Fact]
    public void Builders_AreDeterministic()
    {
        Assert.Equal(DemoMemberData.BuildProfile(100_001), DemoMemberData.BuildProfile(100_001));
        Assert.Equal(DemoMemberData.BuildCareer(100_001), DemoMemberData.BuildCareer(100_001));
    }

    [Fact]
    public void RivalDiffersFromDemoDriver()
    {
        Assert.NotEqual(
            DemoMemberData.BuildProfile(100_001).DisplayName,
            DemoMemberData.BuildProfile(100_002).DisplayName);
    }
}
