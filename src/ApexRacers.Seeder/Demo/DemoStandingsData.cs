using ApexRacers.Api.Dtos;
using ApexRacers.Core;

namespace ApexRacers.Seeder.Demo;

/// <summary>Pure builders for championship / Time Trial / qualifying standings. Each list places the
/// demo driver in the field (with a Division) so the page can show "your division" + highlight the row.</summary>
public static class DemoStandingsData
{
    private const int FieldSize = 40;

    // The demo driver sits 4th; everyone else is a distinct synthetic standings-pool cust id.
    private static long CustAt(int seasonId, int classId, int index) =>
        index == 3 ? DemoData.DriverCustId : 300_000L + seasonId * 100 + classId + index;

    private static string NameAt(long custId) =>
        custId == DemoData.DriverCustId ? "Demo Driver" : $"Driver {custId}";

    public static List<SeasonStandingDto> BuildStandings(int seasonId, int classId) =>
        Enumerable.Range(0, FieldSize)
            .Select(i => new SeasonStandingDto(
                Standing: i + 1,
                CustomerId: CustAt(seasonId, classId, i),
                DriverName: NameAt(CustAt(seasonId, classId, i)),
                Division: 1 + i % 5,
                Starts: 24, Wins: Math.Max(0, 10 - i), Top5: Math.Max(0, 18 - i), Poles: Math.Max(0, 6 - i / 2),
                Points: 3200 - i * 60,
                AvgFinishPosition: 3.0 + i * 0.3,
                Incidents: 40 + i))
            .ToList();

    public static List<SeasonTtStandingDto> BuildTtStandings(int seasonId, int classId) =>
        Enumerable.Range(0, FieldSize)
            .Select(i => new SeasonTtStandingDto(
                Standing: i + 1,
                CustomerId: CustAt(seasonId, classId, i),
                DriverName: NameAt(CustAt(seasonId, classId, i)),
                Division: 1 + i % 5,
                TtRating: 2400 - i * 35,
                Starts: 20, Wins: Math.Max(0, 9 - i), Top5: Math.Max(0, 16 - i), Poles: Math.Max(0, 5 - i / 2),
                Points: 2800 - i * 55,
                AvgFinishPosition: 3.5 + i * 0.3,
                Incidents: 30 + i))
            .ToList();

    public static List<SeasonQualifyResultDto> BuildQualify(int seasonId, int classId, int week) =>
        Enumerable.Range(0, FieldSize)
            .Select(i => new SeasonQualifyResultDto(
                Standing: i + 1,
                CustomerId: CustAt(seasonId, classId, i),
                DriverName: NameAt(CustAt(seasonId, classId, i)),
                Division: 1 + i % 5,
                IRating: 3000 - i * 45,
                BestQualLapSeconds: 84.0 + i * 0.12,
                Week: week))
            .ToList();
}
