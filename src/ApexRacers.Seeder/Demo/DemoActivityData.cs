using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;

namespace ApexRacers.Seeder.Demo;

/// <summary>Pure builders for the demo driver's trophy case (awards) and recent-race history.</summary>
public static class DemoActivityData
{
    public static List<AwardDto> BuildAwards(long custId) =>
    [
        new(1, "Race Winner", "Win an official race", "Racing", 18,
            DemoCache.RefDate.AddDays(-10), null, "#1f6feb", 18, 1),
        new(2, "Clean Driver", "Finish a race with zero incidents", "Safety", 42,
            DemoCache.RefDate.AddDays(-25), null, "#2ea043", 42, 1),
        new(3, "Podium Finisher", "Finish in the top 3", "Racing", 64,
            DemoCache.RefDate.AddDays(-60), null, "#8957e5", 64, 1),
    ];

    // Synthetic negative subsession ids so they never collide with real ingested races and are
    // removed by purge_demo_data.sql (Id < 0). Car 132 (BMW M4 GT3) and track 47 (Laguna Seca)
    // are in the seeded catalog, so the row's configuration resolves and its track link lands.
    public static List<RecentRaceCacheRow> BuildRecentRaces(long custId) =>
        Enumerable.Range(0, 6)
            .Select(i => new RecentRaceCacheRow(
                SubsessionId: -900_000 - i,
                SessionStartTime: DemoCache.RefDate.AddDays(-i * 3),
                SeriesName: "GT3 Challenge Fixed",
                TrackId: 47,
                TrackName: "Laguna Seca",
                CarId: 132,
                StartPosition: 5 + (i % 4),
                FinishPosition: 3 + (i % 3),
                Incidents: i % 5,
                IRatingDelta: (i % 2 == 0 ? 1 : -1) * (20 + i * 3),
                SrDelta: (i % 2 == 0 ? 0.08 : -0.04),
                StrengthOfField: 2200 + i * 25,
                Points: 120 - i * 5))
            .ToList();
}
