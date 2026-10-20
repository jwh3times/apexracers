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
    private DemoHistoryAccess History { get; } = new(db, subjectDriverContext, refresh);

    public async Task<PercentileResultDto?> ComputeAndCacheAsync(
        int seriesId,
        int raceWeekIndex,
        int carId,
        long customerId,
        PersonalBestEvidence evidence,
        Guid? callerUserId = null,
        CancellationToken ct = default)
    {
        if (await History.ReadAsync(callerUserId, evidence, seriesId, customerId, ct) is { } snapshot)
        {
            var week = PercentileHistoryView.CurrentWeek(snapshot, seriesId, raceWeekIndex);
            if (week is null) return null;
            var reading = snapshot.Readings.SingleOrDefault(r => r.WeekId == week.WeekId && r.CarId == carId);
            if (reading?.PersonalBest is not { } best) return null;
            return await PresentAsync(seriesId, raceWeekIndex, carId, customerId, week.TrackId, week.SeriesName,
                week.TrackName, week.TrackConfigName, best, reading.OtherDriversLaps,
                reading.PercentileRank!.Value, reading.FieldPosition!.Value, reading.TopSharePercent!.Value,
                reading.FieldSize!.Value, reading.ComputedAt, ct);
        }

        // A different Driver's lookup does not select an owner or read private upload sources.
        var seasonId = await db.CurrentSeasonIdAsync(seriesId, ct);
        if (seasonId is null) return null;
        var lookupWeek = await db.Weeks.InSeason(seasonId.Value, raceWeekIndex)
            .Select(w => new { w.Id, w.TrackId, SeriesName = w.Season.Series.Name,
                TrackName = (string?)w.Track.Name, TrackConfigName = (string?)w.Track.ConfigName })
            .FirstOrDefaultAsync(ct);
        if (lookupWeek is null) return null;
        var field = await db.SubsessionResults.Where(r => r.Subsession.WeekId == lookupWeek.Id
                && r.Subsession.TrackId == lookupWeek.TrackId && r.CarId == carId && r.BestLapSeconds > 0)
            .GroupBy(r => r.CustId).Select(g => new { CustId = g.Key, BestLap = g.Min(r => r.BestLapSeconds) })
            .ToListAsync(ct);
        var raceBest = field.FirstOrDefault(r => r.CustId == customerId)?.BestLap;
        if (PersonalBest.Select(raceBest, null) is not { } personalBest) return null;
        var others = field.Where(r => r.CustId != customerId).Select(r => r.BestLap).ToList();
        return await PresentAsync(seriesId, raceWeekIndex, carId, customerId, lookupWeek.TrackId,
            lookupWeek.SeriesName, lookupWeek.TrackName, lookupWeek.TrackConfigName, personalBest, others,
            FieldPercentile.Rank(personalBest.LapSeconds, others), FieldPercentile.Position(personalBest.LapSeconds, others),
            FieldPercentile.TopSharePercent(personalBest.LapSeconds, others), FieldPercentile.FieldSize(others),
            DateTimeOffset.UtcNow, ct);
    }

    private async Task<PercentileResultDto> PresentAsync(int seriesId, int raceWeekIndex, int carId, long customerId,
        int trackId, string seriesName, string? trackName, string? configName, PersonalBest best,
        IReadOnlyList<double> others, double rank, int position, int topShare, int size,
        DateTimeOffset computedAt, CancellationToken ct)
    {
        var sorted = others.Append(best.LapSeconds).Order().ToList();
        var distribution = BuildDistribution(sorted, best.LapSeconds);
        var worldRecord = worldRecords is null ? null : await worldRecords.GetWorldRecordLapSecondsAsync(carId, trackId, ct);
        double? gap = worldRecord is null ? null : Math.Round(best.LapSeconds - worldRecord.Value, 4);
        return new PercentileResultDto(seriesId, raceWeekIndex, carId, customerId, rank, position, topShare, size,
            FieldPercentile.IsPresentable(size), computedAt, seriesName, trackName, ConfigurationName.NullIfAbsent(configName),
            best.LapSeconds, best.Evidence, sorted[0], FieldPercentile.MedianOfSorted(sorted), distribution,
            worldRecord, gap, UploadedBestOutsideWeek: null);
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
