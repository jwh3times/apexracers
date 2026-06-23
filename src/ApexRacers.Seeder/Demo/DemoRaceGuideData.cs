using ApexRacers.Api.Services;

namespace ApexRacers.Seeder.Demo;

/// <summary>Pure builder for the "race now" guide. Uses an always-live window (past start,
/// far-future end) so the sentinel-cached rows always pass RaceGuideService's now-filter.</summary>
public static class DemoRaceGuideData
{
    private static readonly DateTimeOffset Past = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FarFuture = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static List<RaceGuideCacheRow> Build(IReadOnlyList<int> seriesIds) =>
        seriesIds
            .Select((id, i) => new RaceGuideCacheRow(
                SeriesId: id,
                Start: Past,
                End: FarFuture,
                EntryCount: 18 + i * 3,
                RaceWeekNumber: 0))
            .ToList();
}
