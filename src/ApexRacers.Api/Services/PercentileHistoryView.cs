using ApexRacers.Core;
using ApexRacers.Core.PercentileHistory;
using ApexRacers.Data;

namespace ApexRacers.Api.Services;

internal static class PercentileHistoryView
{
    internal static PercentileHistoryWeekMetadata? CurrentWeek(PercentileHistorySnapshot snapshot,
        int seriesId, int raceWeekIndex)
    {
        var starts = snapshot.Weeks.GroupBy(w => w.SeasonId).ToDictionary(g => g.Key, g => g.Min(w => w.StartDate));
        var seasonId = SeasonCalendar.CurrentSeasonId(snapshot.Seasons.Where(s => s.SeriesId == seriesId)
            .Select(s => new SeasonStart(s.SeasonId, starts.TryGetValue(s.SeasonId, out var start) ? start : null,
                s.Year, s.Quarter, s.Active)), DateOnly.FromDateTime(DateTime.UtcNow));
        return snapshot.Weeks.SingleOrDefault(w => w.SeasonId == seasonId && w.RaceWeekIndex == raceWeekIndex);
    }
}
