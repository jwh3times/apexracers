using ApexRacers.Api.Dtos;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class PercentileCalculationService(AppDbContext db)
{
    public async Task<PercentileResultDto?> ComputeAndCacheAsync(
        int seriesId,
        int weekId,
        int carId,
        long customerId,
        CancellationToken ct = default)
    {
        // Find the driver's best official TT time for this car/week.
        var driverBest = await db.LapTimeEntries
            .Where(l => l.WeekId == weekId && l.CarId == carId && l.DriverCustomerId == customerId)
            .MinAsync(l => (double?)l.LapTimeSeconds, ct);

        if (driverBest is null) return null;

        var total = await db.LapTimeEntries
            .CountAsync(l => l.WeekId == weekId && l.CarId == carId, ct);

        var slowerCount = await db.LapTimeEntries
            .CountAsync(l => l.WeekId == weekId && l.CarId == carId && l.LapTimeSeconds > driverBest, ct);

        // Percentile: fraction of the field the driver beats, excluding their own row.
        var percentileRank = total > 1 ? slowerCount * 100.0 / (total - 1) : 100.0;
        var computedAt = DateTimeOffset.UtcNow;

        // Cache if a user account is linked to this iRacing customer ID.
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.IRacingCustomerId == customerId, ct);

        if (user is not null)
        {
            var cached = await db.CarPercentileResults
                .FirstOrDefaultAsync(
                    r => r.UserId == user.Id && r.CarId == carId && r.WeekId == weekId, ct);

            if (cached is null)
            {
                db.CarPercentileResults.Add(new CarPercentileResult
                {
                    UserId        = user.Id,
                    CarId         = carId,
                    WeekId        = weekId,
                    PercentileRank = percentileRank,
                    SampleSize    = total,
                    ComputedAt    = computedAt,
                });
            }
            else
            {
                cached.PercentileRank = percentileRank;
                cached.SampleSize     = total;
                cached.ComputedAt     = computedAt;
            }

            await db.SaveChangesAsync(ct);
        }

        return new PercentileResultDto(seriesId, weekId, carId, customerId, percentileRank, total, computedAt);
    }
}
