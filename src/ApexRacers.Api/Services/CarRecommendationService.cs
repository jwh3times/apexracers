using ApexRacers.Api.Dtos;
using ApexRacers.Core.Models;
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
        var week = await db.Weeks
            .Where(w => w.WeekNumber == weekNumber && w.Season.SeriesId == seriesId && w.Season.Active)
            .OrderByDescending(w => w.Season.Year).ThenByDescending(w => w.Season.Quarter)
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
                    .Select(g => new { g.Key.CarId, g.Key.WeekId, BestLap = g.Min(r => r.BestLapSeconds) })
                    .ToListAsync(ct);

                var fieldByCarWeek = fieldHistorical
                    .GroupBy(r => (r.CarId, r.WeekId))
                    .ToDictionary(g => g.Key, g => g.Select(r => r.BestLap).ToList());

                foreach (var carGroup in driverHistorical.GroupBy(r => r.CarId))
                {
                    double? best = null;
                    foreach (var entry in carGroup)
                    {
                        if (!fieldByCarWeek.TryGetValue((entry.CarId, entry.WeekId), out var fieldLaps)) continue;
                        var total = fieldLaps.Count;
                        var slower = fieldLaps.Count(t => t > entry.BestLap);
                        var pct = total > 1 ? slower * 100.0 / (total - 1) : 100.0;
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

                var total = carField.Count;
                var slowerCount = carField.Count(r => r.CustId != customerId && r.BestLap > driverBest);
                var percentileRank = total > 1 ? slowerCount * 100.0 / (total - 1) : 100.0;

                // Compute the running average percentile that will include this week's reading.
                // cachedPercentiles holds (Sum, Count) of all prior rows for this (user, car, series).
                double newAvg;
                if (cachedPercentiles.TryGetValue(carId, out var prior))
                {
                    if (existingCacheThisWeek.TryGetValue(carId, out _))
                    {
                        // Updating an existing week row: swap out old value, keep count the same.
                        var oldReading = existingCacheThisWeek[carId].PercentileRank;
                        newAvg = prior.Count > 0
                            ? (prior.Sum - oldReading + percentileRank) / prior.Count
                            : percentileRank;
                    }
                    else
                    {
                        // New week row: add to sum and increment count.
                        newAvg = (prior.Sum + percentileRank) / (prior.Count + 1);
                    }
                }
                else
                {
                    // No prior history at all for this car/series — first ever reading.
                    newAvg = percentileRank;
                }

                if (user is not null)
                {
                    if (existingCacheThisWeek.TryGetValue(carId, out var cached))
                    {
                        cached.PercentileRank = percentileRank;
                        cached.SampleSize     = total;
                        cached.ComputedAt     = computedAt;
                    }
                    else
                    {
                        db.CarPercentileResults.Add(new CarPercentileResult
                        {
                            UserId         = user.Id,
                            CarId          = carId,
                            SeriesId       = week.SeriesId,
                            WeekId         = weekDbId,
                            PercentileRank = percentileRank,
                            SampleSize     = total,
                            ComputedAt     = computedAt,
                        });
                    }
                }

                var actualSortedLaps = carField.Select(r => r.BestLap).ToList();
                var projectedFromActual = ProjectedLapTime(actualSortedLaps, newAvg);

                results.Add(new CarRecommendationDto(
                    Rank: 0,
                    CarId: carId,
                    CarName: carName,
                    PercentileRank: percentileRank,
                    SampleSize: total,
                    ProjectedLapSeconds: projectedFromActual ?? driverBest,
                    BestLapSeconds: driverBest));
            }
            else if (personalBestByCar.TryGetValue(carId, out var personalLap))
            {
                // Personal lap path — driver uploaded a lap for this car at this track but has
                // no SubsessionResult for it this week. Compute a fresh percentile against the
                // current field; user is not in the race, so denominator is the full field size.
                var total = carField.Count;
                var slowerCount = carField.Count(r => r.BestLap > personalLap);
                var percentileRank = total > 0 ? slowerCount * 100.0 / total : 100.0;

                double newAvg;
                if (cachedPercentiles.TryGetValue(carId, out var prior2))
                {
                    if (existingCacheThisWeek.TryGetValue(carId, out _))
                    {
                        var oldReading = existingCacheThisWeek[carId].PercentileRank;
                        newAvg = prior2.Count > 0
                            ? (prior2.Sum - oldReading + percentileRank) / prior2.Count
                            : percentileRank;
                    }
                    else
                    {
                        newAvg = (prior2.Sum + percentileRank) / (prior2.Count + 1);
                    }
                }
                else
                {
                    newAvg = percentileRank;
                }

                if (user is not null)
                {
                    if (existingCacheThisWeek.TryGetValue(carId, out var cached2))
                    {
                        cached2.PercentileRank = percentileRank;
                        cached2.SampleSize     = total;
                        cached2.ComputedAt     = computedAt;
                    }
                    else
                    {
                        db.CarPercentileResults.Add(new CarPercentileResult
                        {
                            UserId         = user.Id,
                            CarId          = carId,
                            SeriesId       = week.SeriesId,
                            WeekId         = weekDbId,
                            PercentileRank = percentileRank,
                            SampleSize     = total,
                            ComputedAt     = computedAt,
                        });
                    }
                }

                var plSortedLaps = carField.Select(r => r.BestLap).ToList();
                var plProjected  = ProjectedLapTime(plSortedLaps, newAvg);
                if (plProjected is null) continue;

                results.Add(new CarRecommendationDto(
                    Rank: 0,
                    CarId: carId,
                    CarName: carName,
                    PercentileRank: percentileRank,
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
