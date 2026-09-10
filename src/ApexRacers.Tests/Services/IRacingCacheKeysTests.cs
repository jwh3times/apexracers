using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>
/// These assert the key <em>strings</em> deliberately, not just that two callers agree.
///
/// The keys are a persisted contract: rows already written under the old format sit in every
/// deployed database, and <c>purge_demo_data.sql</c> and the demo seeder both reason about them.
/// A test that only checked "the seeder and the service call the same method" would stay green
/// through a rename that silently orphaned every existing row.
/// </summary>
public class IRacingCacheKeysTests
{
    [Fact]
    public void MemberKeys_MatchTheirPersistedFormat()
    {
        Assert.Equal("profile:100001", IRacingCacheKeys.Profile(100001).Key);
        Assert.Equal("career:100001", IRacingCacheKeys.Career(100001).Key);
        Assert.Equal("summary:100001", IRacingCacheKeys.Summary(100001).Key);
        Assert.Equal("recap:100001", IRacingCacheKeys.Recap(100001).Key);
        Assert.Equal("awards:100001", IRacingCacheKeys.Awards(100001).Key);
        // 1 is MemberChartType.IRating; the suffix keeps a future chart type from colliding.
        Assert.Equal("chart:100001:5:1", IRacingCacheKeys.IRatingChart(100001, 5).Key);
    }

    [Fact]
    public void ActivityAndReferenceKeys_MatchTheirPersistedFormat()
    {
        Assert.Equal("recent:100001", IRacingCacheKeys.RecentRaces(100001).Key);
        Assert.Equal("laps:-11:100001", IRacingCacheKeys.LapData(-11, 100001).Key);
        Assert.Equal("leaderboard:v2:5", IRacingCacheKeys.Leaderboard(5).Key);
        Assert.Equal("standings:v2:6115:100", IRacingCacheKeys.Standings(6115, 100).Key);
        Assert.Equal("tt-standings:v2:6115:100", IRacingCacheKeys.TimeTrialStandings(6115, 100).Key);
        Assert.Equal("qual:v3:6115:100:3", IRacingCacheKeys.QualifyResults(6115, 100, 3).Key);
        Assert.Equal("wr:132:532", IRacingCacheKeys.WorldRecord(132, 532).Key);
        Assert.Equal("race-guide:v2", IRacingCacheKeys.RaceGuide.Key);
    }

    [Fact]
    public void StandingResponseKeys_DoNotReuseLegacyRankPayloads()
    {
        Assert.NotEqual("leaderboard:5", IRacingCacheKeys.Leaderboard(5).Key);
        Assert.NotEqual("standings:6115:100", IRacingCacheKeys.Standings(6115, 100).Key);
        Assert.NotEqual("tt-standings:6115:100", IRacingCacheKeys.TimeTrialStandings(6115, 100).Key);
        Assert.NotEqual("qual:6115:100:3", IRacingCacheKeys.QualifyResults(6115, 100, 3).Key);
    }

    [Fact]
    public void RaceWeekResponseKeys_DoNotReuseLegacyMemberPayloads()
    {
        Assert.NotEqual("qual:v2:6115:100:3", IRacingCacheKeys.QualifyResults(6115, 100, 3).Key);
        Assert.NotEqual("race-guide", IRacingCacheKeys.RaceGuide.Key);
    }

    [Fact]
    public void Ttls_MatchTheDocumentedGuidance()
    {
        Assert.Equal(TimeSpan.FromHours(6), IRacingCacheKeys.Profile(1).Ttl);
        Assert.Equal(TimeSpan.FromMinutes(10), IRacingCacheKeys.RecentRaces(1).Ttl);
        Assert.Equal(TimeSpan.FromMinutes(30), IRacingCacheKeys.DriverSearch("abc")!.Value.Ttl);
        Assert.Equal(TimeSpan.FromHours(24), IRacingCacheKeys.WorldRecord(1, 1).Ttl);
        Assert.Equal(TimeSpan.FromSeconds(60), IRacingCacheKeys.RaceGuide.Ttl);
    }

    // ── Driver search normalization ───────────────────────────────────────────
    //
    // This rule used to live as code in RivalService and as a doc-comment in the demo seed data
    // asking the author to hand-lowercase every dictionary key, with nothing enforcing a match.

    [Theory]
    [InlineData("jerry", "driversearch:jerry")]
    [InlineData("Jerry", "driversearch:jerry")]
    [InlineData("  JERRY  ", "driversearch:jerry")]
    [InlineData("Van Der Berg", "driversearch:van der berg")]
    public void DriverSearch_TrimsAndLowercases(string raw, string expected)
    {
        Assert.Equal(expected, IRacingCacheKeys.DriverSearch(raw)!.Value.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    [InlineData("  b  ")]
    public void DriverSearch_ReturnsNullBelowTheMinimumLength(string raw)
    {
        // Null rather than a key, so a caller cannot accidentally cache a search it should refuse.
        Assert.Null(IRacingCacheKeys.DriverSearch(raw));
    }

    // ── Driver search upper bound (GHSA-jv96-89xc-98h2) ──────────────────────

    [Fact]
    public void TermIsTooLong_AcceptsExactlyTheMaximumAndRejectsOneMore()
    {
        Assert.False(IRacingCacheKeys.TermIsTooLong(
            new string('a', IRacingCacheKeys.MaxDriverSearchLength)));
        Assert.True(IRacingCacheKeys.TermIsTooLong(
            new string('a', IRacingCacheKeys.MaxDriverSearchLength + 1)));
    }

    [Fact]
    public void TermIsTooLong_MeasuresAfterTrimming()
    {
        // Surrounding whitespace is stripped before the key is built, so it must not count
        // toward the bound either — otherwise a padded but legitimate term would be refused.
        var padded = "  " + new string('a', IRacingCacheKeys.MaxDriverSearchLength) + "  ";

        Assert.False(IRacingCacheKeys.TermIsTooLong(padded));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("jerry")]
    public void TermIsTooLong_IsFalseForAbsentAndOrdinaryTerms(string? raw)
    {
        Assert.False(IRacingCacheKeys.TermIsTooLong(raw));
    }

    [Fact]
    public void ATermAtTheMaximumStillFitsTheCacheKeyColumn()
    {
        // The bound exists to keep the composed key storable. Asserting the bound alone would
        // not catch a future prefix change making the longest allowed term overflow anyway.
        var longest = new string('a', IRacingCacheKeys.MaxDriverSearchLength);

        var key = IRacingCacheKeys.DriverSearch(longest)!.Value.Key;

        Assert.True(key.Length <= ExternalDataCache.CacheKeyMaxLength);
    }

    [Fact]
    public void DriverSearch_MeasuresLengthAfterTrimming()
    {
        Assert.Null(IRacingCacheKeys.DriverSearch("  a  "));
        Assert.NotNull(IRacingCacheKeys.DriverSearch("  ab  "));
    }
}
