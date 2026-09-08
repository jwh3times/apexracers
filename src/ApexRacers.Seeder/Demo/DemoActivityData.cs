using ApexRacers.Api.Dtos;
namespace ApexRacers.Seeder.Demo;

/// <summary>Pure builder for the demo driver's trophy case.</summary>
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
}
