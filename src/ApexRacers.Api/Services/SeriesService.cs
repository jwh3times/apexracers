using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class SeriesService(AppDbContext db)
{
    /// <summary>
    /// One card per series that has racing to show: its current season, the week that season is in,
    /// that week's track, and how many cars and drivers turned out for it.
    ///
    /// <para><b>One card per series, not per active season.</b> This used to project every active
    /// season straight to a card, on the assumption that a series has one. It does not: iRacing
    /// leaves the outgoing season active while marking the incoming one active, so through a
    /// changeover a recurring series appeared in the browser twice — once for the quarter drivers
    /// were actually racing and once for a quarter that had not started. Which of the two is real is
    /// <see cref="SeasonCalendar.CurrentSeasonId"/>'s decision, resolved per series before anything
    /// is projected; everything below then reads from that one season only.</para>
    ///
    /// <para>Four queries rather than one: an earlier version resolved "the current week" inside the
    /// projection, which meant repeating the same
    /// <c>Where(StartDate &lt;= today).OrderByDescending(StartDate)</c> subquery six times — six
    /// correlated subqueries per row — and left the rule expressed in SQL, where
    /// <see cref="SeasonCalendar"/> could not be shared with the standings page that answers the
    /// same question. Resolving season and week in memory fixes both, and keeps each query's row
    /// count bounded by the series count rather than by stored history.</para>
    /// </summary>
    /// <param name="today">Overridable only so tests can sit on a changeover boundary.</param>
    public async Task<List<SeriesDto>> GetActiveSeriesAsync(
        CancellationToken ct = default, DateOnly? today = null)
    {
        var onDate = today ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // Which series to show is still "has an active season" — an upstream status change is how a
        // series leaves the browser. Which *season* backs the card is the rule's call, and may be a
        // season upstream has already deactivated but whose successor has not begun.
        var activeSeriesIds = await db.Seasons
            .Where(s => s.Active)
            .Select(s => s.SeriesId)
            .Distinct()
            .ToListAsync(ct);

        if (activeSeriesIds.Count == 0) return [];

        var currentSeasonIds = await db.CurrentSeasonIdsAsync(activeSeriesIds, ct, onDate);
        if (currentSeasonIds.Count == 0) return [];

        var selectedSeasonIds = currentSeasonIds.Values.ToList();

        var seasons = await db.Seasons
            .Where(s => selectedSeasonIds.Contains(s.Id))
            .Select(s => new
            {
                s.Id,
                s.SeriesId,
                SeriesName = s.Series.Name,
                s.Series.Category,
            })
            .ToListAsync(ct);

        if (seasons.Count == 0) return [];

        var seasonIds = seasons.Select(s => s.Id).ToList();

        var weeks = await db.Weeks
            .Where(w => seasonIds.Contains(w.SeasonId))
            .Select(w => new
            {
                w.Id,
                w.SeasonId,
                w.RaceWeekIndex,
                w.StartDate,
                TrackName = (string?)w.Track.Name,
                TrackConfigName = (string?)w.Track.ConfigName,
            })
            .ToListAsync(ct);

        var weeksBySeason = weeks.GroupBy(w => w.SeasonId).ToDictionary(g => g.Key, g => g.ToList());

        // The week each season is currently in — one shared rule, applied per season.
        var currentWeekBySeason = weeksBySeason.ToDictionary(
            kvp => kvp.Key,
            kvp =>
            {
                var raceWeekIndex = SeasonCalendar.CurrentRaceWeekIndex(
                    kvp.Value.Select(w => (w.RaceWeekIndex, w.StartDate)), onDate);
                return raceWeekIndex is null
                    ? null
                    : kvp.Value.First(w => w.RaceWeekIndex == raceWeekIndex.Value);
            });

        var currentWeekIds = currentWeekBySeason.Values
            .Where(w => w is not null)
            .Select(w => w!.Id)
            .ToList();

        // Distinct (week, car, driver) triples for the current weeks, counted per week in memory.
        // Counting distinct columns inside a grouped projection is the kind of shape that
        // translates on one provider and not another; this one is unambiguous, and the row count is
        // a single week's official results per active series.
        var entrants = currentWeekIds.Count == 0
            ? []
            : await db.SubsessionResults
                .Where(r => r.Subsession.WeekId.HasValue
                         && currentWeekIds.Contains(r.Subsession.WeekId.Value)
                         && r.Subsession.OfficialSession)
                .Select(r => new { WeekId = r.Subsession.WeekId!.Value, r.CarId, r.CustId })
                .Distinct()
                .ToListAsync(ct);

        var countsByWeek = entrants
            .GroupBy(e => e.WeekId)
            .ToDictionary(
                g => g.Key,
                g => (Cars: g.Select(e => e.CarId).Distinct().Count(),
                      Drivers: g.Select(e => e.CustId).Distinct().Count()));

        return seasons
            .Select(season =>
            {
                var current = currentWeekBySeason.GetValueOrDefault(season.Id);
                (int Cars, int Drivers) counts = current is null
                    ? (0, 0)
                    : countsByWeek.GetValueOrDefault(current.Id, (0, 0));

                return new SeriesDto(
                    season.SeriesId,
                    season.SeriesName,
                    season.Id,
                    current?.RaceWeekIndex,
                    season.Category,
                    current?.TrackName,
                    ConfigurationName.NullIfAbsent(current?.TrackConfigName),
                    counts.Cars,
                    counts.Drivers);
            })
            .ToList();
    }
}
