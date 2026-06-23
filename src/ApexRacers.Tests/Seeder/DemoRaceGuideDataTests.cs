using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoRaceGuideDataTests
{
    [Fact]
    public void Build_AlwaysLiveWindow_PassesTheNowFilter()
    {
        var rows = DemoRaceGuideData.Build([444, 9]);
        Assert.Equal(2, rows.Count);
        var now = DateTimeOffset.UtcNow;
        Assert.All(rows, r =>
        {
            Assert.True(r.End > now);                       // not yet ended
            Assert.True(r.Start <= now + TimeSpan.FromHours(3)); // within the horizon
            Assert.True(r.EntryCount > 0);
        });
        Assert.Equal(new[] { 444, 9 }, rows.Select(r => r.SeriesId));
    }
}
