using Aydsko.iRacingData.Member;

namespace ApexRacers.Api.Services;

/// <summary>A cache key paired with the TTL that key is stored under.</summary>
/// <remarks>
/// Key and TTL travel together because they are one decision: how long <em>this</em> kind of
/// data stays fresh is a property of the key family, not of the call site. Passing them as two
/// parameters let a caller pick a key from one family and a TTL from another.
/// </remarks>
public readonly record struct CacheSpec(string Key, TimeSpan Ttl);

/// <summary>
/// The single authority for every <see cref="ExternalDataCache"/> key the iRacing read paths use.
///
/// These keys are an interface, not an implementation detail: the API services write and read
/// them, <c>DemoCacheSeeder</c> writes the demo surface under the exact same strings, and
/// <c>DemoSeedVerifier</c> asserts they exist. Before this module all three trees authored the
/// same interpolated string independently — with test literals as a fourth transcription — kept
/// in step by a doc-comment asking future editors to "keep the two in lockstep", which named only
/// two of the three authors and not the one that actually matters (the service that reads).
///
/// That arrangement had already cost a bug: the verifier derived expected <c>laps:</c> keys with
/// a different filter than the seeder wrote them with, and demanded a key that was never seeded.
/// It is also invisible to the test suite — nothing under Tests/Seeder references a reading
/// service, so every seeder test asserted a hardcoded literal and a typo in a service's key would
/// have left them all green while the demo page 503'd.
///
/// TTL guidance (mirrors the table in the project guide) lives here as code rather than prose.
/// </summary>
public static class IRacingCacheKeys
{
    private static readonly TimeSpan MemberTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan ReferenceTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan RecentRacesTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DriverSearchTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RaceGuideTtl = TimeSpan.FromSeconds(60);

    /// <summary>Shortest term a driver search is cached under; below this the search is refused.</summary>
    public const int MinDriverSearchLength = 2;

    // ── Member (6 h) ──────────────────────────────────────────────────────────

    public static CacheSpec Profile(long custId) => new($"profile:{custId}", MemberTtl);

    public static CacheSpec Career(long custId) => new($"career:{custId}", MemberTtl);

    public static CacheSpec Summary(long custId) => new($"summary:{custId}", MemberTtl);

    public static CacheSpec Recap(long custId) => new($"recap:{custId}", MemberTtl);

    public static CacheSpec Awards(long custId) => new($"awards:{custId}", MemberTtl);

    /// <summary>
    /// The chart key carries the chart type so a future non-iRating chart cannot collide with
    /// this one — hence the numeric suffix, which is <see cref="MemberChartType.IRating"/>.
    /// </summary>
    public static CacheSpec IRatingChart(long custId, int categoryId) =>
        new($"chart:{custId}:{categoryId}:{(int)MemberChartType.IRating}", MemberTtl);

    // ── Activity ──────────────────────────────────────────────────────────────

    public static CacheSpec RecentRaces(long custId) => new($"recent:{custId}", RecentRacesTtl);

    public static CacheSpec LapData(int subsessionId, long custId) =>
        new($"laps:{subsessionId}:{custId}", ReferenceTtl);

    // ── Reference data (24 h) ─────────────────────────────────────────────────

    // v2 separates ApexRacers' domain-specific Standing response field from legacy payloads
    // serialized with the ambiguous Rank field. Reusing those rows would deserialize Standing as 0.
    public static CacheSpec Leaderboard(int categoryId) =>
        new($"leaderboard:v2:{categoryId}", ReferenceTtl);

    public static CacheSpec Standings(int seasonId, int classId) =>
        new($"standings:v2:{seasonId}:{classId}", ReferenceTtl);

    public static CacheSpec TimeTrialStandings(int seasonId, int classId) =>
        new($"tt-standings:v2:{seasonId}:{classId}", ReferenceTtl);

    public static CacheSpec QualifyResults(int seasonId, int classId, int week) =>
        new($"qual:v2:{seasonId}:{classId}:{week}", ReferenceTtl);

    public static CacheSpec WorldRecord(int carId, int trackId) =>
        new($"wr:{carId}:{trackId}", ReferenceTtl);

    // ── Volatile ──────────────────────────────────────────────────────────────

    /// <summary>A single global key: the race guide is the same board for every caller.</summary>
    public static CacheSpec RaceGuide => new("race-guide", RaceGuideTtl);

    // ── Driver search ─────────────────────────────────────────────────────────

    /// <summary>
    /// Normalizes the caller's raw term and returns its spec, or <c>null</c> when the term is too
    /// short to search on.
    ///
    /// Owning the trim/lowercase here is the point: the rule used to live in
    /// <c>RivalService</c> as code and in the demo seed data as a doc-comment asking the author to
    /// hand-lowercase every dictionary key, with nothing enforcing that they matched.
    /// </summary>
    public static CacheSpec? DriverSearch(string rawTerm)
    {
        var normalized = (rawTerm ?? string.Empty).Trim();
        if (normalized.Length < MinDriverSearchLength) return null;
        return new CacheSpec($"driversearch:{normalized.ToLowerInvariant()}", DriverSearchTtl);
    }
}
