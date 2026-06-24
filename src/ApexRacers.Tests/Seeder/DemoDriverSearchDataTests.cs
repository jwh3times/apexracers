using ApexRacers.Core;
using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoDriverSearchDataTests
{
    [Fact]
    public void Terms_AreLowercase_MinTwoChars_WithRealResults()
    {
        Assert.NotEmpty(DemoDriverSearchData.Terms);
        Assert.All(DemoDriverSearchData.Terms.Keys, k =>
        {
            Assert.Equal(k.ToLowerInvariant(), k);
            Assert.True(k.Length >= 2);
        });
        Assert.All(DemoDriverSearchData.Terms.Values, v => Assert.NotEmpty(v));
    }

    [Fact]
    public void RivalTerms_ReturnTheRival()
    {
        foreach (var term in new[] { "rival", "racer", "riv" })
            Assert.Contains(DemoDriverSearchData.Terms[term], r => r.CustId == DemoData.RivalCustId);
    }
}
