using ApexRacers.Api.Services;
using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoScheduleDataTests
{
    [Fact]
    public void WeatherJson_RoundTripsThroughMapWeather()
    {
        var w = ScheduleService.MapWeather(DemoScheduleData.WeatherJson());
        Assert.NotNull(w);
        Assert.True(w!.TempHighC > 0);
    }

    [Fact]
    public void BuildBop_HasCompositeKeyAndPlausibleValues()
    {
        var bop = DemoScheduleData.BuildBop(6115, 2, 132);
        Assert.Equal(6115, bop.SeasonId);
        Assert.Equal(2, bop.WeekNumber);
        Assert.Equal(132, bop.CarId);
        Assert.True(bop.MaxPctFuelFill is > 0 and <= 100);
    }
}
