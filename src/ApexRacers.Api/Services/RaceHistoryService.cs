using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Aydsko.iRacingData;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// The authenticated driver's recent official races, fetched on demand from iRacing
/// through <see cref="CachedIRacingClient"/> (10-minute TTL — new results land a few
/// times an hour at most). The recent-races payload carries only a car id, so car
/// display names are resolved from the local catalog.
/// </summary>
public class RaceHistoryService(CachedIRacingClient cached, AppDbContext db)
{
    // SDK-decoupled cache row: exactly the fields we read from iRacing's Race, mapped before
    // caching so the cached JSON never depends on the Aydsko wire shape. Car names are resolved
    // from the local catalog afterwards (not part of the cached payload).
    private sealed record RecentRaceCacheRow(
        int SubsessionId, DateTimeOffset SessionStartTime, string SeriesName, string TrackName,
        int CarId, int StartPosition, int FinishPosition, int Incidents,
        int IRatingDelta, double SrDelta, int StrengthOfField, int Points);

    public async Task<IReadOnlyList<RaceHistoryRowDto>> GetRecentRacesAsync(
        long custId, CancellationToken ct)
    {
        var rows = await cached.GetOrFetchAsync(
            $"recent:{custId}", TimeSpan.FromMinutes(10),
            async c =>
            {
                var data = (await c.GetMemberRecentRacesAsync((int)custId, ct)).Data;
                return (data.Races ?? [])
                    .Select(r => new RecentRaceCacheRow(
                        r.SubsessionId,
                        r.SessionStartTime,
                        r.SeriesName,
                        r.Track?.TrackName ?? string.Empty,
                        r.CarId,
                        r.StartPosition,
                        r.FinishPosition,
                        r.Incidents,
                        r.NewiRating - r.OldiRating,
                        (r.NewSubLevel - r.OldSubLevel) / 100.0,
                        r.StrengthOfField,
                        r.Points))
                    .ToList();
            }, ct);

        var carIds = rows.Select(r => r.CarId).Distinct().ToList();
        var carNames = await db.Cars
            .Where(c => carIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return rows
            .OrderByDescending(r => r.SessionStartTime)
            .Select(r => new RaceHistoryRowDto(
                r.SubsessionId,
                r.SessionStartTime,
                r.SeriesName,
                r.TrackName,
                r.CarId,
                carNames.TryGetValue(r.CarId, out var name) ? name : $"Car {r.CarId}",
                r.StartPosition,
                r.FinishPosition,
                r.Incidents,
                r.IRatingDelta,
                r.SrDelta,
                r.StrengthOfField,
                r.Points))
            .ToList();
    }
}
