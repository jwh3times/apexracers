using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// "Which season and week does this series id mean right now?" — asked by seven services and,
/// before this module, answered by ten hand-written copies of the same predicate.
///
/// <para>These are <see cref="IQueryable{T}"/> composables rather than methods returning a fixed
/// record, because the duplication was in the <em>filter and the ordering</em>, not the shape:
/// each caller projects different columns (one wants only the id, another wants track metadata,
/// another the series name and category). Returning a record would have forced every caller to
/// fetch the union of all of them and thrown away the projection pushdown. Composing on the
/// queryable shares exactly the part that was duplicated and nothing else.</para>
///
/// <para>The Year/Quarter ordering is the load-bearing detail. A series can have more than one
/// season flagged active during a changeover, so "the active season" means the newest one — get
/// that wrong and a query silently reads last quarter's data.</para>
/// </summary>
public static class SeasonQueries
{
    /// <summary>A series' active seasons, newest first. Take the first for "the" active season.</summary>
    public static IQueryable<Season> ActiveForSeries(this IQueryable<Season> seasons, int seriesId) =>
        seasons
            .Where(s => s.SeriesId == seriesId && s.Active)
            .OrderByDescending(s => s.Year)
            .ThenByDescending(s => s.Quarter);

    /// <summary>
    /// One numbered week of a series' active season, newest season first. Compose a
    /// <c>.Select(…)</c> for whichever columns the caller actually needs.
    /// </summary>
    public static IQueryable<Week> InActiveSeason(
        this IQueryable<Week> weeks, int seriesId, int weekNumber) =>
        weeks
            .Where(w => w.WeekNumber == weekNumber && w.Season.SeriesId == seriesId && w.Season.Active)
            .OrderByDescending(w => w.Season.Year)
            .ThenByDescending(w => w.Season.Quarter);

    /// <summary>
    /// The series' active season, or a <see cref="KeyNotFoundException"/> the exception middleware
    /// maps to a 404. One wording, one place — it was previously duplicated verbatim three times.
    /// </summary>
    public static async Task<Season> ActiveSeasonOrThrowAsync(
        this AppDbContext db, int seriesId, CancellationToken ct) =>
        await db.Seasons.ActiveForSeries(seriesId).FirstOrDefaultAsync(ct)
        ?? throw new KeyNotFoundException($"No active season for series {seriesId}.");

    /// <summary>The series' display name, or empty when the series is unknown.</summary>
    public static async Task<string> SeriesNameAsync(
        this AppDbContext db, int seriesId, CancellationToken ct) =>
        await db.Series.Where(s => s.Id == seriesId).Select(s => s.Name).FirstOrDefaultAsync(ct)
        ?? string.Empty;
}
