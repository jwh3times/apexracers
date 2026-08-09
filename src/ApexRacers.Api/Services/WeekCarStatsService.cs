using ApexRacers.Api.Dtos;
using ApexRacers.Core;
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

        return weekDbId is null ? [] : await BuildCarStatsAsync(weekDbId.Value, ct);
    }

    public async Task<WeekDetailDto?> GetWeekDetailAsync(int seriesId, int weekNumber, CancellationToken ct = default)
    {
        var weekInfo = await db.Weeks
            .Where(w => w.WeekNumber == weekNumber && w.Season.SeriesId == seriesId && w.Season.Active)
            .OrderByDescending(w => w.Season.Year).ThenByDescending(w => w.Season.Quarter)
            .Select(w => new
            {
                WeekId           = w.Id,
                SeriesName       = w.Season.Series.Name,
                Category         = w.Season.Series.Category,
                TrackName        = (string?)w.Track.Name,
                TrackConfigName  = (string?)w.Track.ConfigName,
                TrackLengthMiles = w.Track.TrackConfigLength,
            })
            .FirstOrDefaultAsync(ct);

        if (weekInfo is null) return null;

        var cars = await BuildCarStatsAsync(weekInfo.WeekId, ct);
        return new WeekDetailDto(
            weekInfo.SeriesName,
            weekInfo.Category,
            weekInfo.TrackName,
            weekInfo.TrackConfigName,
            weekInfo.TrackLengthMiles,
            cars);
    }

    private async Task<List<WeekCarDto>> BuildCarStatsAsync(Guid weekDbId, CancellationToken ct)
    {
        // Best lap per driver per car, grouped so each driver contributes once
        var driverBests = await db.SubsessionResults
            .Where(r => r.Subsession.WeekId == weekDbId && r.BestLapSeconds > 0)
            .GroupBy(r => new { r.CustId, r.CarId, CarName = r.Car.Name, ClassName = r.CarClass.Name })
            .Select(g => new { g.Key.CarId, g.Key.CarName, g.Key.ClassName, BestLap = g.Min(r => r.BestLapSeconds) })
            .ToListAsync(ct);

        return driverBests
            .GroupBy(r => new { r.CarId, r.CarName, r.ClassName })
            .Select(g =>
            {
                var sorted = g.Select(r => r.BestLap).Order().ToList();
                double median = FieldPercentile.MedianOfSorted(sorted);
                return new WeekCarDto(g.Key.CarId, g.Key.CarName, g.Key.ClassName, sorted.Count, sorted[0], median);
            })
            .OrderBy(d => d.FastestLapSeconds)
            .ToList();
    }
}
