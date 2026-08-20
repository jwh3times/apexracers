using ApexRacers.Ingestion;
using Xunit;

namespace ApexRacers.Tests.Ingestion;

public class SubsessionIndexerTests
{
    // ── ComputeSearchRangeBegin ───────────────────────────────────────────────

    [Fact]
    public void ComputeSearchRangeBegin_Null_ReturnsNull()
    {
        Assert.Null(SubsessionIndexer.ComputeSearchRangeBegin(null));
    }

    [Fact]
    public void ComputeSearchRangeBegin_Value_SubtractsOneHourBufferInUtc()
    {
        var last = new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.FromHours(5));

        var result = SubsessionIndexer.ComputeSearchRangeBegin(last);

        // 12:00 +05:00 == 07:00 UTC; minus the 1-hour buffer == 06:00 UTC.
        Assert.Equal(new DateTime(2026, 6, 16, 6, 0, 0, DateTimeKind.Utc), result);
    }

    // ── ComputeNewSubsessionIds ───────────────────────────────────────────────

    [Fact]
    public void ComputeNewSubsessionIds_ExcludesExistingAndDeduplicates()
    {
        var candidates = new[] { 1, 2, 2, 3, 4 };
        var existing   = new[] { 2, 4 };

        var result = SubsessionIndexer.ComputeNewSubsessionIds(candidates, existing);

        Assert.Equal(new[] { 1, 3 }, result);
    }

    [Fact]
    public void ComputeNewSubsessionIds_AllExisting_ReturnsEmpty()
    {
        var result = SubsessionIndexer.ComputeNewSubsessionIds(new[] { 1, 2 }, new[] { 1, 2 });
        Assert.Empty(result);
    }

    // ── ResolveSeriesName ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveSeriesName_MissingName_UsesFallback(string? name)
    {
        Assert.Equal("Series 42", SubsessionIndexer.ResolveSeriesName(name, 42));
    }

    [Fact]
    public void ResolveSeriesName_PresentName_UsesIt()
    {
        Assert.Equal("GT3 Sprint", SubsessionIndexer.ResolveSeriesName("GT3 Sprint", 42));
    }

    // ── ResolveSplitPosition ──────────────────────────────────────────────────

    private static SubsessionIndexer.SplitEntry Split(int subsessionId, int sof) =>
        new(subsessionId, sof);

    [Fact]
    public void ResolveSplitPosition_NullSplits_IsUnknown()
    {
        Assert.Null(SubsessionIndexer.ResolveSplitPosition(null, 100));
    }

    [Fact]
    public void ResolveSplitPosition_EmptySplits_IsUnknown()
    {
        Assert.Null(SubsessionIndexer.ResolveSplitPosition([], 100));
    }

    [Fact]
    public void ResolveSplitPosition_SubsessionAbsentFromItsOwnSplits_IsUnknown()
    {
        var splits = new[] { Split(10, 2500), Split(20, 1200) };

        Assert.Null(SubsessionIndexer.ResolveSplitPosition(splits, 99));
    }

    [Fact]
    public void ResolveSplitPosition_StrongestSplit_IsIndexZero()
    {
        var splits = new[] { Split(10, 2509), Split(20, 1266) };

        Assert.Equal(new SubsessionIndexer.SplitPosition(0, 2),
            SubsessionIndexer.ResolveSplitPosition(splits, 10));
    }

    [Fact]
    public void ResolveSplitPosition_SoleSplit_IsIndexZeroOfOne()
    {
        var splits = new[] { Split(10, 1800) };

        Assert.Equal(new SubsessionIndexer.SplitPosition(0, 1),
            SubsessionIndexer.ResolveSplitPosition(splits, 10));
    }

    [Fact]
    public void ResolveSplitPosition_RanksBySofNotByArrayOrder()
    {
        // Deliberately ascending by SOF: trusting the array order would call 10 the strongest.
        var splits = new[] { Split(10, 900), Split(20, 1800), Split(30, 3200) };

        Assert.Equal(0, SubsessionIndexer.ResolveSplitPosition(splits, 30)!.Value.Index);
        Assert.Equal(1, SubsessionIndexer.ResolveSplitPosition(splits, 20)!.Value.Index);
        Assert.Equal(2, SubsessionIndexer.ResolveSplitPosition(splits, 10)!.Value.Index);
    }

    [Fact]
    public void ResolveSplitPosition_EqualSof_BreaksTieBySubsessionIdAscending()
    {
        var splits = new[] { Split(30, 1500), Split(10, 1500), Split(20, 1500) };

        Assert.Equal(0, SubsessionIndexer.ResolveSplitPosition(splits, 10)!.Value.Index);
        Assert.Equal(1, SubsessionIndexer.ResolveSplitPosition(splits, 20)!.Value.Index);
        Assert.Equal(2, SubsessionIndexer.ResolveSplitPosition(splits, 30)!.Value.Index);
    }

    [Fact]
    public void ResolveSplitPosition_EveryIndexIsDistinct_SoNoSplitShadowsAnother()
    {
        var splits = new[]
        {
            Split(40, 1500), Split(10, 2400), Split(30, 1500), Split(20, 800),
        };

        var indexes = splits
            .Select(s => SubsessionIndexer.ResolveSplitPosition(splits, s.SubsessionId)!.Value.Index)
            .ToList();

        Assert.Equal([0, 1, 2, 3], indexes.Order());
        Assert.All(splits, s =>
            Assert.Equal(4, SubsessionIndexer.ResolveSplitPosition(splits, s.SubsessionId)!.Value.Count));
    }

    // ── ShouldSkipResult ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, 12345L, true)]   // AI driver
    [InlineData(false, null, true)]    // team entry (no customer id)
    [InlineData(true, null, true)]     // both
    [InlineData(false, 12345L, false)] // real driver — index it
    public void ShouldSkipResult_AppliesAiAndTeamRules(bool isAi, long? customerId, bool expected)
    {
        Assert.Equal(expected, SubsessionIndexer.ShouldSkipResult(isAi, customerId));
    }

    // ── LapSecondsOrSentinel ──────────────────────────────────────────────────

    [Fact]
    public void LapSecondsOrSentinel_Null_ReturnsSentinel()
    {
        Assert.Equal(SubsessionIndexer.NoLapSentinel, SubsessionIndexer.LapSecondsOrSentinel(null));
    }

    [Fact]
    public void LapSecondsOrSentinel_Value_ReturnsTotalSeconds()
    {
        var lap = TimeSpan.FromSeconds(92.345);
        Assert.Equal(92.345, SubsessionIndexer.LapSecondsOrSentinel(lap), tolerance: 0.0005);
    }

    // ── EventLapSecondsOrSentinel ─────────────────────────────────────────────

    [Fact]
    public void EventLapSecondsOrSentinel_Positive_ReturnsTotalSeconds()
    {
        Assert.Equal(
            66.295,
            SubsessionIndexer.EventLapSecondsOrSentinel(TimeSpan.FromSeconds(66.295)),
            tolerance: 0.0005);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.0001)]
    [InlineData(-1)]
    public void EventLapSecondsOrSentinel_NonPositive_ReturnsSentinel(double seconds)
    {
        Assert.Equal(
            SubsessionIndexer.NoLapSentinel,
            SubsessionIndexer.EventLapSecondsOrSentinel(TimeSpan.FromSeconds(seconds)));
    }
}
