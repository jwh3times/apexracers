using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class CarRecommendationService(AppDbContext db, PercentileCalculationService percentileService)
{
    public async Task<List<CarRecommendationDto>> GetRecommendationsAsync(
        int weekId,
        long customerId,
        CancellationToken ct = default)
    {
        var week = await db.Weeks
            .Select(w => new { w.Id, w.Season.SeriesId })
            .FirstOrDefaultAsync(w => w.Id == weekId, ct);

        if (week is null) return [];

        var carsInWeek = await db.LapTimeEntries
            .Where(l => l.WeekId == weekId)
            .Select(l => new { l.CarId, l.Car.Name })
            .Distinct()
            .ToListAsync(ct);

        // User's actual best lap per car for this week
        var actualLapsThisWeek = await db.LapTimeEntries
            .Where(l => l.WeekId == weekId && l.DriverCustomerId == customerId)
            .GroupBy(l => l.CarId)
            .Select(g => new { CarId = g.Key, BestLap = g.Min(l => l.LapTimeSeconds) })
            .ToDictionaryAsync(x => x.CarId, x => x.BestLap, ct);

        // Prefer cached percentile results; fall back to live computation from other weeks.
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
                // Path A: driver recorded a lap this week — compute actual percentile and use real time.
                var computed = await percentileService.ComputeAndCacheAsync(
                    week.SeriesId, weekId, car.CarId, customerId, ct);

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
                // Path B: no lap this week — derive percentile rank from cache or historical laps.
                if (!cachedPercentiles.TryGetValue(car.CarId, out var historicalPercentile))
                {
                    var livePercentile = await ComputeBestHistoricalPercentileAsync(
                        car.CarId, customerId, excludeWeekId: weekId, ct);

                    if (livePercentile is null) continue;
                    historicalPercentile = livePercentile.Value;
                }

                var projectedLap = await ProjectedLapTimeAsync(weekId, car.CarId, historicalPercentile, ct);
                if (projectedLap is null) continue;

                var sampleSize = await db.LapTimeEntries
                    .CountAsync(l => l.WeekId == weekId && l.CarId == car.CarId, ct);

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

    // Find the user's best lap for a given car in any week other than the current one,
    // then compute their percentile against that week's full field.
    private async Task<double?> ComputeBestHistoricalPercentileAsync(
        int carId, long customerId, int excludeWeekId, CancellationToken ct)
    {
        var userLapsByWeek = await db.LapTimeEntries
            .Where(l => l.CarId == carId
                     && l.DriverCustomerId == customerId
                     && l.WeekId != excludeWeekId)
            .GroupBy(l => l.WeekId)
            .Select(g => new { WeekId = g.Key, BestLap = g.Min(l => l.LapTimeSeconds) })
            .ToListAsync(ct);

        if (userLapsByWeek.Count == 0) return null;

        double best = 0;
        foreach (var entry in userLapsByWeek)
        {
            var total = await db.LapTimeEntries
                .CountAsync(l => l.WeekId == entry.WeekId && l.CarId == carId, ct);
            var slower = await db.LapTimeEntries
                .CountAsync(l => l.WeekId == entry.WeekId && l.CarId == carId
                              && l.LapTimeSeconds > entry.BestLap, ct);

            var percentile = total > 1 ? slower * 100.0 / (total - 1) : 100.0;
            if (percentile > best) best = percentile;
        }
        return best;
    }

    // Inverse of: percentile = slowerCount / (total - 1) * 100
    // Position from fastest (0-indexed ascending) = (n-1) * (1 - percentile/100)
    private async Task<double?> ProjectedLapTimeAsync(
        int weekId, int carId, double percentileRank, CancellationToken ct)
    {
        var laps = await db.LapTimeEntries
            .Where(l => l.WeekId == weekId && l.CarId == carId)
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
