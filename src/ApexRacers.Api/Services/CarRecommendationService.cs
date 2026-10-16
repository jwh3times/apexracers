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
        var subjectContext = subjects ?? new SubjectDriverContext(db, new FeatureFlagEligibility(db));
        if (callerUserId is { } userId && db.Provenance == DataProvenance.Demo
            && (await subjectContext.GetSubjectDriverAsync(userId, ct)) is
                { Provenance: DataProvenance.Demo } subject && subject.CustomerId == customerId)
        {
            var snapshot = await (refresh ?? new PercentileRefresh(db, TimeProvider.System,
                new UnavailableDriverEnforcementJournal())).ReadDemoHistoryAsync(userId, evidence.Selection, seriesId, ct);
            var week = PercentileHistoryView.CurrentWeek(snapshot, seriesId, raceWeekIndex);
            if (week is null) return [];
            var averages = snapshot.Readings.Where(r => r.SeriesId == seriesId && r.WeekStartDate <= week.StartDate
                    && r.IsPresentable && r.PersonalBest is not null)
                .GroupBy(r => r.CarId).ToDictionary(g => g.Key, g => (double?)g.Average(r => r.PercentileRank!.Value));
            var recommendations = new List<CarRecommendationDto>();
            foreach (var reading in snapshot.Readings.Where(r => r.WeekId == week.WeekId))
            {
                var best = reading.PersonalBest;
                var average = averages.GetValueOrDefault(reading.CarId);
                var field = (best is { } pb ? reading.OtherDriversLaps.Append(pb.LapSeconds) : reading.OtherDriversLaps)
                    .Order().ToList();
                var projection = ProjectedLapTime(field, average ?? reading.PercentileRank);
                if (projection is null) continue;
                recommendations.Add(new CarRecommendationDto(0, reading.CarId, snapshot.CarNames[reading.CarId],
                    reading.PercentileRank, average, reading.TopSharePercent, reading.FieldSize, reading.IsPresentable,
                    projection.Value, best?.LapSeconds, best?.Evidence));
            }
            return RankRecommendations(recommendations);
        }

        // Arbitrary Driver lookup is transient and has no owner history or private-upload inputs.
        var seasonId = await db.CurrentSeasonIdAsync(seriesId, ct);
        if (seasonId is null) return [];
        var lookupWeek = await db.Weeks.InSeason(seasonId.Value, raceWeekIndex)
            .Select(w => new { w.Id, w.TrackId }).FirstOrDefaultAsync(ct);
        if (lookupWeek is null) return [];
        var rows = await db.SubsessionResults.Where(r => r.Subsession.WeekId == lookupWeek.Id
                && r.Subsession.TrackId == lookupWeek.TrackId && r.BestLapSeconds > 0)
            .GroupBy(r => new { r.CarId, r.CustId, CarName = r.Car.Name })
            .Select(g => new { g.Key.CarId, g.Key.CustId, g.Key.CarName, BestLap = g.Min(r => r.BestLapSeconds) })
            .ToListAsync(ct);
        var transient = new List<CarRecommendationDto>();
        foreach (var group in rows.GroupBy(r => r.CarId))
        {
            var best = group.Where(r => r.CustId == customerId).Select(r => (double?)r.BestLap).FirstOrDefault();
            if (best is null) continue;
            var others = group.Where(r => r.CustId != customerId).Select(r => r.BestLap).ToList();
            var rank = FieldPercentile.Rank(best.Value, others);
            var size = FieldPercentile.FieldSize(others);
            var projection = ProjectedLapTime(group.Select(r => r.BestLap).Order().ToList(), rank)!.Value;
            transient.Add(new CarRecommendationDto(0, group.Key, group.First().CarName, rank, null,
                FieldPercentile.TopSharePercent(best.Value, others), size, FieldPercentile.IsPresentable(size),
                projection, best.Value, LapEvidence.RaceLap));
        }
        return RankRecommendations(transient);
    }

    private static List<CarRecommendationDto> RankRecommendations(IEnumerable<CarRecommendationDto> values) =>
        values.OrderBy(r => r.ProjectedLapSeconds).ThenBy(r => r.CarId)
            .Select((r, i) => r with { RecommendationRank = i + 1 }).ToList();

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
