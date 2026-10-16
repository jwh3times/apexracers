using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Data;

namespace ApexRacers.Api.Services;

/// <summary>Presentation of one coherent snapshot. Personal dispatch retains its opaque source
/// witness inside the protected executor; these response records never establish permission.</summary>
public static class PercentileAnalyticsProjection
{
    public static List<CarAnalyticsDto> Build(PercentileHistorySnapshot snapshot)
    {
        var weeks = snapshot.Weeks.ToDictionary(w => w.WeekId);
        return snapshot.Readings.Where(r => r.IsPresentable && r.PersonalBest is not null)
            .GroupBy(r => (r.CarId, r.SeriesId))
            .Select(group =>
            {
                var ordered = group.OrderBy(r => r.WeekStartDate).ThenBy(r => r.WeekId).ToList();
                var latest = ordered[^1];
                var best = ordered.MaxBy(r => r.PercentileRank)!;
                var fastest = ordered.MinBy(r => r.PersonalBest!.Value.LapSeconds)!.PersonalBest!.Value;
                var history = ordered.Select(r =>
                {
                    var week = weeks[r.WeekId];
                    var personalBest = r.PersonalBest!.Value;
                    var field = r.OtherDriversLaps.Append(personalBest.LapSeconds).Order().ToList();
                    return new WeeklyPercentileDto(week.RaceWeekIndex, week.TrackName,
                        ConfigurationName.NullIfAbsent(week.TrackConfigName), r.PercentileRank!.Value,
                        r.TopSharePercent!.Value, r.FieldSize!.Value, r.ComputedAt, r.WeekId, r.WeekStartDate,
                        week.SeasonId, week.TrackId, personalBest.LapSeconds, personalBest.Evidence,
                        r.UploadedSessionType, FieldPercentile.MedianOfSorted(field), r.Selection.Key, r.Selection.PolicyVersion);
                }).ToList();
                return new CarAnalyticsDto(group.Key.CarId, snapshot.CarNames[group.Key.CarId], group.Key.SeriesId,
                    weeks[latest.WeekId].SeriesName, latest.PercentileRank!.Value, latest.TopSharePercent!.Value,
                    best.PercentileRank!.Value, best.TopSharePercent!.Value, fastest.LapSeconds, fastest.Evidence,
                    history.Single(r => r.WeekId == best.WeekId).MedianLapSeconds, history.Count, history,
                    latest.CustomerId, latest.Selection.Key, latest.Selection.PolicyVersion);
            }).OrderByDescending(r => r.BestPercentileRank).ThenBy(r => r.SeriesId).ThenBy(r => r.CarId).ToList();
    }
}
