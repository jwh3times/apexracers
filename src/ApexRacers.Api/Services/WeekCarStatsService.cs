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
            .Select(w => (Guid?)w.Id)
            .FirstOrDefaultAsync(ct);

        if (weekDbId is null) return [];

        var laps = await db.LapTimeEntries
            .Where(l => l.WeekId == weekDbId)
            .Select(l => new { l.CarId, l.Car.Name, l.LapTimeSeconds })
            .ToListAsync(ct);

        return laps
            .GroupBy(l => new { l.CarId, l.Name })
            .Select(g =>
            {
                var sorted = g.Select(l => l.LapTimeSeconds).Order().ToList();
                int mid = sorted.Count / 2;
                double median = sorted.Count % 2 == 0
                    ? (sorted[mid - 1] + sorted[mid]) / 2.0
                    : sorted[mid];
                return new WeekCarDto(g.Key.CarId, g.Key.Name, sorted.Count, sorted[0], median);
            })
            .OrderBy(d => d.FastestLapSeconds)
            .ToList();
    }
}
