using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Core.PercentileHistory;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class CarRecommendationService(AppDbContext db, SubjectDriverContext? subjects = null,
    PercentileRefresh? refresh = null)
{
    // Direct Driver lookup is deliberately transient. Only the explicit authenticated-User
    // overload can refresh personal history; claims never recover a User identity.
    public Task<List<CarRecommendationDto>> GetRecommendationsAsync(int seriesId, int raceWeekIndex,
        long customerId, PersonalBestEvidence evidence, CancellationToken ct = default) =>
        GetRecommendationsAsync(seriesId, raceWeekIndex, customerId, evidence, null, ct);

    public async Task<List<CarRecommendationDto>> GetRecommendationsAsync(int seriesId, int raceWeekIndex,
        long customerId, PersonalBestEvidence evidence, Guid? callerUserId, CancellationToken ct = default)
    {
        var seasonId = await db.CurrentSeasonIdAsync(seriesId, ct);
        if (seasonId is null) return [];
        var week = await db.Weeks.InSeason(seasonId.Value, raceWeekIndex)
            .Select(w => new { w.Id, w.TrackId, w.StartDate }).FirstOrDefaultAsync(ct);
        if (week is null) return [];

        // All current Fields in one query. Projection and recommendation order remain local
        // presentation policy; history eligibility and persistence belong to the refresh module.
        var fieldRows = await db.SubsessionResults
            .Where(r => r.Subsession.WeekId == week.Id && r.Subsession.TrackId == week.TrackId && r.BestLapSeconds > 0)
            .GroupBy(r => new { r.CarId, r.CustId, CarName = r.Car.Name })
            .Select(g => new { g.Key.CarId, g.Key.CustId, g.Key.CarName, BestLap = g.Min(r => r.BestLapSeconds) })
            .ToListAsync(ct);
        var carIds = fieldRows.Select(r => r.CarId).Distinct().ToArray();
        var subjectContext = subjects ?? new SubjectDriverContext(db, new FeatureFlagEligibility(db));
        Guid? refreshUserId = null;
        if (callerUserId is { } authenticatedUserId && db.Provenance == DataProvenance.Demo
            && (await subjectContext.GetSubjectDriverAsync(authenticatedUserId, ct)) is
                { Provenance: DataProvenance.Demo } subject && subject.CustomerId == customerId)
            refreshUserId = authenticatedUserId;
        // Include Cars from existing classified rows so a now-empty Field/PB is removed as well.
        if (refreshUserId is { } ownerUserId)
            carIds = carIds.Concat(await db.ClassifiedCarPercentileResults.IgnoreQueryFilters()
                .Where(r => r.Provenance == DataProvenance.Demo && r.UserId == ownerUserId && r.SeriesId == seriesId
                    && r.OwnerKind == PercentileOwnerKind.DemoSubject && r.CustomerId == DemoData.DriverCustId
                    && r.SelectionKey == evidence.Selection.Key)
                .Select(r => r.CarId).Distinct().ToListAsync(ct)).Distinct().ToArray();
        if (carIds.Length == 0) return [];
        var weeks = await db.Weeks.Where(w => w.Season.SeriesId == seriesId)
            .Select(w => new { w.Id, w.TrackId, w.StartDate }).ToListAsync(ct);
        var weekIds = weeks.Select(w => w.Id).ToArray();
        IReadOnlyList<RefreshedPercentile> readings = [];
        var refreshedOwner = false;
        if (refreshUserId is { } userId)
        {
            var batch = await (refresh ?? new PercentileRefresh(db, TimeProvider.System,
                new UnavailableDriverEnforcementJournal())).RefreshDemoAsync(
                    userId, evidence.Selection, seriesId, weekIds, carIds, ct);
            readings = batch.Readings;
            refreshedOwner = true;
        }
        var byCarWeek = readings.ToDictionary(r => (r.CarId, r.WeekId));
        var requestedStartDate = readings.FirstOrDefault(r => r.WeekId == week.Id)?.WeekStartDate;
        var expected = readings.Where(r => requestedStartDate is { } through && r.WeekStartDate <= through
                && r.IsPresentable && r.PersonalBest is not null)
            .GroupBy(r => r.CarId).ToDictionary(g => g.Key, g => (double?)g.Average(r => r.PercentileRank!.Value));
        var results = new List<CarRecommendationDto>();
        foreach (var group in fieldRows.GroupBy(r => r.CarId))
        {
            var carId = group.Key;
            byCarWeek.TryGetValue((carId, week.Id), out var reading);
            if (refreshedOwner && reading is null) continue;
            var raceLap = group.Where(r => r.CustId == customerId).Select(r => (double?)r.BestLap).FirstOrDefault();
            var best = reading is null ? PersonalBest.Select(raceLap, null) : reading.PersonalBest;
            var average = expected.GetValueOrDefault(carId);
            var others = reading?.OtherDriversLaps.ToList()
                ?? group.Where(r => r.CustId != customerId).Select(r => r.BestLap).ToList();
            double? rank = reading is not null ? reading.PercentileRank
                : best is { } pb ? FieldPercentile.Rank(pb.LapSeconds, others) : null;
            int? size = reading is not null ? reading.FieldSize
                : best is null ? null : FieldPercentile.FieldSize(others);
            int? top = reading is not null ? reading.TopSharePercent
                : best is { } personalBest ? FieldPercentile.TopSharePercent(personalBest.LapSeconds, others) : null;
            var projectionField = reading is null
                ? group.Select(r => r.BestLap).OrderBy(x => x).ToList()
                : (best is { } currentBest ? others.Append(currentBest.LapSeconds) : others).OrderBy(x => x).ToList();
            var projection = ProjectedLapTime(projectionField, average ?? rank);
            if (projection is null) continue;
            results.Add(new CarRecommendationDto(0, carId, group.First().CarName, rank, average, top, size,
                size is { } fieldSize && FieldPercentile.IsPresentable(fieldSize), projection.Value,
                best?.LapSeconds, best?.Evidence));
        }
        return results.OrderBy(r => r.ProjectedLapSeconds)
            .Select((r, i) => r with { RecommendationRank = i + 1 }).ToList();
    }

    public Task<List<WeekCarPercentileDto>> GetMyPercentilesAsync(int seriesId, int raceWeekIndex,
        long customerId, PersonalBestEvidence evidence, CancellationToken ct = default) =>
        GetMyPercentilesAsync(seriesId, raceWeekIndex, customerId, evidence, null, ct);

    public async Task<List<WeekCarPercentileDto>> GetMyPercentilesAsync(int seriesId, int raceWeekIndex,
        long customerId, PersonalBestEvidence evidence, Guid? callerUserId, CancellationToken ct = default)
    {
        var recommendations = await GetRecommendationsAsync(seriesId, raceWeekIndex, customerId, evidence, callerUserId, ct);
        return recommendations.Where(r => r.BestLapSeconds is not null && r.TopSharePercent is not null && r.IsPercentilePresentable)
            .Select(r => new WeekCarPercentileDto(r.CarId, r.PercentileRank!.Value, r.TopSharePercent!.Value, r.BestLapEvidence!.Value))
            .ToList();
    }

    // Retained pure compatibility helper; runtime history averages distinct current eligible
    // Weeks from the shared refresh rather than folding a cached row into its own aggregate.
    public static double RunningAveragePercentile(double percentileRank, (double Sum, int Count)? prior, double? oldReading)
    {
        if (prior is not { } p) return percentileRank;
        if (oldReading is { } old) return p.Count > 0 ? (p.Sum - old + percentileRank) / p.Count : percentileRank;
        return (p.Sum + percentileRank) / (p.Count + 1);
    }

    private static double? ProjectedLapTime(IReadOnlyList<double> sortedLaps, double? percentileRank)
    {
        if (sortedLaps.Count == 0 || percentileRank is null) return null;
        var pos = (sortedLaps.Count - 1) * (1.0 - percentileRank.Value / 100.0);
        var low = (int)Math.Floor(pos);
        var high = Math.Min((int)Math.Ceiling(pos), sortedLaps.Count - 1);
        return sortedLaps[low] + (pos - low) * (sortedLaps[high] - sortedLaps[low]);
    }
}
