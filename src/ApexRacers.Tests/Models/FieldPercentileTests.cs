using ApexRacers.Core;
using Xunit;

namespace ApexRacers.Tests.Models;

/// <summary>
/// The arithmetic these cover was previously reachable only through a full DB fixture — the
/// percentile service tests document it in comments ("driverBest=80, beats 90+95 among others →
/// 2/4 = 50th percentile") precisely because it could not be asserted any other way.
/// </summary>
public class FieldPercentileTests
{
    // ── Rank ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Rank_CountsOnlyStrictlySlowerLaps()
    {
        // Beats 90 and 95; ties with 80; loses to 70.
        var others = new List<double> { 70, 80, 90, 95 };
        Assert.Equal(50.0, FieldPercentile.Rank(80, others), precision: 4);
    }

    [Fact]
    public void Rank_IsOneHundredWhenFastest()
    {
        Assert.Equal(100.0, FieldPercentile.Rank(60, new List<double> { 70, 80, 90 }), precision: 4);
    }

    [Fact]
    public void Rank_IsZeroWhenSlowest()
    {
        Assert.Equal(0.0, FieldPercentile.Rank(99, new List<double> { 70, 80, 90 }), precision: 4);
    }

    [Fact]
    public void Rank_IsOneHundredAgainstAnEmptyField()
    {
        // Nobody to compare against — the branch that used to be spelled `total > 1 ? … : 100.0`
        // at five separate call sites.
        Assert.Equal(100.0, FieldPercentile.Rank(80, new List<double>()));
    }

    [Fact]
    public void Rank_TreatsATieAsNotBeaten()
    {
        // The convention the five duplicated sites silently depended on: `>` is strict, so an
        // identical lap does not count as beaten. Two of those sites left the ranked driver's own
        // lap in the field and were correct *only* because of this.
        Assert.Equal(0.0, FieldPercentile.Rank(80, new List<double> { 80, 80 }), precision: 4);
    }

    [Fact]
    public void Rank_IgnoresHowTheDriverComparesToThemselves()
    {
        // The interface asks for the *other* drivers, so a driver whose personal lap beats their
        // own race lap cannot have that slower race lap counted as one more driver beaten. Before
        // this module, forgetting the exclusion inflated the rank by exactly one driver.
        var others = new List<double> { 90, 95 };
        var withOwnSlowerRaceLap = new List<double> { 90, 95, 85 };

        Assert.Equal(100.0, FieldPercentile.Rank(80, others), precision: 4);
        Assert.Equal(100.0, FieldPercentile.Rank(80, withOwnSlowerRaceLap), precision: 4);
        // …but the denominator grew, so the sample size a caller reports would be wrong:
        Assert.Equal(2, others.Count);
    }

    [Theory]
    // Lap times are lower-is-faster, so a smaller driverBest outranks the field.
    [InlineData(1, 100.0)] // a 1s lap beats the only other driver's 2s
    [InlineData(3, 0.0)] // a 3s lap loses to it
    public void Rank_HandlesASingleOtherDriver(double driverBest, double expected)
    {
        Assert.Equal(expected, FieldPercentile.Rank(driverBest, new List<double> { 2 }), precision: 4);
    }

    // ── MedianOfSorted ────────────────────────────────────────────────────────

    [Fact]
    public void MedianOfSorted_OddCount_TakesTheMiddle()
    {
        Assert.Equal(80.0, FieldPercentile.MedianOfSorted([70, 80, 95]), precision: 4);
    }

    [Fact]
    public void MedianOfSorted_EvenCount_AveragesTheMiddleTwo()
    {
        Assert.Equal(85.0, FieldPercentile.MedianOfSorted([70, 80, 90, 95]), precision: 4);
    }

    [Fact]
    public void MedianOfSorted_SingleLap_IsThatLap()
    {
        Assert.Equal(72.5, FieldPercentile.MedianOfSorted([72.5]), precision: 4);
    }

    [Fact]
    public void MedianOfSorted_EmptyList_Throws()
    {
        // Every caller guards for emptiness already; failing loudly beats indexing past the end.
        Assert.Throws<ArgumentOutOfRangeException>(() => FieldPercentile.MedianOfSorted([]));
    }
}
