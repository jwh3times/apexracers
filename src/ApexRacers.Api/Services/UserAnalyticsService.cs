using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class UserAnalyticsService(AppDbContext db)
{
    public async Task<List<CarAnalyticsDto>> GetAnalyticsAsync(
        Guid userId, int? seriesId, CancellationToken ct = default)
    {
        var iracingId = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.IRacingCustomerId)
            .FirstOrDefaultAsync(ct);

        var rawResults = await db.CarPercentileResults
            .Where(r => r.UserId == userId)
            .Where(r => !seriesId.HasValue || r.Week.Season.SeriesId == seriesId.Value)
            .Select(r => new
            {
                r.CarId,
                CarName = r.Car.Name,
                SeriesId = r.Week.Season.SeriesId,
                SeriesName = r.Week.Season.Series.Name,
                r.WeekId,
                WeekNumber = r.Week.WeekNumber,
                TrackName = r.Week.TrackName,
                ConfigName = r.Week.ConfigName,
                r.PercentileRank,
                r.SampleSize,
                r.ComputedAt,
            })
            .ToListAsync(ct);

        if (rawResults.Count == 0) return [];

        var groups = rawResults.GroupBy(r => (r.CarId, r.SeriesId)).ToList();

        var bestWeekIds = groups
            .Select(g => g.MaxBy(r => r.PercentileRank)!.WeekId)
            .Distinct()
            .ToList();

        // Fetch all laps for best weeks to compute median
        var medianLapsRaw = await db.LapTimeEntries
            .Where(l => bestWeekIds.Contains(l.WeekId))
            .Select(l => new { l.CarId, l.WeekId, l.LapTimeSeconds })
            .ToListAsync(ct);

        var medianLapsByWeekCar = medianLapsRaw
            .GroupBy(l => (l.CarId, l.WeekId))
            .ToDictionary(
                g => g.Key,
                g => g.Select(l => l.LapTimeSeconds).OrderBy(t => t).ToList());

        // Fetch driver's own laps for personal best and total count
        var allWeekIds = rawResults.Select(r => r.WeekId).Distinct().ToList();
        Dictionary<(int CarId, Guid WeekId), List<double>> driverLaps = new();

        if (iracingId is > 0)
        {
            var driverLapRows = await db.LapTimeEntries
                .Where(l => allWeekIds.Contains(l.WeekId) && l.DriverCustomerId == iracingId.Value)
                .Select(l => new { l.CarId, l.WeekId, l.LapTimeSeconds })
                .ToListAsync(ct);

            foreach (var row in driverLapRows)
            {
                var key = (row.CarId, row.WeekId);
                if (!driverLaps.TryGetValue(key, out var list))
                    driverLaps[key] = list = [];
                list.Add(row.LapTimeSeconds);
            }
        }

        return groups
            .Select(group =>
            {
                var ordered = group.OrderBy(r => r.ComputedAt).ToList();
                var bestWeek = ordered.MaxBy(r => r.PercentileRank)!;
                var latest = ordered[^1];

                var carDriverLaps = ordered
                    .SelectMany(r => driverLaps.TryGetValue((r.CarId, r.WeekId), out var l) ? l : Enumerable.Empty<double>())
                    .ToList();

                double? personalBest = carDriverLaps.Count > 0 ? carDriverLaps.Min() : null;

                double? median = null;
                if (medianLapsByWeekCar.TryGetValue((bestWeek.CarId, bestWeek.WeekId), out var weekLaps) && weekLaps.Count > 0)
                {
                    var mid = weekLaps.Count / 2;
                    median = weekLaps.Count % 2 == 0
                        ? (weekLaps[mid - 1] + weekLaps[mid]) / 2.0
                        : weekLaps[mid];
                }

                var history = ordered
                    .Select(r => new WeeklyPercentileDto(
                        r.WeekNumber, r.TrackName, r.ConfigName,
                        r.PercentileRank, r.SampleSize, r.ComputedAt))
                    .ToList();

                return new CarAnalyticsDto(
                    group.Key.CarId,
                    group.First().CarName,
                    group.Key.SeriesId,
                    group.First().SeriesName,
                    latest.PercentileRank,
                    group.Max(r => r.PercentileRank),
                    personalBest,
                    median,
                    carDriverLaps.Count,
                    history);
            })
            .OrderByDescending(a => a.BestPercentileRank)
            .ToList();
    }
}
