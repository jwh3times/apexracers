using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class CarRecommendationService(AppDbContext db, PercentileCalculationService percentileService)
{
    public async Task<List<CarRecommendationDto>> GetRecommendationsAsync(
        int seriesId,
        int weekNumber,
        long customerId,
        CancellationToken ct = default)
    {
        var week = await db.Weeks
            .Where(w => w.WeekNumber == weekNumber && w.Season.SeriesId == seriesId && w.Season.Active)
            .Select(w => new { w.Id, SeriesId = w.Season.SeriesId, w.WeekNumber })
            .FirstOrDefaultAsync(ct);

        if (week is null) return [];

        var weekDbId = week.Id;

        var carsInWeek = await db.LapTimeEntries
            .Where(l => l.WeekId == weekDbId)
            .Select(l => new { l.CarId, l.Car.Name })
            .Distinct()
            .ToListAsync(ct);

        var actualLapsThisWeek = await db.LapTimeEntries
            .Where(l => l.WeekId == weekDbId && l.DriverCustomerId == customerId)
            .GroupBy(l => l.CarId)
            .Select(g => new { CarId = g.Key, BestLap = g.Min(l => l.LapTimeSeconds) })
            .ToDictionaryAsync(x => x.CarId, x => x.BestLap, ct);

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.IRacingCustomerId == customerId, ct);

        var cachedPercentiles = user is not null
            ? await db.CarPercentileResults
                .Where(r => r.UserId == user.Id)
                .GroupBy(r => r.CarId)
                .Select(g => new { CarId = g.Key, BestPercentile = g.Max(r => r.PercentileRank) })
                .ToDictionaryAsync(x => x.CarId, x => x.BestPercentile, ct)
            : [];

        var results = new List<CarRecommendationDto>();

        foreach (var car in carsInWeek)
        {
            if (actualLapsThisWeek.TryGetValue(car.CarId, out var actualBestLap))
            {
                var computed = await percentileService.ComputeAndCacheAsync(
                    week.SeriesId, weekNumber, car.CarId, customerId, ct);

                if (computed is null) continue;

                results.Add(new CarRecommendationDto(
                    Rank: 0,
                    CarId: car.CarId,
                    CarName: car.Name,
                    PercentileRank: computed.PercentileRank,
                    SampleSize: computed.SampleSize,
                    EstimatedLapSeconds: actualBestLap,
                    IsProjected: false));
            }
            else
            {
                if (!cachedPercentiles.TryGetValue(car.CarId, out var historicalPercentile))
                {
                    var livePercentile = await ComputeBestHistoricalPercentileAsync(
                        car.CarId, customerId, excludeWeekDbId: weekDbId, ct);

                    if (livePercentile is null) continue;
                    historicalPercentile = livePercentile.Value;
                }

                var projectedLap = await ProjectedLapTimeAsync(weekDbId, car.CarId, historicalPercentile, ct);
                if (projectedLap is null) continue;

                var sampleSize = await db.LapTimeEntries
                    .CountAsync(l => l.WeekId == weekDbId && l.CarId == car.CarId, ct);

                results.Add(new CarRecommendationDto(
                    Rank: 0,
                    CarId: car.CarId,
                    CarName: car.Name,
                    PercentileRank: historicalPercentile,
                    SampleSize: sampleSize,
                    EstimatedLapSeconds: projectedLap.Value,
                    IsProjected: true));
            }
        }

        return results
            .OrderBy(r => r.EstimatedLapSeconds)
            .Select((r, i) => r with { Rank = i + 1 })
            .ToList();
    }

    private async Task<double?> ComputeBestHistoricalPercentileAsync(
        int carId, long customerId, Guid excludeWeekDbId, CancellationToken ct)
    {
        var userLapsByWeek = await db.LapTimeEntries
            .Where(l => l.CarId == carId
                     && l.DriverCustomerId == customerId
                     && l.WeekId != excludeWeekDbId)
            .GroupBy(l => l.WeekId)
            .Select(g => new { WeekId = g.Key, BestLap = g.Min(l => l.LapTimeSeconds) })
            .ToListAsync(ct);

        if (userLapsByWeek.Count == 0) return null;

        var weekIds = userLapsByWeek.Select(e => e.WeekId).ToList();
        var fieldLapRows = await db.LapTimeEntries
            .Where(l => weekIds.Contains(l.WeekId) && l.CarId == carId)
            .Select(l => new { l.WeekId, l.LapTimeSeconds })
            .ToListAsync(ct);

        var fieldLapsByWeek = fieldLapRows
            .GroupBy(l => l.WeekId)
            .ToDictionary(g => g.Key, g => g.Select(l => l.LapTimeSeconds).ToList());

        double best = 0;
        foreach (var entry in userLapsByWeek)
        {
            if (!fieldLapsByWeek.TryGetValue(entry.WeekId, out var fieldLaps)) continue;
            var total = fieldLaps.Count;
            var slower = fieldLaps.Count(t => t > entry.BestLap);
            var percentile = total > 1 ? slower * 100.0 / (total - 1) : 100.0;
            if (percentile > best) best = percentile;
        }
        return best;
    }

    private async Task<double?> ProjectedLapTimeAsync(
        Guid weekDbId, int carId, double percentileRank, CancellationToken ct)
    {
        var laps = await db.LapTimeEntries
            .Where(l => l.WeekId == weekDbId && l.CarId == carId)
            .OrderBy(l => l.LapTimeSeconds)
            .Select(l => l.LapTimeSeconds)
            .ToListAsync(ct);

        if (laps.Count == 0) return null;

        var n = laps.Count;
        var pos = (n - 1) * (1.0 - percentileRank / 100.0);
        var low = (int)Math.Floor(pos);
        var high = Math.Min((int)Math.Ceiling(pos), n - 1);

        var frac = pos - low;
        return laps[low] + frac * (laps[high] - laps[low]);
    }
}
