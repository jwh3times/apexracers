using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Data;
using Aydsko.iRacingData;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// The authenticated driver's recent official races, fetched on demand from iRacing
/// through <see cref="CachedIRacingClient"/> (10-minute TTL — new results land a few
/// times an hour at most). The recent-races payload carries only a car id, so car
/// display names are resolved from the local catalog.
///
/// The payload's track block is <c>{ track_id, track_name }</c> with no configuration, and the
/// name it carries belongs to the venue: tracks 249 and 253 are both "Nürburgring Nordschleife"
/// (Industriefahrten and Touristenfahrten). The identifier is kept so the row can say which
/// track it was, and the configuration is resolved from the local catalog the same way the car
/// name is. The name itself stays as iRacing gave it, so a track missing from the catalog still
/// reads correctly — it simply has no configuration to show.
/// </summary>
public class RaceHistoryService(CachedIRacingClient cached, AppDbContext db)
{
    public async Task<IReadOnlyList<RaceHistoryRowDto>> GetRecentRacesAsync(
        long custId, CancellationToken ct)
    {
        var rows = await cached.GetOrFetchAsync(
            IRacingCacheKeys.RecentRaces(custId),
            async c =>
            {
                var data = (await c.GetMemberRecentRacesAsync((int)custId, ct)).Data;
                return (data.Races ?? [])
                    .Select(r => new RecentRaceCacheRow(
                        r.SubsessionId,
                        r.SessionStartTime,
                        r.SeriesName,
                        r.Track?.TrackId ?? 0,
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

        var trackIds = rows.Select(r => r.TrackId).Distinct().ToList();
        var configNames = await db.Tracks
            .Where(t => trackIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.ConfigName, ct);

        return rows
            .OrderByDescending(r => r.SessionStartTime)
            .Select(r => new RaceHistoryRowDto(
                r.SubsessionId,
                r.SessionStartTime,
                r.SeriesName,
                r.TrackId,
                r.TrackName,
                configNames.TryGetValue(r.TrackId, out var config)
                    ? ConfigurationName.NullIfAbsent(config)
                    : null,
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

/// <summary>
/// SDK-decoupled cache row for <c>recent:{custId}</c> (public so the demo seeder can build it).
/// <c>TrackId</c> is 0 when the payload named no track, and in rows cached before the identifier
/// was kept — those age out within the 10-minute TTL, so callers treat 0 as "track unknown"
/// rather than as a track.
/// </summary>
public sealed record RecentRaceCacheRow(
    int SubsessionId, DateTimeOffset SessionStartTime, string SeriesName, int TrackId, string TrackName,
    int CarId, int StartPosition, int FinishPosition, int Incidents,
    int IRatingDelta, double SrDelta, int StrengthOfField, int Points);
