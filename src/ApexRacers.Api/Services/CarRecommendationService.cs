using ApexRacers.Api.Dtos;
using ApexRacers.Core.Models;
using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class CarRecommendationService(AppDbContext db)
{
    public async Task<List<CarRecommendationDto>> GetRecommendationsAsync(
        int seriesId,
        int weekNumber,
        long customerId,
        bool includePersonalLaps = false,
        IReadOnlyList<LapSessionType>? personalLapTypes = null,
        CancellationToken ct = default)
    {
        var seasonId = await db.CurrentSeasonIdAsync(seriesId, ct);
        if (seasonId is null) return [];

        var week = await db.Weeks
            .InSeason(seasonId.Value, weekNumber)
            .Select(w => new { w.Id, SeriesId = w.Season.SeriesId, w.WeekNumber, w.TrackId })
            .FirstOrDefaultAsync(ct);

        if (week is null) return [];

        var weekDbId = week.Id;

        // ── Bulk pre-fetches ──────────────────────────────────────────────────

        // Best lap per (carId, custId) across all subsessions this week, with car name.
        var weekFieldRows = await db.SubsessionResults
            .Where(r => r.Subsession.WeekId == weekDbId && r.BestLapSeconds > 0)
            .GroupBy(r => new { r.CustId, r.CarId, CarName = r.Car.Name })
            .Select(g => new { g.Key.CarId, g.Key.CarName, g.Key.CustId, BestLap = g.Min(r => r.BestLapSeconds) })
            .ToListAsync(ct);

        if (weekFieldRows.Count == 0) return [];

        // Per-car: rows sorted by lap time (used for both percentile calc and projection).
        var fieldByCar = weekFieldRows
            .GroupBy(r => r.CarId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.BestLap).ToList());

        // Car names keyed by CarId (derived from field rows; no extra round-trip needed).
        var carNames = fieldByCar
            .ToDictionary(g => g.Key, g => g.Value.First().CarName);

        // Driver's best race lap per car this week (extracted from the already-fetched field).
        var actualLapsThisWeek = weekFieldRows
            .Where(r => r.CustId == customerId)
            .ToDictionary(r => r.CarId, r => r.BestLap);

        // User + series-scoped running average percentile per car (used for projected path).
        var user = await db.Users.FirstOrDefaultAsync(u => u.IRacingCustomerId == customerId, ct);

        // Load (Sum, Count) per car so we can compute a running average that includes this week.
        var cachedPercentiles = user is not null
            ? await db.CarPercentileResults
                .Where(r => r.UserId == user.Id && r.SeriesId == week.SeriesId)
                .GroupBy(r => r.CarId)
                .Select(g => new { CarId = g.Key, Sum = g.Sum(r => r.PercentileRank), Count = g.Count() })
                .ToDictionaryAsync(x => x.CarId, x => (x.Sum, x.Count), ct)
            : new Dictionary<int, (double Sum, int Count)>();

        // Existing per-week, per-series cache rows for this user (for upsert without per-car queries).
        var existingCacheThisWeek = user is not null
            ? await db.CarPercentileResults
                .Where(r => r.UserId == user.Id && r.WeekId == weekDbId && r.SeriesId == week.SeriesId)
                .ToDictionaryAsync(r => r.CarId, ct)
            : new Dictionary<int, CarPercentileResult>();

        // Personal lap bests per car at this track (when requested).
        var personalBestByCar = new Dictionary<int, double>();
        if (includePersonalLaps && user is not null)
        {
            var types = personalLapTypes ?? [];
            personalBestByCar = await db.PersonalLaps
                .Where(p => p.UserId == user.Id && p.IsValidLap && p.TrackId == week.TrackId
                         && (!types.Any() || types.Contains(p.SessionType) || p.SessionType == LapSessionType.Unknown))
                .GroupBy(p => p.CarId)
                .Select(g => new { CarId = g.Key, BestLap = g.Min(p => p.LapTimeSeconds) })
                .ToDictionaryAsync(x => x.CarId, x => x.BestLap, ct);
        }

        // Batch-compute historical percentile for projected cars that lack a cached value.
        var projectedCarIds = carNames.Keys
            .Where(carId => !actualLapsThisWeek.ContainsKey(carId) && !cachedPercentiles.ContainsKey(carId)
                            && !existingCacheThisWeek.ContainsKey(carId))
            .ToList();

        var historicalPercentileByCar = new Dictionary<int, double>();
        if (projectedCarIds.Count > 0)
        {
            var driverHistorical = await db.SubsessionResults
                .Where(r => r.Subsession.WeekId.HasValue
                         && r.Subsession.WeekId != weekDbId
                         && projectedCarIds.Contains(r.CarId)
                         && r.CustId == customerId
                         && r.BestLapSeconds > 0)
                .GroupBy(r => new { r.CarId, WeekId = r.Subsession.WeekId!.Value })
                .Select(g => new { g.Key.CarId, g.Key.WeekId, BestLap = g.Min(r => r.BestLapSeconds) })
                .ToListAsync(ct);

            if (driverHistorical.Count > 0)
            {
                var histWeekIds = driverHistorical.Select(r => r.WeekId).Distinct().ToList();
                var histCarIds  = driverHistorical.Select(r => r.CarId).Distinct().ToList();

                var fieldHistorical = await db.SubsessionResults
                    .Where(r => r.Subsession.WeekId.HasValue
                             && histWeekIds.Contains(r.Subsession.WeekId!.Value)
                             && histCarIds.Contains(r.CarId)
                             && r.BestLapSeconds > 0)
                    .GroupBy(r => new { r.CustId, r.CarId, WeekId = r.Subsession.WeekId!.Value })
                    .Select(g => new { g.Key.CustId, g.Key.CarId, g.Key.WeekId, BestLap = g.Min(r => r.BestLapSeconds) })
                    .ToListAsync(ct);

                // Keyed on the *other* drivers' laps — see FieldPercentile.Rank.
                var fieldByCarWeek = fieldHistorical
                    .GroupBy(r => (r.CarId, r.WeekId))
                    .ToDictionary(
                        g => g.Key,
                        g => g.Where(r => r.CustId != customerId).Select(r => r.BestLap).ToList());

                foreach (var carGroup in driverHistorical.GroupBy(r => r.CarId))
                {
                    double? best = null;
                    foreach (var entry in carGroup)
                    {
                        if (!fieldByCarWeek.TryGetValue((entry.CarId, entry.WeekId), out var otherLaps)) continue;
                        var pct = FieldPercentile.Rank(entry.BestLap, otherLaps);
                        if (best is null || pct > best) best = pct;
                    }
                    if (best is not null)
                        historicalPercentileByCar[carGroup.Key] = best.Value;
                }
            }
        }

        // ── Build results in memory ───────────────────────────────────────────

        var computedAt = DateTimeOffset.UtcNow;
        var results = new List<CarRecommendationDto>();

        foreach (var (carId, carName) in carNames)
        {
            var carField = fieldByCar.GetValueOrDefault(carId, []);

            if (actualLapsThisWeek.TryGetValue(carId, out var raceBestLap))
            {
                // Actual path — driver raced this car this week.
                double driverBest = raceBestLap;
                if (personalBestByCar.TryGetValue(carId, out var pb) && pb < driverBest)
                    driverBest = pb;

                var otherLaps = carField.Where(r => r.CustId != customerId).Select(r => r.BestLap).ToList();
                var total = FieldPercentile.FieldSize(otherLaps);
                var percentileRank = FieldPercentile.Rank(driverBest, otherLaps);
                var topShare = FieldPercentile.TopSharePercent(driverBest, otherLaps);

                // Fold this week's reading into the running average and upsert the cache row.
                var newAvg = RecordPercentileReading(
                    user, cachedPercentiles, existingCacheThisWeek,
                    carId, week.SeriesId, weekDbId, percentileRank, topShare, total, computedAt);

                var actualSortedLaps = carField.Select(r => r.BestLap).ToList();
                var projectedFromActual = ProjectedLapTime(actualSortedLaps, newAvg);

                results.Add(new CarRecommendationDto(
                    Rank: 0,
                    CarId: carId,
                    CarName: carName,
                    PercentileRank: percentileRank,
                    TopSharePercent: topShare,
                    SampleSize: total,
                    ProjectedLapSeconds: projectedFromActual ?? driverBest,
                    BestLapSeconds: driverBest));
            }
            else if (personalBestByCar.TryGetValue(carId, out var personalLap))
            {
                // Personal lap path — driver uploaded a lap for this car at this track but has
                // no SubsessionResult for it this week. Compute a fresh percentile against the
                // current field, which they join on the strength of that uploaded lap.
                var otherLaps = carField.Where(r => r.CustId != customerId).Select(r => r.BestLap).ToList();
                var total = FieldPercentile.FieldSize(otherLaps);
                var percentileRank = FieldPercentile.Rank(personalLap, otherLaps);
                var topShare = FieldPercentile.TopSharePercent(personalLap, otherLaps);

                var newAvg = RecordPercentileReading(
                    user, cachedPercentiles, existingCacheThisWeek,
                    carId, week.SeriesId, weekDbId, percentileRank, topShare, total, computedAt);

                var plSortedLaps = carField.Select(r => r.BestLap).ToList();
                var plProjected  = ProjectedLapTime(plSortedLaps, newAvg);
                if (plProjected is null) continue;

                results.Add(new CarRecommendationDto(
                    Rank: 0,
                    CarId: carId,
                    CarName: carName,
                    PercentileRank: percentileRank,
                    TopSharePercent: topShare,
                    SampleSize: total,
                    ProjectedLapSeconds: plProjected.Value,
                    BestLapSeconds: personalLap));
            }
            else
            {
                // Projected path — driver hasn't raced this car this week and has no personal lap.
                double historicalPercentile;
                if (cachedPercentiles.TryGetValue(carId, out var priorTuple))
                {
                    historicalPercentile = priorTuple.Sum / priorTuple.Count;
                }
                else if (!historicalPercentileByCar.TryGetValue(carId, out historicalPercentile))
                {
                    continue;
                }

                var sortedLaps = carField.Select(r => r.BestLap).ToList();
                var projectedLap = ProjectedLapTime(sortedLaps, historicalPercentile);
                if (projectedLap is null) continue;

                results.Add(new CarRecommendationDto(
                    Rank: 0,
                    CarId: carId,
                    CarName: carName,
                    PercentileRank: historicalPercentile,
                    // No placement: this is a running average of past readings, not a position in
                    // this week's Field — the driver has no lap in it to hold a position with.
                    TopSharePercent: null,
                    SampleSize: carField.Count,
                    ProjectedLapSeconds: projectedLap.Value,
                    BestLapSeconds: null));
            }
        }

        if (user is not null)
            await db.SaveChangesAsync(ct);

        return results
            .OrderBy(r => r.ProjectedLapSeconds)
            .Select((r, i) => r with { Rank = i + 1 })
            .ToList();
    }

    /// <summary>
    /// The caller's own percentile per car for a week, for the Week Detail "Your pct" column.
    /// Reuses <see cref="GetRecommendationsAsync"/> (incl. personal laps) and keeps only cars the
    /// caller actually has a lap for this week (<c>BestLapSeconds</c> set) — projected-only cars,
    /// whose percentile is a historical estimate rather than a real reading, are excluded.
    /// </summary>
    public async Task<List<WeekCarPercentileDto>> GetMyPercentilesAsync(
        int seriesId, int weekNumber, long customerId, CancellationToken ct = default)
    {
        var recs = await GetRecommendationsAsync(
            seriesId, weekNumber, customerId, includePersonalLaps: true, ct: ct);

        return recs
            .Where(r => r.BestLapSeconds is not null && r.TopSharePercent is not null)
            .Select(r => new WeekCarPercentileDto(r.CarId, r.PercentileRank, r.TopSharePercent!.Value))
            .ToList();
    }

    /// <summary>
    /// Records this week's percentile reading for one car: returns the per-(car, series) running
    /// average (folding in the reading) and upserts the cache row. The single shared path for both
    /// the actual-lap and personal-lap branches.
    /// </summary>
    private double RecordPercentileReading(
        ApplicationUser? user,
        Dictionary<int, (double Sum, int Count)> cachedPercentiles,
        Dictionary<int, CarPercentileResult> existingCacheThisWeek,
        int carId, int seriesId, Guid weekDbId,
        double percentileRank, int topSharePercent, int sampleSize, DateTimeOffset computedAt)
    {
        var prior = cachedPercentiles.TryGetValue(carId, out var p) ? ((double Sum, int Count)?)p : null;
        double? oldReading = existingCacheThisWeek.TryGetValue(carId, out var existing)
            ? existing.PercentileRank
            : null;

        var runningAverage = RunningAveragePercentile(percentileRank, prior, oldReading);
        UpsertPercentileCache(
            user, existingCacheThisWeek, carId, seriesId, weekDbId,
            percentileRank, topSharePercent, sampleSize, computedAt);
        return runningAverage;
    }

    /// <summary>
    /// Folds this week's <paramref name="percentileRank"/> reading into the driver's prior
    /// per-(car, series) running average.
    /// <list type="bullet">
    /// <item>No prior history (<paramref name="prior"/> is null) → the reading itself.</item>
    /// <item>A row already exists for this week (<paramref name="oldReading"/> is non-null) → swap the
    /// old reading out of the sum, keeping the count fixed.</item>
    /// <item>Otherwise → a fresh reading: add to the sum and grow the count.</item>
    /// </list>
    /// Pure so every branch (including the guarded zero-count case) is unit-tested directly.
    /// </summary>
    public static double RunningAveragePercentile(
        double percentileRank,
        (double Sum, int Count)? prior,
        double? oldReading)
    {
        if (prior is not { } p) return percentileRank;
        if (oldReading is { } old)
            return p.Count > 0 ? (p.Sum - old + percentileRank) / p.Count : percentileRank;
        return (p.Sum + percentileRank) / (p.Count + 1);
    }

    /// <summary>
    /// Upserts the driver's cached percentile row for this (user, car, series, week): mutates the
    /// existing row in place when present, else stages a new row for insert. No-op for an unlinked caller.
    /// </summary>
    private void UpsertPercentileCache(
        ApplicationUser? user,
        Dictionary<int, CarPercentileResult> existingCacheThisWeek,
        int carId, int seriesId, Guid weekDbId,
        double percentileRank, int topSharePercent, int sampleSize, DateTimeOffset computedAt)
    {
        if (user is null) return;

        if (existingCacheThisWeek.TryGetValue(carId, out var cached))
        {
            cached.PercentileRank  = percentileRank;
            cached.TopSharePercent = topSharePercent;
            cached.SampleSize      = sampleSize;
            cached.ComputedAt      = computedAt;
        }
        else
        {
            db.CarPercentileResults.Add(new CarPercentileResult
            {
                UserId          = user.Id,
                CarId           = carId,
                SeriesId        = seriesId,
                WeekId          = weekDbId,
                PercentileRank  = percentileRank,
                TopSharePercent = topSharePercent,
                SampleSize      = sampleSize,
                ComputedAt      = computedAt,
            });
        }
    }

    private static double? ProjectedLapTime(IReadOnlyList<double> sortedLaps, double percentileRank)
    {
        if (sortedLaps.Count == 0) return null;
        var n = sortedLaps.Count;
        var pos  = (n - 1) * (1.0 - percentileRank / 100.0);
        var low  = (int)Math.Floor(pos);
        var high = Math.Min((int)Math.Ceiling(pos), n - 1);
        return sortedLaps[low] + (pos - low) * (sortedLaps[high] - sortedLaps[low]);
    }
}
