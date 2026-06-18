using System.Globalization;
using ApexRacers.Api.Dtos;
using Aydsko.iRacingData;
using Aydsko.iRacingData.Member;

namespace ApexRacers.Api.Services;

/// <summary>
/// Reads a member's iRacing statistics on demand (through <see cref="CachedIRacingClient"/>
/// so repeated views stay within rate limits). Shared by the progression tracker and the
/// enriched driver profile. All fetches use a 6-hour TTL — these stats only move after a
/// race, so an hours-old view is acceptable and keeps us well under iRacing's limits.
/// </summary>
public class MemberStatsService(CachedIRacingClient cached)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);

    /// <summary>
    /// One progression card per license category the member holds: current iRating, safety
    /// rating, CPI, TT rating and license level, plus the iRating history for that category.
    /// </summary>
    public async Task<MemberProgressionDto> GetProgressionAsync(long custId, CancellationToken ct)
    {
        var profile = await cached.GetOrFetchAsync(
            $"profile:{custId}", Ttl,
            async c => (await c.GetMemberProfileAsync((int)custId, ct)).Data, ct);

        var categories = new List<CategoryProgressionDto>();
        foreach (var lic in profile.LicenseHistory ?? [])
        {
            var chart = await cached.GetOrFetchAsync(
                $"chart:{custId}:{lic.CategoryId}:{(int)MemberChartType.IRating}", Ttl,
                async c => (await c.GetMemberChartDataAsync(
                    (int)custId, lic.CategoryId, MemberChartType.IRating, ct)).Data, ct);

            var history = (chart.Points ?? [])
                .Select(p => new TimeSeriesPointDto(
                    p.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), p.Value))
                .ToList();

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

        return new MemberProgressionDto(custId, categories);
    }

    /// <summary>
    /// Enriched driver profile: identity (display name, country flair, member-since),
    /// colored license badges, lifetime career stats per category, this-year activity,
    /// and the recap's favorite car/track. Reuses the cached <c>profile:{custId}</c>
    /// entry for identity/licenses so visiting /progression first warms this view too.
    /// </summary>
    public async Task<DriverProfileDto> GetDriverProfileAsync(long custId, CancellationToken ct)
    {
        var profile = await cached.GetOrFetchAsync(
            $"profile:{custId}", Ttl,
            async c => (await c.GetMemberProfileAsync((int)custId, ct)).Data, ct);
        var career = await cached.GetOrFetchAsync(
            $"career:{custId}", Ttl,
            async c => (await c.GetCareerStatisticsAsync((int)custId, ct)).Data, ct);
        var summary = await cached.GetOrFetchAsync(
            $"summary:{custId}", Ttl,
            async c => (await c.GetMemberSummaryAsync((int)custId, ct)).Data, ct);
        var recap = await cached.GetOrFetchAsync(
            $"recap:{custId}", Ttl,
            async c => (await c.GetMemberRecapAsync((int)custId, null, null, ct)).Data, ct);

        var licenses = (profile.LicenseHistory ?? [])
            .Select(l => new LicenseBadgeDto(
                l.CategoryId, PrettifyCategory(l.Category), l.GroupName, l.LicenseLevel,
                l.SafetyRating, l.Irating, l.Color))
            .ToList();

        // Career "Category" is already a display name (e.g. "Sports Car") — no prettify.
        var careerCards = (career.Statistics ?? [])
            .Select(s => new CategoryCareerDto(
                s.CategoryId, s.Category, s.Starts, s.Wins, s.Top5, s.Poles,
                s.AvgStartPosition, s.AvgFinishPosition, s.Laps, s.LapsLed,
                s.WinPercentage, s.Top5Percentage))
            .ToList();

        var y = summary.YearStatistics;
        var thisYear = new ThisYearSummaryDto(
            y?.NumberOfOfficialSessions ?? 0, y?.NumberOfOfficialWins ?? 0,
            y?.NumberOfLeagueSessions ?? 0, y?.NumberOfLeagueWins ?? 0);

        var stats = recap.Statistics;
        var favCar = stats?.FavoriteCar is { } fc
            ? new FavoriteCarDto(fc.CarId, fc.CarName, fc.CarImageUrl?.ToString())
            : null;
        var favTrack = stats?.FavoriteTrack is { } ft
            ? new FavoriteTrackDto(ft.TrackId, ft.TrackName, ft.ConfigName, ft.TrackLogoUrl?.ToString())
            : null;

        var info = profile.Info;
        return new DriverProfileDto(
            custId,
            info?.DisplayName ?? string.Empty,
            info?.FlairName,
            info?.FlairShortName,
            info?.MemberSince,
            licenses,
            careerCards,
            thisYear,
            favCar,
            favTrack);
    }

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
