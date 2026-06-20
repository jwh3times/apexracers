using ApexRacers.Api.Services;
using Xunit;

namespace ApexRacers.Tests.Services;

public class StrategyAnalysisTests
{
    // ── WeatherRisk ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(14.9, true)]
    public void WeatherRisk_BelowMediumThreshold_IsLow(double precip, bool rainCapable)
    {
        var (level, note) = StrategyAnalysis.WeatherRisk(precip, rainCapable);
        Assert.Equal("Low", level);
        Assert.Equal("Dry conditions expected.", note);
    }

    [Theory]
    [InlineData(15.0)] // lower boundary inclusive
    [InlineData(39.9)]
    public void WeatherRisk_MediumBand_RainCapable_AdvisesWetSetupReady(double precip)
    {
        var (level, note) = StrategyAnalysis.WeatherRisk(precip, fieldRainCapable: true);
        Assert.Equal("Medium", level);
        Assert.Contains("wet setup ready", note);
    }

    [Fact]
    public void WeatherRisk_MediumBand_NotRainCapable_AdvisesDryOrAffected()
    {
        var (level, note) = StrategyAnalysis.WeatherRisk(20, fieldRainCapable: false);
        Assert.Equal("Medium", level);
        Assert.Contains("dry or be affected", note);
    }

    [Theory]
    [InlineData(40.0)] // lower boundary inclusive
    [InlineData(85.0)]
    public void WeatherRisk_HighBand_RainCapable_PrioritizesRainSetup(double precip)
    {
        var (level, note) = StrategyAnalysis.WeatherRisk(precip, fieldRainCapable: true);
        Assert.Equal("High", level);
        Assert.Contains("prioritize a rain setup", note);
    }

    [Fact]
    public void WeatherRisk_HighBand_NotRainCapable_WarnsNoRainTires()
    {
        var (level, note) = StrategyAnalysis.WeatherRisk(60, fieldRainCapable: false);
        Assert.Equal("High", level);
        Assert.Contains("aren't rain-capable", note);
    }

    // ── FuelNote ─────────────────────────────────────────────────────────────

    [Fact]
    public void FuelNote_CapBetweenZeroAndHundred_IsCapped()
    {
        var (capped, note) = StrategyAnalysis.FuelNote(92.5);
        Assert.True(capped);
        Assert.Contains("92.5%", note);
        Assert.Contains("extra stop", note);
    }

    [Fact]
    public void FuelNote_WholePercent_TrimsTrailingZeros()
    {
        var (capped, note) = StrategyAnalysis.FuelNote(90);
        Assert.True(capped);
        Assert.Contains("90%", note);
        Assert.DoesNotContain("90.0", note);
    }

    [Theory]
    [InlineData(0)]    // unset / no data
    [InlineData(100)]  // full tank
    [InlineData(120)]  // over-fill is not a restriction
    public void FuelNote_NoRestriction_IsNotCapped(double fill)
    {
        var (capped, note) = StrategyAnalysis.FuelNote(fill);
        Assert.False(capped);
        Assert.Equal("No fuel-fill restriction.", note);
    }

    // ── TireNote ─────────────────────────────────────────────────────────────

    [Fact]
    public void TireNote_SingleSet_IsLimited_Singular()
    {
        var (limited, note) = StrategyAnalysis.TireNote(1);
        Assert.True(limited);
        Assert.Contains("1 dry set ", note); // singular, trailing space before em-dash phrase
        Assert.DoesNotContain("sets", note);
    }

    [Fact]
    public void TireNote_MultipleSets_IsLimited_Plural()
    {
        var (limited, note) = StrategyAnalysis.TireNote(3);
        Assert.True(limited);
        Assert.Contains("3 dry sets", note);
    }

    [Fact]
    public void TireNote_Zero_IsUnlimited()
    {
        var (limited, note) = StrategyAnalysis.TireNote(0);
        Assert.False(limited);
        Assert.Equal("Unlimited dry tire sets.", note);
    }

    // ── BopShift ─────────────────────────────────────────────────────────────

    [Fact]
    public void BopShift_NoPreviousWeek_ReturnsDashAndZeroDeltas()
    {
        var (weightDelta, powerDelta, trend) = StrategyAnalysis.BopShift(null, 10, null, -2);
        Assert.Equal(0, weightDelta);
        Assert.Equal(0, powerDelta);
        Assert.Equal("—", trend);
    }

    [Fact]
    public void BopShift_NoChange_IsUnchanged()
    {
        var (weightDelta, powerDelta, trend) = StrategyAnalysis.BopShift(10, 10, -2, -2);
        Assert.Equal(0, weightDelta);
        Assert.Equal(0, powerDelta);
        Assert.Equal("Unchanged", trend);
    }

    [Theory]
    [InlineData(0, 10, 0, 0)]    // heavier, same power
    [InlineData(5, 5, 0, -1.5)]  // same weight, down on power
    public void BopShift_HeavierOrDownOnPower_IsNerfed(
        double prevW, double w, double prevP, double p)
    {
        var (_, _, trend) = StrategyAnalysis.BopShift(prevW, w, prevP, p);
        Assert.Equal("Nerfed", trend);
    }

    [Theory]
    [InlineData(10, 5, 0, 0)]    // lighter, same power
    [InlineData(5, 5, -1.5, 0)]  // same weight, up on power
    public void BopShift_LighterOrUpOnPower_IsBuffed(
        double prevW, double w, double prevP, double p)
    {
        var (_, _, trend) = StrategyAnalysis.BopShift(prevW, w, prevP, p);
        Assert.Equal("Buffed", trend);
    }

    [Theory]
    [InlineData(0, 10, 0, 2)]    // heavier AND more power
    [InlineData(10, 0, -2, -4)]  // lighter AND less power
    public void BopShift_OpposingMoves_IsMixed(
        double prevW, double w, double prevP, double p)
    {
        var (_, _, trend) = StrategyAnalysis.BopShift(prevW, w, prevP, p);
        Assert.Equal("Mixed", trend);
    }

    [Fact]
    public void BopShift_RoundsDeltasToOneDecimal()
    {
        var (weightDelta, powerDelta, _) = StrategyAnalysis.BopShift(5.0, 10.12, -1.0, -2.16);
        Assert.Equal(5.1, weightDelta);   // 10.12 - 5.0 = 5.12 → 5.1
        Assert.Equal(-1.2, powerDelta);   // -2.16 - (-1.0) = -1.16 → -1.2
    }
}
