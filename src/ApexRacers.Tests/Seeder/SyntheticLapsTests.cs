using ApexRacers.Seeder;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class SyntheticLapsTests
{
    [Fact]
    public void ComputeSkillFactor_IsClampedToUnitInterval()
    {
        for (long id = 100_001; id < 100_101; id++)
            Assert.InRange(SyntheticLaps.ComputeSkillFactor(id), 0.0, 1.0);
    }

    [Fact]
    public void GenerateLapTime_NeverFasterThanFloor()
    {
        var lap = SyntheticLaps.GenerateLapTime(100_001, 42, 1, 100.0, 0.0, 2.0);
        Assert.True(lap >= 97.0, $"expected >= 97.0 but was {lap}");
    }

    [Fact]
    public void GetCarOffset_SpreadsWithinBand()
    {
        for (var carId = 1; carId < 200; carId++)
            Assert.InRange(SyntheticLaps.GetCarOffset(carId), -1.5, 1.5);
    }

    [Fact]
    public void NextGaussian_ReturnsFiniteValues()
    {
        var rng = new System.Random(12345);
        for (var i = 0; i < 100; i++)
        {
            var v = SyntheticLaps.NextGaussian(rng, 0.0, 1.0);
            Assert.False(double.IsNaN(v) || double.IsInfinity(v));
        }
    }
}
