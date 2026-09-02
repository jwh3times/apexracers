using ApexRacers.Core;
using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoStandingsDataTests
{
    [Fact]
    public void BuildStandings_RankedFromOne_IncludesDemoDriverWithDivision()
    {
        var rows = DemoStandingsData.BuildStandings(6115, 100);
        Assert.Equal(1, rows[0].Standing);
        for (var i = 1; i < rows.Count; i++) Assert.Equal(i + 1, rows[i].Standing);
        var me = Assert.Single(rows, r => r.CustomerId == DemoData.DriverCustId);
        Assert.True(me.Division >= 1);
    }

    [Fact]
    public void BuildQualify_HasWeekAndSortedLapTimes()
    {
        var rows = DemoStandingsData.BuildQualify(6115, 100, week: 2);
        Assert.All(rows, r => Assert.Equal(2, r.Week));
        for (var i = 1; i < rows.Count; i++)
            Assert.True(rows[i - 1].BestQualLapSeconds <= rows[i].BestQualLapSeconds);
        Assert.Contains(rows, r => r.CustomerId == DemoData.DriverCustId);
    }

    [Fact]
    public void BuildTtStandings_HasTtRating() =>
        Assert.All(DemoStandingsData.BuildTtStandings(6115, 100), r => Assert.NotNull(r.TtRating));
}
