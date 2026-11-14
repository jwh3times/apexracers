using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ApexRacers.Api.Services;

public class WeekCarStatsService(
    AppDbContext db,
    WorldRecordService? worldRecords = null,
    ILogger<WeekCarStatsService>? logger = null)
{
    public async Task<List<WeekCarDto>> GetCarsForWeekAsync(int seriesId, int raceWeekIndex, CancellationToken ct = default)
    {
        var seasonId = await db.CurrentSeasonIdAsync(seriesId, ct);
        if (seasonId is null) return [];

        var weekDbId = await db.Weeks
            .InSeason(seasonId.Value, raceWeekIndex)
            .Select(w => (Guid?)w.Id)
            .FirstOrDefaultAsync(ct);

        return weekDbId is null ? [] : await BuildCarStatsAsync(weekDbId.Value, ct);
    }

    public async Task<WeekDetailDto?> GetWeekDetailAsync(int seriesId, int raceWeekIndex, CancellationToken ct = default)
    {
        var seasonId = await db.CurrentSeasonIdAsync(seriesId, ct);
        if (seasonId is null) return null;

        var weekInfo = await db.Weeks
            .InSeason(seasonId.Value, raceWeekIndex)
            .Select(w => new
            {
                WeekId           = w.Id,
                w.TrackId,
                SeriesName       = w.Season.Series.Name,
                Category         = w.Season.Series.Category,
                TrackName        = (string?)w.Track.Name,
                TrackConfigName  = (string?)w.Track.ConfigName,
                TrackLengthMiles = w.Track.TrackConfigLength,
            })
            .FirstOrDefaultAsync(ct);

        if (weekInfo is null) return null;

        var stats = await BuildCarStatsAsync(weekInfo.WeekId, ct);
        var cars = new List<WeekDetailCarDto>(stats.Count);
        // Sequential on purpose: the record lookup reads and writes the cache through this scoped
        // AppDbContext, which is not safe for concurrent use. After a failed lookup no further lookups
        // run: a failure inside the cache write can leave entities tracked on this shared context, and
        // a later Car's write would then fail with them. The cars after it show no record.
        var lookupsAvailable = worldRecords is not null;
        foreach (var car in stats)
        {
            double? record = null;
            if (lookupsAvailable)
                (record, lookupsAvailable) = await WorldRecordLookupAsync(car.CarId, weekInfo.TrackId, ct);
            cars.Add(new WeekDetailCarDto(
                car.CarId, car.CarName, car.ClassName, car.EntryCount,
                car.FastestLapSeconds, car.MedianLapSeconds,
                record, WorldRecordService.GapSeconds(car.FastestLapSeconds, record)));
        }

        return new WeekDetailDto(
            weekInfo.SeriesName,
            weekInfo.Category,
            weekInfo.TrackName,
            ConfigurationName.NullIfAbsent(weekInfo.TrackConfigName),
            weekInfo.TrackLengthMiles,
            cars);
    }

    /// <summary>
    /// The world record for one Car at the Week's Track (null when there is none), and whether
    /// further lookups may run. A failed lookup degrades to no record rather than failing Week
    /// Detail, and stops the remaining lookups; cancellation still propagates.
    /// </summary>
    private async Task<(double? Record, bool ContinueLookups)> WorldRecordLookupAsync(
        int carId, int trackId, CancellationToken ct)
    {
        try
        {
            return (await worldRecords!.GetWorldRecordLapSecondsAsync(carId, trackId, ct), true);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger?.LogWarning(
                "World-record lookup failed for car {CarId} at track {TrackId}; Week Detail shows no record for it or the cars after it. Failure type: {FailureType}",
                carId, trackId, ex.GetType().Name);
            return (null, false);
        }
    }

    private async Task<List<WeekCarDto>> BuildCarStatsAsync(Guid weekDbId, CancellationToken ct)
    {
        // Best lap per driver per car, grouped so each driver contributes once
        var driverBests = await db.SubsessionResults
            .Where(r => r.Subsession.WeekId == weekDbId && r.BestLapSeconds > 0)
            .GroupBy(r => new { r.CustId, r.CarId, CarName = r.Car.Name, ClassName = r.CarClass.Name })
            .Select(g => new { g.Key.CarId, g.Key.CarName, g.Key.ClassName, BestLap = g.Min(r => r.BestLapSeconds) })
            .ToListAsync(ct);

        return driverBests
            .GroupBy(r => new { r.CarId, r.CarName, r.ClassName })
            .Select(g =>
            {
                var sorted = g.Select(r => r.BestLap).Order().ToList();
                double median = FieldPercentile.MedianOfSorted(sorted);
                return new WeekCarDto(g.Key.CarId, g.Key.CarName, g.Key.ClassName, sorted.Count, sorted[0], median);
            })
            .OrderBy(d => d.FastestLapSeconds)
            .ToList();
    }
}
