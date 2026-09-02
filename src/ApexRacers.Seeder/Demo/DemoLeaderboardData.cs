using ApexRacers.Api.Dtos;
using ApexRacers.Core;

namespace ApexRacers.Seeder.Demo;

/// <summary>Pure builder for a category's global iRating leaderboard. The demo driver appears
/// in the categories they hold (5/6/1) so LeaderboardsPage can highlight "your" row.</summary>
public static class DemoLeaderboardData
{
    private static readonly int[] RatedCategories = [5, 6, 1];
    private static readonly string[] Locations = ["United States", "United Kingdom", "Germany", "Australia", "Brazil"];

    public static List<GlobalLeaderboardEntryDto> Build(int categoryId)
    {
        const int count = 60;
        var entries = Enumerable.Range(0, count)
            .Select(i =>
            {
                var custId = 200_000L + categoryId * 1000 + i; // distinct synthetic pool, != driver pool
                return (CustId: custId, IRating: 9000 - i * 110);
            })
            .ToList();

        // Drop the demo driver into rated categories at a strong (but not #1) position.
        if (RatedCategories.Contains(categoryId))
            entries[3] = (DemoData.DriverCustId, entries[3].IRating);

        return entries
            .OrderByDescending(e => e.IRating)
            .Select((e, i) => new GlobalLeaderboardEntryDto(
                CategoryId: categoryId,
                Standing: i + 1,
                CustomerId: e.CustId,
                DriverName: e.CustId == DemoData.DriverCustId ? "Demo Driver" : $"Driver {e.CustId}",
                Location: Locations[(int)(e.CustId % Locations.Length)],
                Starts: 500 - i * 4,
                Wins: 80 - i,
                IRating: e.IRating,
                TtRating: e.IRating - 500,
                ChampPoints: 4000 - i * 40))
            .ToList();
    }
}
