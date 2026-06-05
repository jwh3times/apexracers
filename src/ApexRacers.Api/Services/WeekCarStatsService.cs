using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class WeekCarStatsService(AppDbContext db)
{
    public async Task<List<WeekCarDto>> GetCarsForWeekAsync(int seriesId, int weekNumber, CancellationToken ct = default)
    {
        var weekDbId = await db.Weeks
            .Where(w => w.WeekNumber == weekNumber && w.Season.SeriesId == seriesId && w.Season.Active)
            .OrderByDescending(w => w.Season.Year).ThenByDescending(w => w.Season.Quarter)
            .Select(w => (Guid?)w.Id)
            .FirstOrDefaultAsync(ct);

        if (weekDbId is null) return [];

        // Best lap per driver per car, then compute stats
        var driverBests = await db.SubsessionResults
            .Where(r => r.Subsession.WeekId == weekDbId && r.BestLapSeconds > 0)
            .GroupBy(r => new { r.CustId, r.CarId, CarName = r.Car.Name })
            .Select(g => new { g.Key.CarId, g.Key.CarName, BestLap = g.Min(r => r.BestLapSeconds) })
            .ToListAsync(ct);

        return driverBests
            .GroupBy(r => new { r.CarId, r.CarName })
            .Select(g =>
            {
                var sorted = g.Select(r => r.BestLap).Order().ToList();
                int mid = sorted.Count / 2;
                double median = sorted.Count % 2 == 0
                    ? (sorted[mid - 1] + sorted[mid]) / 2.0
                    : sorted[mid];
                return new WeekCarDto(g.Key.CarId, g.Key.CarName, sorted.Count, sorted[0], median);
            })
            .OrderBy(d => d.FastestLapSeconds)
            .ToList();
    }
}
