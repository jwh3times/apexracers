using ApexRacers.Core;
using ApexRacers.Data;
using Aydsko.iRacingData.Member;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Seeder.Demo;

/// <summary>
/// Seeds the synthetic demo dataset: ExternalDataCaches rows under each service's exact keys
/// (so the CachedIRacingClient endpoints serve hits) plus the persisted BoP/weather gaps.
/// Reads the freshly-seeded catalog/seasons from the same DB the main seeder just populated.
/// </summary>
public sealed class DemoCacheSeeder(AppDbContext db)
{
    private static readonly int IRatingChart = (int)MemberChartType.IRating;

    /// <summary>profile/career/chart for demo driver + rival; summary/recap for the demo driver only.</summary>
    public async Task SeedMembersAsync(CancellationToken ct)
    {
        foreach (var custId in new[] { DemoData.DriverCustId, DemoData.RivalCustId })
        {
            await DemoCache.UpsertAsync(db, $"profile:{custId}", DemoMemberData.BuildProfile(custId), ct);
            await DemoCache.UpsertAsync(db, $"career:{custId}", DemoMemberData.BuildCareer(custId), ct);
            foreach (var cat in DemoMemberData.Categories)
                await DemoCache.UpsertAsync(
                    db, $"chart:{custId}:{cat.Id}:{IRatingChart}", DemoMemberData.BuildChart(custId, cat.Id), ct);
        }

        // Profile-stats extras only the demo driver's /profile reads.
        await DemoCache.UpsertAsync(db, $"summary:{DemoData.DriverCustId}", DemoMemberData.BuildSummary(DemoData.DriverCustId), ct);
        await DemoCache.UpsertAsync(db, $"recap:{DemoData.DriverCustId}", DemoMemberData.BuildRecap(DemoData.DriverCustId), ct);
    }

    /// <summary>awards + recent races for the authenticated demo driver only.</summary>
    public async Task SeedActivityAsync(CancellationToken ct)
    {
        await DemoCache.UpsertAsync(db, $"awards:{DemoData.DriverCustId}", DemoActivityData.BuildAwards(DemoData.DriverCustId), ct);
        await DemoCache.UpsertAsync(db, $"recent:{DemoData.DriverCustId}", DemoActivityData.BuildRecentRaces(DemoData.DriverCustId), ct);
    }

    /// <summary>leaderboard:1..6 (the API allows category 1-6; default 5).</summary>
    public async Task SeedLeaderboardsAsync(CancellationToken ct)
    {
        for (var categoryId = 1; categoryId <= 6; categoryId++)
            await DemoCache.UpsertAsync(db, $"leaderboard:{categoryId}", DemoLeaderboardData.Build(categoryId), ct);
    }

    /// <summary>standings/tt-standings per active season × car class; qual per season × class × week.</summary>
    public async Task SeedStandingsAsync(CancellationToken ct)
    {
        var activeSeasonIds = await db.Seasons.Where(s => s.Active).Select(s => s.Id).ToListAsync(ct);

        foreach (var seasonId in activeSeasonIds)
        {
            var classIds = await db.SeasonCarClasses
                .Where(c => c.SeasonId == seasonId).Select(c => c.CarClassId).ToListAsync(ct);
            var weeks = await db.Weeks
                .Where(w => w.SeasonId == seasonId).Select(w => w.WeekNumber).ToListAsync(ct);

            foreach (var classId in classIds)
            {
                await DemoCache.UpsertAsync(db, $"standings:{seasonId}:{classId}", DemoStandingsData.BuildStandings(seasonId, classId), ct);
                await DemoCache.UpsertAsync(db, $"tt-standings:{seasonId}:{classId}", DemoStandingsData.BuildTtStandings(seasonId, classId), ct);
                foreach (var week in weeks)
                    await DemoCache.UpsertAsync(db, $"qual:{seasonId}:{classId}:{week}", DemoStandingsData.BuildQualify(seasonId, classId, week), ct);
            }
        }
    }
}
