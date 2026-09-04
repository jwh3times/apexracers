using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// "Which season and week does this series id mean right now?" — asked by seven services and,
/// before this module, answered by ten hand-written copies of the same predicate.
///
/// <para>The answer is the <b>Current Season</b>, and the rule for choosing it lives in
/// <see cref="SeasonCalendar.CurrentSeasonId"/> — read that first, since it is the part that was
/// wrong. This module is only the data access around it: fetch each season's first race week start
/// date, hand them to the rule, and expose the chosen id. Nothing here re-decides the rule.</para>
///
/// <para>Resolving a season is a round trip, so the week helper is split in two: callers await
/// <see cref="CurrentSeasonIdAsync"/> (or <see cref="CurrentSeasonOrThrowAsync"/>) once, then
/// compose <see cref="InSeason"/> for the projection they actually want. That keeps the original
/// point of these being <see cref="IQueryable{T}"/> composables rather than methods returning a
/// fixed record: the duplication was in the filter, not the shape — each caller projects different
/// columns (one wants only the id, another track metadata, another the series name and category),
/// and a shared record would force every one of them to fetch the union and throw away the
/// projection pushdown.</para>
/// </summary>
public static class SeasonQueries
{
    /// <summary>One Race Week by its Index in an already-resolved season.</summary>
    public static IQueryable<Week> InSeason(this IQueryable<Week> weeks, int seasonId, int raceWeekIndex) =>
        weeks.Where(w => w.SeasonId == seasonId && w.RaceWeekIndex == raceWeekIndex);

    /// <summary>
    /// The series' current season id, or <c>null</c> when the series has no season to show.
    /// </summary>
    /// <param name="today">Overridable only so tests can sit on a changeover boundary.</param>
    public static async Task<int?> CurrentSeasonIdAsync(
        this AppDbContext db, int seriesId, CancellationToken ct, DateOnly? today = null)
    {
        var byId = await db.CurrentSeasonIdsAsync([seriesId], ct, today);
        return byId.TryGetValue(seriesId, out var seasonId) ? seasonId : null;
    }

    /// <summary>
    /// The current season for each of several series, keyed by series id. Series with no season to
    /// show are absent from the result rather than mapped to null.
    ///
    /// <para>Two queries instead of one join: the season rows carry the tiebreakers, and a separate
    /// grouped <c>MIN(StartDate)</c> carries each season's first race week. Both are a handful of
    /// rows per series, and keeping the aggregate in its own query avoids a correlated subquery in
    /// a projection — the shape that translates on one provider and not another.</para>
    /// </summary>
    public static async Task<Dictionary<int, int>> CurrentSeasonIdsAsync(
        this AppDbContext db, IReadOnlyCollection<int> seriesIds, CancellationToken ct,
        DateOnly? today = null)
    {
        if (seriesIds.Count == 0) return [];

        var ids = seriesIds as IList<int> ?? seriesIds.ToList();

        var seasons = await db.Seasons
            .Where(s => ids.Contains(s.SeriesId))
            .Select(s => new { s.Id, s.SeriesId, s.Year, s.Quarter, s.Active })
            .ToListAsync(ct);

        if (seasons.Count == 0) return [];

        var firstWeekStarts = await db.Weeks
            .Where(w => ids.Contains(w.Season.SeriesId))
            .GroupBy(w => w.SeasonId)
            .Select(g => new { SeasonId = g.Key, FirstStart = g.Min(w => w.StartDate) })
            .ToListAsync(ct);

        var startBySeason = firstWeekStarts.ToDictionary(x => x.SeasonId, x => x.FirstStart);
        var onDate = today ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var resolved = new Dictionary<int, int>();
        foreach (var group in seasons.GroupBy(s => s.SeriesId))
        {
            var current = SeasonCalendar.CurrentSeasonId(
                group.Select(s => new SeasonStart(
                    s.Id,
                    startBySeason.TryGetValue(s.Id, out var start) ? start : null,
                    s.Year,
                    s.Quarter,
                    s.Active)),
                onDate);

            if (current is { } seasonId) resolved[group.Key] = seasonId;
        }

        return resolved;
    }

    /// <summary>
    /// The series' current season, or a <see cref="KeyNotFoundException"/> the exception middleware
    /// maps to a 404. One wording, one place — it was previously duplicated verbatim three times.
    /// </summary>
    public static async Task<Season> CurrentSeasonOrThrowAsync(
        this AppDbContext db, int seriesId, CancellationToken ct, DateOnly? today = null)
    {
        var seasonId = await db.CurrentSeasonIdAsync(seriesId, ct, today);

        return (seasonId is null
                ? null
                : await db.Seasons.FirstOrDefaultAsync(s => s.Id == seasonId.Value, ct))
            ?? throw new KeyNotFoundException($"No current season for series {seriesId}.");
    }

    /// <summary>
    /// The time span each Race Week of a season covers, keyed by Race Week Index.
    ///
    /// <para>The whole season is loaded rather than the one Week a caller asked about, because a
    /// Week that reported no end time is bounded by the <em>next</em> Week's start — which cannot
    /// be known from that Week's own row. It is at most a few dozen rows.</para>
    /// </summary>
    public static async Task<Dictionary<int, RaceWeekWindow>> RaceWeekWindowsAsync(
        this AppDbContext db, int seasonId, CancellationToken ct)
    {
        var weeks = await db.Weeks
            .Where(w => w.SeasonId == seasonId)
            .Select(w => new { w.RaceWeekIndex, w.StartDate, w.EndTime })
            .ToListAsync(ct);

        return RaceWeekWindow
            .ForSeason(weeks.Select(w => (w.RaceWeekIndex, w.StartDate, w.EndTime)))
            .ToDictionary(x => x.RaceWeekIndex, x => x.Window);
    }

    /// <summary>
    /// The time span one Race Week covers, or <c>null</c> when the season has no such Week.
    /// </summary>
    public static async Task<RaceWeekWindow?> RaceWeekWindowAsync(
        this AppDbContext db, int seasonId, int raceWeekIndex, CancellationToken ct)
    {
        var windows = await db.RaceWeekWindowsAsync(seasonId, ct);
        return windows.TryGetValue(raceWeekIndex, out var window) ? window : null;
    }

    /// <summary>The series' display name, or empty when the series is unknown.</summary>
    public static async Task<string> SeriesNameAsync(
        this AppDbContext db, int seriesId, CancellationToken ct) =>
        await db.Series.Where(s => s.Id == seriesId).Select(s => s.Name).FirstOrDefaultAsync(ct)
        ?? string.Empty;
}
