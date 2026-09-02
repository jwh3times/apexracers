using ApexRacers.Core;
using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoLeaderboardDataTests
{
    [Fact]
    public void Build_IsRankedDescendingByIRating_FromRankOne()
    {
        var rows = DemoLeaderboardData.Build(5);
        Assert.True(rows.Count >= 50);
        Assert.Equal(1, rows[0].Standing);
        for (var i = 1; i < rows.Count; i++)
        {
            Assert.Equal(i + 1, rows[i].Standing);
            Assert.True(rows[i - 1].IRating >= rows[i].IRating);
        }
        Assert.All(rows, r => Assert.Equal(5, r.CategoryId));
    }

    [Fact]
    public void Build_RatedCategory_IncludesDemoDriver()
    {
        Assert.Contains(DemoLeaderboardData.Build(5), r => r.CustomerId == DemoData.DriverCustId);
    }
}
