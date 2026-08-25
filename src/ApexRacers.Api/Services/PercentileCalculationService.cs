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
        PersonalBestEvidence evidence,
        Guid? callerUserId = null,
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

        // Fetch the authenticated caller's profile for Uploaded Lap lookup and cache upsert.
        // Never use the caller-supplied customerId for this lookup — that would allow any user
        // to read another user's private Uploaded Laps or write cache rows under their account.
        var user = callerUserId.HasValue
            ? await db.Users.FirstOrDefaultAsync(u => u.Id == callerUserId.Value, ct)
            : null;

        // Uploaded Laps count only when driven inside this Race Week. A Race Best already belongs
        // to one Race Week, so bounding the uploaded side is what makes the two comparable: an
        // all-time Uploaded Best would otherwise bring a lap from another season — a different
        // build, a different BoP, possibly dry weather against a wet week — into this week's Field.
        var window = await db.RaceWeekWindowAsync(seasonId.Value, weekNumber, ct);

        double? uploadedBest = null;
        UploadedBestOutsideWeekDto? excluded = null;

        if (evidence.IncludesUploadedLaps && user?.IRacingCustomerId == customerId && window is { } weekWindow)
        {
            var scoped = evidence
                .ScopeUploadedLaps(db.UploadedLaps)
                .Where(p => p.UserId == user.Id && p.CarId == carId && p.TrackId == week.TrackId)
                .Where(p => p.IsValidLap);

            uploadedBest = await scoped
                .Where(p => p.RecordedAt >= weekWindow.Start && p.RecordedAt < weekWindow.End)
                .Select(p => (double?)p.LapTimeSeconds)
                .MinAsync(ct);

            // The fastest Uploaded Lap this Race Week excluded, paired with the date it was
            // actually driven. Ordering and taking one keeps the lap and its own date together —
            // a grouped MIN(lap) beside MAX(recorded) would attribute the wrong date to it.
            excluded = await scoped
                .Where(p => p.RecordedAt < weekWindow.Start || p.RecordedAt >= weekWindow.End)
                .OrderBy(p => p.LapTimeSeconds)
                .Select(p => new UploadedBestOutsideWeekDto(p.LapTimeSeconds, p.RecordedAt))
                .FirstOrDefaultAsync(ct);
        }

        if (PersonalBest.Select(driverRaceBest, uploadedBest) is not { } driverBest) return null;

        // Only worth reporting when it would have changed the answer. A slower excluded lap
        // changes nothing, and saying so on every visit would be noise rather than disclosure.
        if (excluded is not null && excluded.LapSeconds >= driverBest.LapSeconds)
            excluded = null;

        // Rank against other drivers' race laps only: the driver's own slower race result would
        // otherwise count as one more driver beaten whenever an Uploaded Lap supersedes it.
        var otherDriversLaps = fieldByDriver
            .Where(d => d.CustId != customerId)
            .Select(d => d.BestLap)
            .ToList();

        // The Field is those drivers plus the subject on the lap actually being ranked — which is
        // not the row bearing their customer id when an Uploaded Lap superseded it. Every metric
        // below is computed over that same population, so the rank, the position, the median and
        // the distribution can never disagree about who was counted.
        var total = FieldPercentile.FieldSize(otherDriversLaps);
        var percentileRank = FieldPercentile.Rank(driverBest.LapSeconds, otherDriversLaps);
        var fieldPosition = FieldPercentile.Position(driverBest.LapSeconds, otherDriversLaps);
        var topSharePercent = FieldPercentile.TopSharePercent(driverBest.LapSeconds, otherDriversLaps);

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
        var sortedLaps = otherDriversLaps.Append(driverBest.LapSeconds).OrderBy(x => x).ToList();
        var fieldBest = sortedLaps[0];
        var fieldMedian = FieldPercentile.MedianOfSorted(sortedLaps);
        var distribution = BuildDistribution(sortedLaps, driverBest.LapSeconds);

        // World-record overlay (best-effort; null when iRacing isn't configured).
        double? wrLap = worldRecords is null
            ? null
            : await worldRecords.GetWorldRecordLapSecondsAsync(carId, week.TrackId, ct);
        double? wrGap = wrLap is null ? null : Math.Round(driverBest.LapSeconds - wrLap.Value, 4);

        return new PercentileResultDto(
            seriesId, weekNumber, carId, customerId,
            percentileRank, fieldPosition, topSharePercent, total,
            FieldPercentile.IsPresentable(total), computedAt,
            week.SeriesName, week.TrackName, week.TrackConfigName,
            driverBest.LapSeconds, driverBest.Evidence, fieldBest, fieldMedian,
            distribution, wrLap, wrGap, excluded);
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
