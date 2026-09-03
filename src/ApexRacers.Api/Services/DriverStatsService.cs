using System.Globalization;
using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using Aydsko.iRacingData;
using Aydsko.iRacingData.Member;

namespace ApexRacers.Api.Services;

/// <summary>
/// Reads a Subject Driver's iRacing statistics on demand (through <see cref="CachedIRacingClient"/>
/// so repeated views stay within rate limits). Shared by the progression tracker and the
/// enriched driver profile. All fetches use a 6-hour TTL — these stats only move after a
/// race, so an hours-old view is acceptable and keeps us well under iRacing's limits.
///
/// Each cache entry stores a mapped, SDK-decoupled snapshot/DTO (never a raw Aydsko type),
/// so the cached JSON doesn't depend on the SDK wire shape. The <c>profile:{custId}</c>,
/// <c>career:{custId}</c> and <c>chart:…</c> entries are shared across all three read methods.
/// Aydsko's <c>MemberSummary</c> and <c>MemberProfileInfo</c> names remain unchanged where the
/// SDK types mirror iRacing's <c>member_*</c> response shapes; they are upstream language.
/// </summary>
public class DriverStatsService(CachedIRacingClient cached)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);

    /// <summary>
    /// One progression card per license category the Subject Driver holds: current iRating, safety
    /// rating, CPI, TT rating and license level, plus the iRating history for that category.
    /// </summary>
    public async Task<DriverProgressionDto> GetProgressionAsync(
        long subjectDriverCustId, CancellationToken ct = default)
    {
        var profile = await GetProfileAsync(subjectDriverCustId, ct);

        var categories = new List<CategoryProgressionDto>();
        foreach (var lic in profile.Licenses)
        {
            var history = await GetIRatingHistoryAsync(subjectDriverCustId, lic.CategoryId, ct);
            categories.Add(new CategoryProgressionDto(
                lic.CategoryId,
                PrettifyCategory(lic.Category),
                lic.Irating,
                lic.SafetyRating,
                lic.Cpi,
                lic.LicenseLevel,
                lic.GroupName,
                lic.TtRating,
                lic.Color,
                history));
        }

        return new DriverProgressionDto(subjectDriverCustId, categories);
    }

    /// <summary>
    /// Enriched driver profile: identity (display name, country flair, member-since),
    /// colored license badges, lifetime career stats per category, this-year activity,
    /// and the recap's favorite car/track. Reuses the cached <c>profile:{custId}</c>
    /// entry for identity/licenses so visiting /progression first warms this view too.
    /// </summary>
    public async Task<DriverProfileDto> GetDriverProfileAsync(
        long subjectDriverCustId, CancellationToken ct = default)
    {
        var profile = await GetProfileAsync(subjectDriverCustId, ct);
        var career = await GetCareerAsync(subjectDriverCustId, ct);

        var thisYear = await cached.GetOrFetchAsync(
            IRacingCacheKeys.Summary(subjectDriverCustId),
            async c =>
            {
                var y = (await c.GetMemberSummaryAsync((int)subjectDriverCustId, ct)).Data.YearStatistics;
                return new ThisYearSummaryDto(
                    y?.NumberOfOfficialSessions ?? 0, y?.NumberOfOfficialWins ?? 0,
                    y?.NumberOfLeagueSessions ?? 0, y?.NumberOfLeagueWins ?? 0);
            }, ct);

        var recap = await cached.GetOrFetchAsync(
            IRacingCacheKeys.Recap(subjectDriverCustId),
            async c =>
            {
                var stats = (await c.GetMemberRecapAsync(
                    (int)subjectDriverCustId, null, null, ct)).Data.Statistics;
                var favCar = stats?.FavoriteCar is { } fc
                    ? new FavoriteCarDto(fc.CarId, fc.CarName, fc.CarImageUrl?.ToString())
                    : null;
                var favTrack = stats?.FavoriteTrack is { } ft
                    ? new FavoriteTrackDto(
                        ft.TrackId,
                        ft.TrackName,
                        ConfigurationName.NullIfAbsent(ft.ConfigName),
                        ft.TrackLogoUrl?.ToString())
                    : null;
                return new RecapSnapshot(favCar, favTrack);
            }, ct);

        // Normalize again after the cache boundary so a short-lived entry written by an older
        // build cannot leak its upstream N/A spelling while it waits for the six-hour TTL.
        var favoriteTrack = recap.FavoriteTrack is { } cachedTrack
            ? cachedTrack with { ConfigName = ConfigurationName.NullIfAbsent(cachedTrack.ConfigName) }
            : null;

        return new DriverProfileDto(
            subjectDriverCustId,
            profile.DisplayName,
            profile.FlairName,
            profile.FlairShortName,
            profile.MemberSince,
            MapLicenses(profile),
            career,
            thisYear,
            recap.FavoriteCar,
            favoriteTrack);
    }

    /// <summary>
    /// One side of a head-to-head comparison: identity, license badges, lifetime career stats,
    /// and per-category iRating history. Lighter than <see cref="GetDriverProfileAsync"/> (no
    /// summary/recap) but reuses the same cache entries, so the two views warm each other.
    /// </summary>
    public async Task<ComparisonSideDto> GetComparisonSideAsync(
        long subjectDriverCustId, CancellationToken ct = default)
    {
        var profile = await GetProfileAsync(subjectDriverCustId, ct);
        var career = await GetCareerAsync(subjectDriverCustId, ct);

        var history = new List<CategoryHistoryDto>();
        foreach (var lic in profile.Licenses)
        {
            history.Add(new CategoryHistoryDto(
                lic.CategoryId, PrettifyCategory(lic.Category),
                await GetIRatingHistoryAsync(subjectDriverCustId, lic.CategoryId, ct)));
        }

        return new ComparisonSideDto(
            subjectDriverCustId,
            profile.DisplayName,
            profile.FlairName,
            profile.FlairShortName,
            profile.MemberSince,
            MapLicenses(profile),
            career,
            history);
    }

    // ── Shared cached fetches (one entry, many consumers) ──────────────────────

    private Task<ProfileSnapshot> GetProfileAsync(long subjectDriverCustId, CancellationToken ct) =>
        cached.GetOrFetchAsync(
            IRacingCacheKeys.Profile(subjectDriverCustId),
            async c =>
            {
                var p = (await c.GetMemberProfileAsync((int)subjectDriverCustId, ct)).Data;
                var info = p.Info;
                return new ProfileSnapshot(
                    info?.DisplayName ?? string.Empty, info?.FlairName, info?.FlairShortName,
                    info?.MemberSince,
                    (p.LicenseHistory ?? [])
                        .Select(l => new LicenseSnapshot(
                            l.CategoryId, l.Category, l.Irating, l.SafetyRating, l.Cpi,
                            l.LicenseLevel, l.GroupName, l.TtRating, l.Color))
                        .ToList());
            }, ct);

    private Task<List<CategoryCareerDto>> GetCareerAsync(
        long subjectDriverCustId, CancellationToken ct) =>
        cached.GetOrFetchAsync(
            IRacingCacheKeys.Career(subjectDriverCustId),
            // Career "Category" is already a display name (e.g. "Sports Car") — no prettify.
            async c => ((await c.GetCareerStatisticsAsync(
                    (int)subjectDriverCustId, ct)).Data.Statistics ?? [])
                .Select(s => new CategoryCareerDto(
                    s.CategoryId, s.Category, s.Starts, s.Wins, s.Top5, s.Poles,
                    s.AvgStartPosition, s.AvgFinishPosition, s.Laps, s.LapsLed,
                    s.WinPercentage, s.Top5Percentage))
                .ToList(), ct);

    private Task<List<TimeSeriesPointDto>> GetIRatingHistoryAsync(
        long subjectDriverCustId, int categoryId, CancellationToken ct) =>
        cached.GetOrFetchAsync(
            IRacingCacheKeys.IRatingChart(subjectDriverCustId, categoryId),
            async c => ((await c.GetMemberChartDataAsync(
                    (int)subjectDriverCustId, categoryId, MemberChartType.IRating, ct)).Data.Points ?? [])
                .Select(p => new TimeSeriesPointDto(
                    p.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), p.Value))
                .ToList(), ct);

    private static List<LicenseBadgeDto> MapLicenses(ProfileSnapshot profile) =>
        profile.Licenses
            .Select(l => new LicenseBadgeDto(
                l.CategoryId, PrettifyCategory(l.Category), l.GroupName, l.LicenseLevel,
                l.SafetyRating, l.Irating, l.Color))
            .ToList();

    /// <summary>
    /// iRacing exposes the category as a slug (e.g. "sports_car"); turn it into a display
    /// name ("Sports Car"). Pure + public so it can be unit-tested directly.
    /// </summary>
    public static string PrettifyCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return string.Empty;

        var words = category
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]);
        return string.Join(' ', words);
    }
}

/// <summary>SDK-decoupled cache snapshot for <c>profile:{custId}</c> (public so the demo seeder can build it).</summary>
public sealed record ProfileSnapshot(
    string DisplayName, string? FlairName, string? FlairShortName, string? MemberSince,
    IReadOnlyList<LicenseSnapshot> Licenses);

public sealed record LicenseSnapshot(
    int CategoryId, string? Category, int Irating, double SafetyRating, double Cpi,
    int LicenseLevel, string GroupName, int TtRating, string Color);

/// <summary>SDK-decoupled cache snapshot for <c>recap:{custId}</c>.</summary>
public sealed record RecapSnapshot(FavoriteCarDto? FavoriteCar, FavoriteTrackDto? FavoriteTrack);
