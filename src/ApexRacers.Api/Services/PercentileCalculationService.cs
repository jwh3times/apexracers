using ApexRacers.Api.Dtos;
using ApexRacers.Core.Models;
using ApexRacers.Core.PercentileHistory;
using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class PercentileCalculationService(
    AppDbContext db, SubjectDriverContext subjectDriverContext, WorldRecordService? worldRecords = null, PercentileRefresh? refresh = null)
{
    public async Task<PercentileResultDto?> ComputeAndCacheAsync(
        int seriesId,
        int raceWeekIndex,
        int carId,
        long customerId,
        PersonalBestEvidence evidence,
        Guid? callerUserId = null,
        CancellationToken ct = default)
    {
        var seasonId = await db.CurrentSeasonIdAsync(seriesId, ct);
        if (seasonId is null) return null;

        var week = await db.Weeks
            .InSeason(seasonId.Value, raceWeekIndex)
            .Select(w => new
            {
                w.Id,
                w.TrackId,
                SeriesName = w.Season.Series.Name,
                TrackName = (string?)w.Track.Name,
                TrackConfigName = (string?)w.Track.ConfigName,
            })
            .FirstOrDefaultAsync(ct);

        if (week is null) return null;

        // Best race lap per driver for this week+car (dedup multi-race entries)
        var fieldByDriver = await db.SubsessionResults
            .Where(r => r.Subsession.WeekId == week.Id && r.Subsession.TrackId == week.TrackId && r.CarId == carId && r.BestLapSeconds > 0)
            .GroupBy(r => r.CustId)
            .Select(g => new { CustId = g.Key, BestLap = g.Min(r => r.BestLapSeconds) })
            .ToListAsync(ct);

        var driverRaceBest = fieldByDriver.FirstOrDefault(d => d.CustId == customerId)?.BestLap;

        // Only the authenticated User's currently resolved Demo Subject can refresh history.
        // Arbitrary Driver lookups never select an owner through a stored claim.
        RefreshedPercentile? refreshed = null;
        if (callerUserId is { } userId && db.Provenance == DataProvenance.Demo
            && (await subjectDriverContext.GetSubjectDriverAsync(userId, ct)) is
                { Provenance: DataProvenance.Demo } subject && subject.CustomerId == customerId)
        {
            var batch = await (refresh ?? new PercentileRefresh(db, TimeProvider.System,
                new UnavailableDriverEnforcementJournal())).RefreshDemoAsync(
                    userId, evidence.Selection, seriesId, [week.Id], [carId], ct);
            refreshed = batch.Readings.SingleOrDefault();
            if (refreshed is null) return null;
        }
        // Quarantined legacy uploads cannot supply verified Personal evidence. Typed Personal
        // calculations enter through the authorized refresh module, under their own grant.
        if ((refreshed is null ? PersonalBest.Select(driverRaceBest, null) : refreshed.PersonalBest) is not { } driverBest) return null;

        // Rank against other drivers' race laps only: the driver's own slower race result would
        // otherwise count as one more driver beaten whenever an Uploaded Lap supersedes it.
        var otherDriversLaps = refreshed?.OtherDriversLaps.ToList() ?? fieldByDriver
            .Where(d => d.CustId != customerId)
            .Select(d => d.BestLap)
            .ToList();

        // The Field is those drivers plus the subject on the lap actually being ranked — which is
        // not the row bearing their customer id when an Uploaded Lap superseded it. Every metric
        // below is computed over that same population, so the rank, the position, the median and
        // the distribution can never disagree about who was counted.
        var total = refreshed?.FieldSize ?? FieldPercentile.FieldSize(otherDriversLaps);
        var percentileRank = refreshed?.PercentileRank ?? FieldPercentile.Rank(driverBest.LapSeconds, otherDriversLaps);
        var fieldPosition = refreshed?.FieldPosition ?? FieldPercentile.Position(driverBest.LapSeconds, otherDriversLaps);
        var topSharePercent = refreshed?.TopSharePercent ?? FieldPercentile.TopSharePercent(driverBest.LapSeconds, otherDriversLaps);

        var computedAt = refreshed?.ComputedAt ?? DateTimeOffset.UtcNow;

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
            seriesId, raceWeekIndex, carId, customerId,
            percentileRank, fieldPosition, topSharePercent, total,
            FieldPercentile.IsPresentable(total), computedAt,
            week.SeriesName, week.TrackName, ConfigurationName.NullIfAbsent(week.TrackConfigName),
            driverBest.LapSeconds, driverBest.Evidence, fieldBest, fieldMedian,
            distribution, wrLap, wrGap, UploadedBestOutsideWeek: null);
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
