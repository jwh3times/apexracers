using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class SeriesService(AppDbContext db)
{
    /// <summary>
    /// Every active season with the week it is currently in, that week's track, and how many cars
    /// and drivers turned out for it.
    ///
    /// Three queries rather than one: the previous version resolved "the current week" inside the
    /// projection, which meant repeating the same
    /// <c>Where(StartDate &lt;= today).OrderByDescending(StartDate)</c> subquery six times — six
    /// correlated subqueries per row — and left the rule expressed in SQL, where
    /// <see cref="SeasonCalendar"/> could not be shared with the standings page that answers the
    /// same question. Resolving the week once in memory fixes both.
    /// </summary>
    public async Task<List<SeriesDto>> GetActiveSeriesAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var seasons = await db.Seasons
            .Where(s => s.Active)
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
                w.WeekNumber,
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
                var weekNumber = SeasonCalendar.CurrentWeekNumber(
                    kvp.Value.Select(w => (w.WeekNumber, w.StartDate)), today);
                return weekNumber is null
                    ? null
                    : kvp.Value.First(w => w.WeekNumber == weekNumber.Value);
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
                    current?.WeekNumber,
                    season.Category,
                    current?.TrackName,
                    current?.TrackConfigName,
                    counts.Cars,
                    counts.Drivers);
            })
            .ToList();
    }
}
