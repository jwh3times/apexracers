using ApexRacers.Api.Dtos;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class PercentileCalculationService(AppDbContext db)
{
    public async Task<PercentileResultDto?> ComputeAndCacheAsync(
        int seriesId,
        int weekNumber,
        int carId,
        long customerId,
        bool includePersonalLaps = false,
        CancellationToken ct = default)
    {
        var week = await db.Weeks
            .Where(w => w.WeekNumber == weekNumber && w.Season.SeriesId == seriesId && w.Season.Active)
            .OrderByDescending(w => w.Season.Year).ThenByDescending(w => w.Season.Quarter)
            .Select(w => new { w.Id, w.TrackId })
            .FirstOrDefaultAsync(ct);

        if (week is null) return null;

        // Best race lap per driver for this week+car (dedup multi-race entries)
        var fieldByDriver = await db.SubsessionResults
            .Where(r => r.Subsession.WeekId == week.Id && r.CarId == carId && r.BestLapSeconds > 0)
            .GroupBy(r => r.CustId)
            .Select(g => new { CustId = g.Key, BestLap = g.Min(r => r.BestLapSeconds) })
            .ToListAsync(ct);

        if (fieldByDriver.Count == 0) return null;

        var driverRaceBest = fieldByDriver.FirstOrDefault(d => d.CustId == customerId)?.BestLap;
        double? driverBest = driverRaceBest;

        // Fetch the user once for both personal-lap lookup and cache upsert.
        var user = await db.Users.FirstOrDefaultAsync(u => u.IRacingCustomerId == customerId, ct);

        if (includePersonalLaps && user is not null)
        {
            var personalBest = await db.PersonalLaps
                .Where(p => p.UserId == user.Id && p.CarId == carId
                         && p.TrackId == week.TrackId && p.IsValidLap)
                .MinAsync(p => (double?)p.LapTimeSeconds, ct);

            if (personalBest is not null && (driverBest is null || personalBest < driverBest))
                driverBest = personalBest;
        }

        if (driverBest is null) return null;

        bool driverInField = driverRaceBest.HasValue;
        var total = fieldByDriver.Count;

        // Compare only against other drivers' race laps so the driver's own slower
        // race result cannot inflate slowerCount when a personal lap supersedes it.
        var slowerCount = fieldByDriver
            .Where(d => d.CustId != customerId)
            .Count(d => d.BestLap > driverBest.Value);

        var percentileRank = driverInField
            ? (total > 1 ? slowerCount * 100.0 / (total - 1) : 100.0)
            : (total > 0 ? slowerCount * 100.0 / total : 100.0);

        var computedAt = DateTimeOffset.UtcNow;

        if (user is not null)
        {
            var cached = await db.CarPercentileResults
                .FirstOrDefaultAsync(
                    r => r.UserId == user.Id && r.CarId == carId && r.SeriesId == seriesId && r.WeekId == week.Id, ct);

            if (cached is null)
            {
                db.CarPercentileResults.Add(new CarPercentileResult
                {
                    UserId         = user.Id,
                    CarId          = carId,
                    SeriesId       = seriesId,
                    WeekId         = week.Id,
                    PercentileRank = percentileRank,
                    SampleSize     = total,
                    ComputedAt     = computedAt,
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

        return new PercentileResultDto(seriesId, weekNumber, carId, customerId, percentileRank, total, computedAt);
    }
}
