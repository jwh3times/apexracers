using System.Globalization;
using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;

namespace ApexRacers.Seeder.Demo;

/// <summary>One demo license category: id, slug (PrettifyCategory-friendly), license group/level/color.</summary>
public sealed record DemoCategory(int Id, string Slug, string GroupName, int LicenseLevel, string Color);

/// <summary>
/// Pure builders for the member cache snapshots/DTOs (profile / career / chart / summary / recap).
/// Deterministic per custId so re-running the seeder is stable and the builders are unit-testable.
/// Builds the REAL cached types (no mirroring), so payloads are byte-compatible by construction.
/// </summary>
public static class DemoMemberData
{
    // Sports Car, Formula Car, Oval — the categories the demo driver + rival are rated in.
    public static readonly IReadOnlyList<DemoCategory> Categories =
    [
        new(5, "sports_car",  "Class A", 20, "#fc8a27"),
        new(6, "formula_car", "Class B", 16, "#feec04"),
        new(1, "oval",        "Class C", 12, "#53b14f"),
    ];

    private static int BaseIrating(long custId, int categoryId)
    {
        // Demo driver a touch stronger than the rival; varies a little by category.
        var driverBonus = custId == 100_001 ? 350 : 0;
        return 1800 + driverBonus + categoryId * 40;
    }

    public static ProfileSnapshot BuildProfile(long custId)
    {
        var licenses = Categories
            .Select(c => new LicenseSnapshot(
                c.Id, c.Slug,
                Irating: BaseIrating(custId, c.Id),
                SafetyRating: 3.5,
                Cpi: 80.0,
                LicenseLevel: c.LicenseLevel,
                GroupName: c.GroupName,
                TtRating: 1500 + c.Id * 10,
                Color: c.Color))
            .ToList();

        return new ProfileSnapshot(
            DisplayName: custId == 100_001 ? "Demo Driver" : "Rival Racer",
            FlairName: "United States",
            FlairShortName: "USA",
            MemberSince: "2019-03-01",
            Licenses: licenses);
    }

    public static List<CategoryCareerDto> BuildCareer(long custId) =>
        Categories
            .Select(c => new CategoryCareerDto(
                c.Id, MemberStatsService.PrettifyCategory(c.Slug),
                Starts: 240, Wins: 18, Top5: 96, Poles: 12,
                AvgStartPosition: 8, AvgFinishPosition: 6,
                Laps: 7400, LapsLed: 320,
                WinPercentage: 7.5, Top5Percentage: 40.0))
            .ToList();

    public static List<TimeSeriesPointDto> BuildChart(long custId, int categoryId)
    {
        var start = BaseIrating(custId, categoryId) - 700;
        return Enumerable.Range(0, 12)
            .Select(i => new TimeSeriesPointDto(
                DemoCache.RefDate.AddDays(-7 * (11 - i)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                start + i * 60))
            .ToList();
    }

    public static ThisYearSummaryDto BuildSummary(long custId) =>
        new(OfficialSessions: 84, OfficialWins: 9, LeagueSessions: 12, LeagueWins: 3);

    public static RecapSnapshot BuildRecap(long custId) =>
        new(
            new FavoriteCarDto(132, "BMW M4 GT3", null),
            new FavoriteTrackDto(47, "Laguna Seca", "", null));
}
