using ApexRacers.Core;
using Xunit;

namespace ApexRacers.Tests.Models;

/// <summary>
/// The arithmetic these cover was previously reachable only through a full DB fixture — the
/// percentile service tests document it in comments ("driverBest=80, beats 90+95 among others →
/// 2/4 = 50th percentile") precisely because it could not be asserted any other way.
///
/// Every case here reconstructs the Field as <c>otherDriversLaps + the ranked lap</c>, so the
/// denominators below are one larger than the list handed in.
/// </summary>
public class FieldPercentileTests
{
    // ── Rank ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Rank_SplitsTheTiedGroupAndCountsTheSubjectDriver()
    {
        // Field of 5: beats 90 and 95, ties with 80, loses to 70.
        // (2 slower + 0.5 × 2 tied) / 5 = 60.
        var others = new List<double> { 70, 80, 90, 95 };
        Assert.Equal(60.0, FieldPercentile.Rank(80, others), precision: 4);
    }

    [Fact]
    public void Rank_FastestInTheFieldFallsShortOfOneHundred()
    {
        // Field of 4, nobody faster: (3 + 0.5) / 4 = 87.5. The fastest Driver cannot be at the
        // 100th percentile of a population they belong to — that is the correction this formula
        // makes over counting strictly-slower Drivers against the count of *other* Drivers.
        Assert.Equal(87.5, FieldPercentile.Rank(60, new List<double> { 70, 80, 90 }), precision: 4);
    }

    [Fact]
    public void Rank_SlowestInTheFieldIsAboveZero()
    {
        // Field of 4, nobody slower: (0 + 0.5) / 4 = 12.5. Symmetric with the fastest case.
        Assert.Equal(12.5, FieldPercentile.Rank(99, new List<double> { 70, 80, 90 }), precision: 4);
    }

    [Fact]
    public void Rank_AloneInTheFieldIsItsMedian()
    {
        // A Field whose only member is the Subject Driver: (0 + 0.5 × 1) / 1 = 50. Reporting 100
        // here — the old behaviour — flattered a Driver who had beaten nobody at all.
        Assert.Equal(50.0, FieldPercentile.Rank(80, new List<double>()), precision: 4);
    }

    [Fact]
    public void Rank_IdenticalLapsRankEqually()
    {
        // Three Drivers on the same lap time: (0 + 0.5 × 3) / 3 = 50. Each is faster than half the
        // Field, which is the whole point of splitting the tie rather than discarding it.
        Assert.Equal(50.0, FieldPercentile.Rank(80, new List<double> { 80, 80 }), precision: 4);
    }

    [Fact]
    public void Rank_IgnoresHowTheDriverComparesToThemselves()
    {
        // The interface asks for the *other* Drivers, so a Driver whose Uploaded Lap beats their
        // own Race Best cannot have that slower Race Best counted as one more Driver beaten.
        var others = new List<double> { 90, 95 };
        var withOwnSlowerRaceLap = new List<double> { 90, 95, 85 };

        Assert.Equal(83.3333, FieldPercentile.Rank(80, others), precision: 4);
        // Leaving their own slower lap in inflates both the numerator and the Field size.
        Assert.NotEqual(
            FieldPercentile.Rank(80, others),
            FieldPercentile.Rank(80, withOwnSlowerRaceLap));
    }

    [Theory]
    // Lap times are lower-is-faster, so a smaller driverBest outranks the field.
    [InlineData(1, 75.0)] // a 1s lap beats the only other Driver's 2s → (1 + 0.5) / 2
    [InlineData(2, 50.0)] // an identical lap splits the Field → (0 + 0.5 × 2) / 2
    [InlineData(3, 25.0)] // a 3s lap loses to it → (0 + 0.5) / 2
    public void Rank_HandlesASingleOtherDriver(double driverBest, double expected)
    {
        Assert.Equal(expected, FieldPercentile.Rank(driverBest, new List<double> { 2 }), precision: 4);
    }

    // ── Position ──────────────────────────────────────────────────────────────

    [Fact]
    public void Position_CountsOnlyStrictlyFasterDrivers()
    {
        Assert.Equal(3, FieldPercentile.Position(90, new List<double> { 70, 80, 95 }));
    }

    [Fact]
    public void Position_TiedDriversShareThePosition()
    {
        // Nobody is strictly faster, so both Drivers on 80 are 1st.
        Assert.Equal(1, FieldPercentile.Position(80, new List<double> { 80, 95 }));
    }

    [Fact]
    public void Position_AloneInTheFieldIsFirst()
    {
        Assert.Equal(1, FieldPercentile.Position(80, new List<double>()));
    }

    // ── TopSharePercent ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 50)] // 1st of 2 → the top 50%, not the top 1%
    [InlineData(3, 100)] // 2nd of 2 → the whole Field
    public void TopSharePercent_IsAPlacementShareOfTheWholeField(double driverBest, int expected)
    {
        Assert.Equal(expected, FieldPercentile.TopSharePercent(driverBest, new List<double> { 2 }));
    }

    [Fact]
    public void TopSharePercent_RoundsUpToAWholePercent()
    {
        // 1st of 40 is the top 2.5%, shown as 3% — rounding down would claim a placement the
        // Driver does not hold.
        var others = Enumerable.Range(1, 39).Select(i => 80.0 + i).ToList();
        Assert.Equal(3, FieldPercentile.TopSharePercent(80, others));
    }

    [Fact]
    public void TopSharePercent_AloneInTheFieldIsTheWholeField()
    {
        Assert.Equal(100, FieldPercentile.TopSharePercent(80, new List<double>()));
    }

    // ── FieldSize ─────────────────────────────────────────────────────────────

    [Fact]
    public void FieldSize_CountsTheSubjectDriver()
    {
        Assert.Equal(4, FieldPercentile.FieldSize(new List<double> { 70, 80, 90 }));
        Assert.Equal(1, FieldPercentile.FieldSize(new List<double>()));
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
