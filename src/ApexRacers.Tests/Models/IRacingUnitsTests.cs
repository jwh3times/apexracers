using ApexRacers.Core;
using Xunit;

namespace ApexRacers.Tests.Models;

/// <summary>
/// One home for the conversions that were previously written twice, once beside each read path,
/// each with its own passing tests.
/// </summary>
public class IRacingUnitsTests
{
    [Theory]
    [InlineData(28.9, 1, 28.9)] // already Celsius
    [InlineData(19, 1, 19.0)]
    [InlineData(50.0, 0, 10.0)] // 50 °F
    [InlineData(32.0, 0, 0.0)] // freezing
    [InlineData(-40.0, 0, -40.0)] // the crossover point
    public void ToCelsius_ConvertsByUnits(double value, int units, double expected) =>
        Assert.Equal(expected, IRacingUnits.ToCelsius(value, units), precision: 1);

    [Theory]
    [InlineData(5.6, 1, 20.2)] // m/s
    [InlineData(5, 1, 18.0)]
    [InlineData(10.0, 0, 16.1)] // mph
    [InlineData(0, 0, 0.0)]
    public void ToKph_ConvertsByUnits(double value, int units, double expected) =>
        Assert.Equal(expected, IRacingUnits.ToKph(value, units), precision: 1);

    [Fact]
    public void AnyNonZeroUnit_MeansMetric()
    {
        // iRacing flags Fahrenheit/mph as 0 and everything else as metric, so the branch keys on
        // "is it zero", not on an enumeration of known metric codes.
        Assert.Equal(21.0, IRacingUnits.ToCelsius(21.0, 1), precision: 1);
        Assert.Equal(21.0, IRacingUnits.ToCelsius(21.0, 2), precision: 1);
    }

    [Fact]
    public void Rounds_ToOneDecimalPlace()
    {
        // 51 °F is 10.5555…; the persisted DTOs and the UI both want one place.
        Assert.Equal(10.6, IRacingUnits.ToCelsius(51.0, 0), precision: 1);
        Assert.Equal(1.6, IRacingUnits.ToKph(1.0, 0), precision: 1);
    }
}
