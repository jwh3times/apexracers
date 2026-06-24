using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoWorldRecordTests
{
    [Fact]
    public void RecordSeconds_IsTwoPercentBelowFieldBest_Rounded()
    {
        Assert.Equal(98.0, DemoWorldRecord.RecordSeconds(100.0), precision: 4);   // 100 * 0.98
        Assert.Equal(65.66, DemoWorldRecord.RecordSeconds(67.0), precision: 2);   // 67 * 0.98 = 65.66
    }

    [Fact]
    public void RecordSeconds_IsFasterThanFieldBest()
    {
        Assert.True(DemoWorldRecord.RecordSeconds(90.0) < 90.0);
    }
}
