using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
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

    /// <summary>race-guide for the active series (always-live window — see DemoRaceGuideData).</summary>
    public async Task SeedRaceGuideAsync(CancellationToken ct)
    {
        var seriesIds = await db.Seasons.Where(s => s.Active).Select(s => s.SeriesId).Distinct().ToListAsync(ct);
        await DemoCache.UpsertAsync(db, "race-guide", DemoRaceGuideData.Build(seriesIds), ct);
    }

    /// <summary>Fills the §6 persisted gaps: per-week weather + per-car BoP for active seasons.
    /// Only fills weeks/cars that don't already have data (idempotent; never clobbers real data).</summary>
    public async Task SeedBopAndWeatherAsync(CancellationToken ct)
    {
        var activeSeasonIds = await db.Seasons.Where(s => s.Active).Select(s => s.Id).ToListAsync(ct);

        foreach (var seasonId in activeSeasonIds)
        {
            var weeks = await db.Weeks.Where(w => w.SeasonId == seasonId).ToListAsync(ct);
            var carIds = await db.SeasonCars.Where(c => c.SeasonId == seasonId).Select(c => c.CarId).ToListAsync(ct);

            foreach (var week in weeks)
            {
                if (string.IsNullOrEmpty(week.WeatherSummaryJson))
                    week.WeatherSummaryJson = DemoScheduleData.WeatherJson();

                foreach (var carId in carIds)
                {
                    var exists = await db.SeasonCarBops
                        .AnyAsync(b => b.SeasonId == seasonId && b.WeekNumber == week.WeekNumber && b.CarId == carId, ct);
                    if (!exists)
                        db.SeasonCarBops.Add(DemoScheduleData.BuildBop(seasonId, week.WeekNumber, carId));
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>wr:{carId}:{trackId} = the fastest synthetic field lap for that car+track × 0.98
    /// (a realistic WR gap). Reads the synthetic results (negative subsession ids).</summary>
    public async Task SeedWorldRecordsAsync(CancellationToken ct)
    {
        // Project entity columns server-side (nav join is fine), then group in memory (SQLite-safe).
        var rows = await db.SubsessionResults
            .Where(r => r.SubsessionId < 0 && r.BestLapSeconds > 0)
            .Select(r => new { r.CarId, r.Subsession.TrackId, r.BestLapSeconds })
            .ToListAsync(ct);

        var combos = rows
            .GroupBy(r => (r.CarId, r.TrackId))
            .Select(g => (g.Key.CarId, g.Key.TrackId, FieldBest: g.Min(r => r.BestLapSeconds)));

        foreach (var (carId, trackId, fieldBest) in combos)
            await DemoCache.UpsertAsync<double?>(
                db, $"wr:{carId}:{trackId}", DemoWorldRecord.RecordSeconds(fieldBest), ct);
    }

    /// <summary>laps:{subsessionId}:{demo driver} — a synthetic per-lap trace around the demo driver's
    /// BestLapSeconds for each synthetic subsession; summary stats via the real LapAnalysis.Compute.</summary>
    public async Task SeedLapDataAsync(CancellationToken ct)
    {
        var subBests = await db.SubsessionResults
            .Where(r => r.CustId == DemoData.DriverCustId && r.SubsessionId < 0 && r.BestLapSeconds > 0)
            .Select(r => new { r.SubsessionId, r.BestLapSeconds })
            .ToListAsync(ct);

        foreach (var s in subBests)
        {
            var laps = DemoLapData.BuildLaps(s.SubsessionId, s.BestLapSeconds);
            var (mean, std, fastest, deg) = LapAnalysis.Compute(laps);
            var dto = new DriverLapsDto(s.SubsessionId, DemoData.DriverCustId, mean, std, fastest, deg, laps);
            await DemoCache.UpsertAsync(db, $"laps:{s.SubsessionId}:{DemoData.DriverCustId}", dto, ct);
        }
    }

    /// <summary>Runs every demo seed step in order. Safe to re-run (each step upserts).</summary>
    public async Task SeedAllAsync(CancellationToken ct)
    {
        await SeedMembersAsync(ct);
        await SeedActivityAsync(ct);
        await SeedLeaderboardsAsync(ct);
        await SeedStandingsAsync(ct);
        await SeedRaceGuideAsync(ct);
        await SeedBopAndWeatherAsync(ct);
    }
}
