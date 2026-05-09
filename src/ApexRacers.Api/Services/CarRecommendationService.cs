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

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.IRacingCustomerId == customerId, ct);

        // All distinct cars with official TT times in this week.
        var carsInWeek = await db.LapTimeEntries
            .Where(l => l.WeekId == weekId)
            .Select(l => new { l.CarId, l.Car.Name })
            .Distinct()
            .ToListAsync(ct);

        var results = new List<CarRecommendationDto>();

        foreach (var car in carsInWeek)
        {
            double percentileRank;
            int sampleSize;

            // Use cached result when available; compute and cache otherwise.
            var cached = user is not null
                ? await db.CarPercentileResults
                    .FirstOrDefaultAsync(
                        r => r.UserId == user.Id && r.CarId == car.CarId && r.WeekId == weekId, ct)
                : null;

            if (cached is not null)
            {
                percentileRank = cached.PercentileRank;
                sampleSize     = cached.SampleSize;
            }
            else
            {
                var computed = await percentileService.ComputeAndCacheAsync(
                    week.SeriesId, weekId, car.CarId, customerId, ct);

                if (computed is null) continue; // driver has no TT time for this car
                percentileRank = computed.PercentileRank;
                sampleSize     = computed.SampleSize;
            }

            results.Add(new CarRecommendationDto(
                Rank: 0, // assigned after sort
                CarId: car.CarId,
                CarName: car.Name,
                PercentileRank: percentileRank,
                SampleSize: sampleSize));
        }

        return results
            .OrderByDescending(r => r.PercentileRank)
            .Select((r, i) => r with { Rank = i + 1 })
            .ToList();
    }
}
