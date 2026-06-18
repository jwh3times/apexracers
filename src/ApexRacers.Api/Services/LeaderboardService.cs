using System.Text;
using ApexRacers.Api.Dtos;
using Aydsko.iRacingData;

namespace ApexRacers.Api.Services;

/// <summary>
/// A category's global top-N leaderboard, fetched on demand through
/// <see cref="CachedIRacingClient"/> (24-hour TTL). The iRacing endpoint returns a large
/// CSV file (all drivers); we decode, parse, and cache only the parsed top-N rows.
/// </summary>
public class LeaderboardService(CachedIRacingClient cached)
{
    public const int TopN = 200;

    public Task<IReadOnlyList<GlobalLeaderboardEntryDto>> GetLeaderboardAsync(
        int categoryId, CancellationToken ct) =>
        cached.GetOrFetchAsync<IReadOnlyList<GlobalLeaderboardEntryDto>>(
            $"leaderboard:{categoryId}", TimeSpan.FromHours(24),
            async c =>
            {
                var file = await c.GetDriverStatisticsByCategoryCsvAsync(categoryId, ct);
                var csv = file.ContentBytes is null ? null : Encoding.UTF8.GetString(file.ContentBytes);
                return LeaderboardCsvParser.Parse(csv, categoryId, TopN);
            },
            ct);
}
