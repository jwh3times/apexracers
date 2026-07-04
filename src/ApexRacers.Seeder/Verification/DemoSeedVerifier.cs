using ApexRacers.Core;
using ApexRacers.Data;
using ApexRacers.Seeder.Demo;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Seeder.Verification;

public sealed record VerificationCheck(string Name, bool Passed, string Detail);

/// <summary>
/// Mechanical gate for the prod demo rollout (deployTODO.md §14) and the M2 teardown.
/// VerifyDemoAsync: every cache-key family + persisted gap the demo surface reads must
/// exist with the far-future sentinel. VerifyTeardownAsync: none of it remains.
/// Expected key formats are read from <see cref="DemoCacheSeeder"/>'s actual writes —
/// keep the two in lockstep if the seeder's key formats ever change.
/// </summary>
public static class DemoSeedVerifier
{
    private static readonly DateTimeOffset Sentinel = new(9000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task<List<VerificationCheck>> VerifyDemoAsync(AppDbContext db, CancellationToken ct)
    {
        var checks = new List<VerificationCheck>();
        var keys = await db.ExternalDataCaches.Select(c => c.CacheKey).ToListAsync(ct);
        var keySet = keys.ToHashSet();

        // 1. member keys (driver + rival; summary/recap driver-only)
        var memberKeys = new List<string>();
        foreach (var custId in new[] { DemoData.DriverCustId, DemoData.RivalCustId })
        {
            memberKeys.Add($"profile:{custId}");
            memberKeys.Add($"career:{custId}");
        }
        memberKeys.Add($"summary:{DemoData.DriverCustId}");
        memberKeys.Add($"recap:{DemoData.DriverCustId}");
        AddSetCheck(checks, "members", memberKeys, keySet);

        // 2. activity
        AddSetCheck(checks, "activity",
            [$"awards:{DemoData.DriverCustId}", $"recent:{DemoData.DriverCustId}"], keySet);

        // 3. leaderboards 1..6
        AddSetCheck(checks, "leaderboards",
            Enumerable.Range(1, 6).Select(c => $"leaderboard:{c}").ToList(), keySet);

        // 4. standings/tt/qual per active season × class (+ per week for qual)
        var expectedStandings = new List<string>();
        var seasonIds = await db.Seasons.Where(s => s.Active).Select(s => s.Id).ToListAsync(ct);
        foreach (var seasonId in seasonIds)
        {
            var classIds = await db.SeasonCarClasses
                .Where(c => c.SeasonId == seasonId).Select(c => c.CarClassId).ToListAsync(ct);
            var weekNums = await db.Weeks
                .Where(w => w.SeasonId == seasonId).Select(w => w.WeekNumber).ToListAsync(ct);
            foreach (var classId in classIds)
            {
                expectedStandings.Add($"standings:{seasonId}:{classId}");
                expectedStandings.Add($"tt-standings:{seasonId}:{classId}");
                expectedStandings.AddRange(weekNums.Select(w => $"qual:{seasonId}:{classId}:{w}"));
            }
        }
        AddSetCheck(checks, "standings", expectedStandings, keySet);

        // 5. race guide
        AddSetCheck(checks, "race-guide", ["race-guide"], keySet);

        // 6. world records per (car, track) combo present in synthetic results.
        // Two-step + in-memory join (rather than a r.Subsession.TrackId nav-property join)
        // so this translates identically on SQLite/Npgsql and the EF InMemory provider.
        var negativeSubTracks = await db.Subsessions
            .Where(s => s.Id < 0)
            .Select(s => new { s.Id, s.TrackId })
            .ToListAsync(ct);
        var trackByNegSub = negativeSubTracks.ToDictionary(s => s.Id, s => s.TrackId);

        var wrPairs = await db.SubsessionResults
            .Where(r => r.SubsessionId < 0 && r.BestLapSeconds > 0)
            .Select(r => new { r.CarId, r.SubsessionId })
            .Distinct().ToListAsync(ct);
        var wrCombos = wrPairs
            .Where(p => trackByNegSub.ContainsKey(p.SubsessionId))
            .Select(p => $"wr:{p.CarId}:{trackByNegSub[p.SubsessionId]}")
            .Distinct().ToList();
        AddSetCheck(checks, "world-records", wrCombos, keySet);

        // 7. lap traces per demo-driver synthetic subsession
        var demoSubs = await db.SubsessionResults
            .Where(r => r.CustId == DemoData.DriverCustId && r.SubsessionId < 0)
            .Select(r => r.SubsessionId).Distinct().ToListAsync(ct);
        AddSetCheck(checks, "lap-data",
            demoSubs.Select(s => $"laps:{s}:{DemoData.DriverCustId}").ToList(), keySet);

        // 8. curated driver-search terms
        AddSetCheck(checks, "driver-search",
            DemoDriverSearchData.Terms.Keys.Select(t => $"driversearch:{t}").ToList(), keySet);

        // 9. every demo cache row carries the sentinel (materialize, then filter —
        //    DateTimeOffset range predicates are the known SQLite-untranslatable case)
        var nonSentinel = (await db.ExternalDataCaches
                .Select(c => new { c.CacheKey, c.ExpiresAt }).ToListAsync(ct))
            .Where(c => c.ExpiresAt < Sentinel).Select(c => c.CacheKey).ToList();
        checks.Add(new("sentinel-expiry", nonSentinel.Count == 0,
            nonSentinel.Count == 0 ? "all cache rows sentinel" : $"non-sentinel: {string.Join(", ", nonSentinel.Take(5))}…"));

        // 10. persisted gaps: synthetic races, BoP, weather
        var negSubs = await db.Subsessions.CountAsync(s => s.Id < 0, ct);
        checks.Add(new("synthetic-races", negSubs > 0, $"{negSubs} negative-id subsessions"));
        var bops = await db.SeasonCarBops.CountAsync(ct);
        checks.Add(new("bop", bops > 0, $"{bops} SeasonCarBop rows"));
        var weatherless = await db.Weeks.CountAsync(
            w => seasonIds.Contains(w.SeasonId) && string.IsNullOrEmpty(w.WeatherSummaryJson), ct);
        checks.Add(new("weather", weatherless == 0, $"{weatherless} active-season weeks missing weather"));

        // 11. flag row exists (state is the operator's call — report, don't fail)
        var flag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == "iracing-demo", ct);
        checks.Add(new("flag-row", flag is not null,
            flag is null ? "iracing-demo flag row missing" : $"present (IsEnabled={flag.IsEnabled}, MinimumRole={flag.MinimumRole})"));

        return checks;
    }

    public static async Task<List<VerificationCheck>> VerifyTeardownAsync(AppDbContext db, CancellationToken ct)
    {
        var checks = new List<VerificationCheck>();
        var sentinelRows = (await db.ExternalDataCaches
                .Select(c => c.ExpiresAt).ToListAsync(ct))
            .Count(e => e >= Sentinel);
        checks.Add(new("no-sentinel-cache", sentinelRows == 0, $"{sentinelRows} sentinel rows remain"));
        var negSubs = await db.Subsessions.CountAsync(s => s.Id < 0, ct);
        checks.Add(new("no-synthetic-races", negSubs == 0, $"{negSubs} negative-id subsessions remain"));
        var posSubs = await db.Subsessions.CountAsync(s => s.Id > 0, ct);
        checks.Add(new("real-ingestion-info", true, $"{posSubs} positive-id (real) subsessions present"));
        return checks;
    }

    private static void AddSetCheck(List<VerificationCheck> checks, string name,
        IReadOnlyCollection<string> expected, HashSet<string> actual)
    {
        var missing = expected.Where(k => !actual.Contains(k)).ToList();
        checks.Add(new(name, missing.Count == 0,
            missing.Count == 0
                ? $"{expected.Count} keys present"
                : $"missing {missing.Count}/{expected.Count}: {string.Join(", ", missing.Take(5))}…"));
    }
}
