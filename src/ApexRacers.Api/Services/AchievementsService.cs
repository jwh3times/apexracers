using ApexRacers.Api.Dtos;

namespace ApexRacers.Api.Services;

/// <summary>
/// The authenticated driver's trophy case — the awards/achievements they've earned, fetched on
/// demand from iRacing through <see cref="CachedIRacingClient"/> (6-hour TTL; awards change rarely).
/// The SDK auto-resolves the signed awards <c>data_url</c> into a typed array; we cache the
/// already-mapped DTOs (not the raw SDK type), newest first.
///
/// Note: per-series participation credits are intentionally not surfaced here —
/// <c>GetMemberParticipationCreditsAsync</c> has no cust_id parameter, so it returns the
/// authenticated service account's credits rather than the end user's (same limitation as
/// <c>GetMemberDivisionAsync</c> in standings). Showing them would be wrong-per-user data.
/// </summary>
public class AchievementsService(CachedIRacingClient cached)
{
    public async Task<AchievementsDto> GetAchievementsAsync(long custId, CancellationToken ct)
    {
        var awards = await cached.GetOrFetchAsync(
            IRacingCacheKeys.Awards(custId),
            async c => ((await c.GetDriverAwardsAsync((int)custId, ct)).Data ?? [])
                .Select(AchievementsMapper.MapAward)
                .OrderByDescending(a => a.AwardDate)
                .ThenBy(a => a.Name, StringComparer.Ordinal)
                .ToList(),
            ct);

        return new AchievementsDto(custId, awards.Count, awards);
    }
}
