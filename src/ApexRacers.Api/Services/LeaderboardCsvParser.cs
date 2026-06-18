using System.Globalization;
using ApexRacers.Api.Dtos;

namespace ApexRacers.Api.Services;

/// <summary>
/// Parses iRacing's driver-stats-by-category CSV (header row + comma-separated rows,
/// already sorted by iRating descending) into ranked leaderboard entries. Pure +
/// static so it can be unit-tested directly. Rows whose column count doesn't match the
/// header are skipped (defensive against the rare comma-in-name row).
/// </summary>
public static class LeaderboardCsvParser
{
    // Column indices in the driver_stats_by_category CSV.
    private const int Driver = 0;
    private const int CustId = 1;
    private const int Location = 2;
    private const int Starts = 4;
    private const int Wins = 5;
    private const int IRating = 14;
    private const int TtRating = 15;
    private const int ChampPoints = 17;
    private const int ColumnCount = 18;

    public static IReadOnlyList<GlobalLeaderboardEntryDto> Parse(string? csv, int categoryId, int topN)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return [];

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var entries = new List<GlobalLeaderboardEntryDto>();
        var rank = 0;

        // Skip the header row (index 0).
        for (var i = 1; i < lines.Length && entries.Count < topN; i++)
        {
            var f = lines[i].TrimEnd('\r').Split(',');
            if (f.Length != ColumnCount)
                continue;

            rank++;
            entries.Add(new GlobalLeaderboardEntryDto(
                categoryId,
                rank,
                ParseLong(f[CustId]),
                f[Driver],
                f[Location],
                ParseInt(f[Starts]),
                ParseInt(f[Wins]),
                ParseInt(f[IRating]),
                ParseInt(f[TtRating]),
                ParseInt(f[ChampPoints])));
        }

        return entries;
    }

    private static int ParseInt(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static long ParseLong(string s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
