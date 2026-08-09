using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Aydsko.iRacingData;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// "Race now" board: official sessions starting within the next ~3 hours (plus any
/// still in progress), across all active series. The race guide is fetched through
/// <see cref="CachedIRacingClient"/> with a short 60-second TTL (it changes minute to
/// minute); the time-window filter and series-name join are applied per request so a
/// cached payload still reflects the current clock.
/// </summary>
public class RaceGuideService(CachedIRacingClient cached, AppDbContext db)
{
    private static readonly TimeSpan Horizon = TimeSpan.FromHours(3);

    public async Task<IReadOnlyList<RaceGuideEntryDto>> GetGuideAsync(CancellationToken ct)
    {
        var sessions = await cached.GetOrFetchAsync(
            IRacingCacheKeys.RaceGuide,
            async c =>
            {
                var raw = (await c.GetRaceGuideAsync(DateTimeOffset.UtcNow, true, ct)).Data.Sessions ?? [];
                return raw
                    .Select(s => new RaceGuideCacheRow(
                        s.SeriesId, Utc(s.StartTime), Utc(s.EndTime), s.EntryCount, s.RaceWeekNumber))
                    .ToList();
            },
            ct);

        var now = DateTimeOffset.UtcNow;
        var horizon = now + Horizon;

        var upcoming = sessions
            .Where(s => s.End > now && s.Start <= horizon)
            .OrderBy(s => s.Start)
            .ToList();

        var seriesIds = upcoming.Select(s => s.SeriesId).Distinct().ToList();
        var names = await db.Series
            .Where(x => seriesIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        return upcoming
            .Select(s => new RaceGuideEntryDto(
                s.SeriesId,
                names.TryGetValue(s.SeriesId, out var n) ? n : $"Series {s.SeriesId}",
                s.Start,
                s.End,
                s.EntryCount,
                s.RaceWeekNumber))
            .ToList();
    }

    // Race-guide times are UTC; normalize the DateTime (Kind may be Unspecified) before
    // projecting to DateTimeOffset so callers get a correct instant.
    private static DateTimeOffset Utc(DateTime dt) =>
        new(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
}

/// <summary>SDK-decoupled cache row for <c>race-guide</c> (public so the demo seeder can build it).</summary>
public sealed record RaceGuideCacheRow(
    int SeriesId, DateTimeOffset Start, DateTimeOffset End, int EntryCount, int RaceWeekNumber);
