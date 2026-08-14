using ApexRacers.Api.Dtos;
using ApexRacers.Core.Models;
using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class PercentileCalculationService(AppDbContext db, WorldRecordService? worldRecords = null)
{
    public async Task<PercentileResultDto?> ComputeAndCacheAsync(
        int seriesId,
        int weekNumber,
        int carId,
        long customerId,
        Guid? callerUserId = null,
        bool includePersonalLaps = false,
        IReadOnlyList<LapSessionType>? personalLapTypes = null,
        CancellationToken ct = default)
    {
        var seasonId = await db.CurrentSeasonIdAsync(seriesId, ct);
        if (seasonId is null) return null;

        var week = await db.Weeks
            .InSeason(seasonId.Value, weekNumber)
            .Select(w => new
            {
                w.Id,
                w.TrackId,
                SeriesName      = w.Season.Series.Name,
                TrackName       = (string?)w.Track.Name,
                TrackConfigName = (string?)w.Track.ConfigName,
            })
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

        // Fetch the authenticated caller's profile for personal-lap lookup and cache upsert.
        // Never use the caller-supplied customerId for this lookup — that would allow any user
        // to read another user's private personal laps or write cache rows under their account.
        var user = callerUserId.HasValue
            ? await db.Users.FirstOrDefaultAsync(u => u.Id == callerUserId.Value, ct)
            : null;

        if (includePersonalLaps && user is not null)
        {
            var types = personalLapTypes ?? [];
            var personalBest = await db.PersonalLaps
                .Where(p => p.UserId == user.Id && p.CarId == carId
                         && p.TrackId == week.TrackId && p.IsValidLap
                         && (!types.Any() || types.Contains(p.SessionType) || p.SessionType == LapSessionType.Unknown))
                .MinAsync(p => (double?)p.LapTimeSeconds, ct);

            if (personalBest is not null && (driverBest is null || personalBest < driverBest))
                driverBest = personalBest;
        }

        if (driverBest is null) return null;

        // Rank against other drivers' race laps only: the driver's own slower race result would
        // otherwise count as one more driver beaten whenever a personal lap supersedes it.
        var otherDriversLaps = fieldByDriver
            .Where(d => d.CustId != customerId)
            .Select(d => d.BestLap)
            .ToList();

        // The Field is those drivers plus the subject on the lap actually being ranked — which is
        // not the row bearing their customer id when an uploaded lap superseded it. Every metric
        // below is computed over that same population, so the rank, the position, the median and
        // the distribution can never disagree about who was counted.
        var total = FieldPercentile.FieldSize(otherDriversLaps);
        var percentileRank = FieldPercentile.Rank(driverBest.Value, otherDriversLaps);
        var fieldPosition = FieldPercentile.Position(driverBest.Value, otherDriversLaps);
        var topSharePercent = FieldPercentile.TopSharePercent(driverBest.Value, otherDriversLaps);

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
                    UserId          = user.Id,
                    CarId           = carId,
                    SeriesId        = seriesId,
                    WeekId          = week.Id,
                    PercentileRank  = percentileRank,
                    TopSharePercent = topSharePercent,
                    SampleSize      = total,
                    ComputedAt      = computedAt,
                });
            }
            else
            {
                cached.PercentileRank  = percentileRank;
                cached.TopSharePercent = topSharePercent;
                cached.SampleSize      = total;
                cached.ComputedAt      = computedAt;
            }

            await db.SaveChangesAsync(ct);
        }

        // Field stats and distribution — over the same Field the rank was computed against, so a
        // driver whose uploaded lap leads the Field sees a zero gap to the field best rather than
        // a negative one against a race best they beat.
        var sortedLaps = otherDriversLaps.Append(driverBest.Value).OrderBy(x => x).ToList();
        var fieldBest = sortedLaps[0];
        var fieldMedian = FieldPercentile.MedianOfSorted(sortedLaps);
        var distribution = BuildDistribution(sortedLaps, driverBest.Value);

        // World-record overlay (best-effort; null when iRacing isn't configured).
        double? wrLap = worldRecords is null
            ? null
            : await worldRecords.GetWorldRecordLapSecondsAsync(carId, week.TrackId, ct);
        double? wrGap = wrLap is null ? null : Math.Round(driverBest.Value - wrLap.Value, 4);

        return new PercentileResultDto(
            seriesId, weekNumber, carId, customerId,
            percentileRank, fieldPosition, topSharePercent, total, computedAt,
            week.SeriesName, week.TrackName, week.TrackConfigName,
            driverBest.Value, fieldBest, fieldMedian,
            distribution, wrLap, wrGap);
    }

    private static IReadOnlyList<DistributionBin> BuildDistribution(List<double> sortedLaps, double userBest)
    {
        const int binCount = 20;
        var minBound = Math.Min(sortedLaps[0], userBest);
        var maxBound = Math.Max(sortedLaps[^1], userBest);
        var range = maxBound - minBound;
        if (range < 0.001) range = 1.0;
        var binWidth = range / binCount;

        return Enumerable.Range(0, binCount)
            .Select(i =>
            {
                var binMin = minBound + i * binWidth;
                var binMax = i == binCount - 1 ? maxBound + 0.001 : binMin + binWidth;
                var count = sortedLaps.Count(l => l >= binMin && l < binMax);
                var containsUser = userBest >= binMin && userBest < binMax;
                return new DistributionBin(binMin, binMax, count, containsUser);
            })
            .ToList();
    }
}
