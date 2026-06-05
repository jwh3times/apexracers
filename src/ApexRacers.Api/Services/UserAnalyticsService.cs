using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class UserAnalyticsService(AppDbContext db)
{
    public async Task<List<CarAnalyticsDto>> GetAnalyticsAsync(
        Guid userId, int? seriesId, CancellationToken ct = default)
    {
        var iracingId = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.IRacingCustomerId)
            .FirstOrDefaultAsync(ct);

        var rawResults = await db.CarPercentileResults
            .Where(r => r.UserId == userId)
            .Where(r => !seriesId.HasValue || r.Week.Season.SeriesId == seriesId.Value)
            .Select(r => new
            {
                r.CarId,
                CarName = r.Car.Name,
                SeriesId = r.Week.Season.SeriesId,
                SeriesName = r.Week.Season.Series.Name,
                r.WeekId,
                WeekNumber = r.Week.WeekNumber,
                TrackName = r.Week.Track.Name,
                ConfigName = r.Week.Track.ConfigName,
                TrackId = r.Week.TrackId,
                r.PercentileRank,
                r.SampleSize,
                r.ComputedAt,
            })
            .ToListAsync(ct);

        if (rawResults.Count == 0) return [];

        var groups = rawResults.GroupBy(r => (r.CarId, r.SeriesId)).ToList();

        var bestWeekIds = groups
            .Select(g => g.MaxBy(r => r.PercentileRank)!.WeekId)
            .Distinct()
            .ToList();

        // Field median: best lap per driver per (week, car) from race results
        var medianLapsRaw = await db.SubsessionResults
            .Where(r => r.Subsession.WeekId.HasValue
                     && bestWeekIds.Contains(r.Subsession.WeekId!.Value)
                     && r.BestLapSeconds > 0)
            .GroupBy(r => new { r.CustId, r.CarId, WeekId = r.Subsession.WeekId!.Value })
            .Select(g => new { g.Key.CarId, g.Key.WeekId, BestLap = g.Min(r => r.BestLapSeconds) })
            .ToListAsync(ct);

        var medianLapsByWeekCar = medianLapsRaw
            .GroupBy(l => (l.CarId, l.WeekId))
            .ToDictionary(
                g => g.Key,
                g => g.Select(l => l.BestLap).OrderBy(t => t).ToList());

        // Driver's race laps for personal best
        var allWeekIds = rawResults.Select(r => r.WeekId).Distinct().ToList();
        Dictionary<(int CarId, Guid WeekId), double> driverRaceBests = new();

        if (iracingId is > 0)
        {
            var driverRaceRows = await db.SubsessionResults
                .Where(r => r.Subsession.WeekId.HasValue
                         && allWeekIds.Contains(r.Subsession.WeekId!.Value)
                         && r.CustId == iracingId.Value
                         && r.BestLapSeconds > 0)
                .GroupBy(r => new { r.CarId, WeekId = r.Subsession.WeekId!.Value })
                .Select(g => new { g.Key.CarId, g.Key.WeekId, BestLap = g.Min(r => r.BestLapSeconds) })
                .ToListAsync(ct);

            foreach (var row in driverRaceRows)
                driverRaceBests[(row.CarId, row.WeekId)] = row.BestLap;

            // Also consider PersonalLap data for personal best
            var trackIds = rawResults.Select(r => r.TrackId).Distinct().ToList();
            var carIds = rawResults.Select(r => r.CarId).Distinct().ToList();

            var personalLapBests = await db.PersonalLaps
                .Where(p => p.UserId == userId && p.IsValidLap
                         && carIds.Contains(p.CarId) && trackIds.Contains(p.TrackId))
                .GroupBy(p => new { p.CarId, p.TrackId })
                .Select(g => new { g.Key.CarId, g.Key.TrackId, BestLap = g.Min(p => p.LapTimeSeconds) })
                .ToListAsync(ct);

            // Map personal lap bests onto week entries by (carId, trackId)
            var personalLapByCarTrack = personalLapBests.ToDictionary(p => (p.CarId, p.TrackId));

            foreach (var result in rawResults)
            {
                if (!personalLapByCarTrack.TryGetValue((result.CarId, result.TrackId), out var plap)) continue;
                var key = (result.CarId, result.WeekId);
                if (!driverRaceBests.TryGetValue(key, out var raceBest) || plap.BestLap < raceBest)
                    driverRaceBests[key] = plap.BestLap;
            }
        }

        return groups
            .Select(group =>
            {
                var ordered = group.OrderBy(r => r.ComputedAt).ToList();
                var bestWeek = ordered.MaxBy(r => r.PercentileRank)!;
                var latest = ordered[^1];

                var carPersonalBests = ordered
                    .Select(r => driverRaceBests.TryGetValue((r.CarId, r.WeekId), out var b) ? (double?)b : null)
                    .Where(b => b is not null)
                    .ToList();

                double? personalBest = carPersonalBests.Count > 0 ? carPersonalBests.Min()! : null;
                int totalWeeks = carPersonalBests.Count;

                double? median = null;
                if (medianLapsByWeekCar.TryGetValue((bestWeek.CarId, bestWeek.WeekId), out var weekLaps) && weekLaps.Count > 0)
                {
                    var mid = weekLaps.Count / 2;
                    median = weekLaps.Count % 2 == 0
                        ? (weekLaps[mid - 1] + weekLaps[mid]) / 2.0
                        : weekLaps[mid];
                }

                var history = ordered
                    .Select(r => new WeeklyPercentileDto(
                        r.WeekNumber, r.TrackName, r.ConfigName,
                        r.PercentileRank, r.SampleSize, r.ComputedAt))
                    .ToList();

                return new CarAnalyticsDto(
                    group.Key.CarId,
                    group.First().CarName,
                    group.Key.SeriesId,
                    group.First().SeriesName,
                    latest.PercentileRank,
                    group.Max(r => r.PercentileRank),
                    personalBest,
                    median,
                    totalWeeks,
                    history);
            })
            .OrderByDescending(a => a.BestPercentileRank)
            .ToList();
    }
}
