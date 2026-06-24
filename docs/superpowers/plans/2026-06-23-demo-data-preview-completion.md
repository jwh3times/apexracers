# Demo-Data Preview — Surface Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill the three deferred demo-mode gaps — the percentile page's world-record overlay, Race Detail's "Your Race Pace" trace, and the `/compare` driver-search box — so they render against synthetic data.

**Architecture:** Backend-only, extending the Plan 2 `DemoCacheSeeder` (`--demo`) with three new seed steps that write `ExternalDataCaches` rows under each service's exact keys with the existing far-future `ExpiresAt` sentinel. One small production change: `WorldRecordService` drops its early `!IsConfigured` short-circuit so a seeded `wr:` row is read in demo mode (the existing try/catch remains the not-configured fallback).

**Tech Stack:** .NET 10 / EF Core 10 / Npgsql; the seeder reuses the real Api DTOs (`DriverLapsDto`, `LapDto`, `DriverSearchResultDto`) + the real `LapAnalysis.Compute`; xUnit + in-memory SQLite (`DbContextFactory.Create()`).

This completes the demo surface begun in Plans 1–2 (both merged). Spec: `docs/superpowers/specs/2026-06-23-demo-data-preview-completion-design.md`.

## Background facts (verified against the code — do not re-derive)

- **The three keys + cached `<T>`:** `wr:{carId}:{trackId}` → `double?` (`WorldRecordService.cs:23`); `laps:{subsessionId}:{custId}` → `DriverLapsDto` (`LapDataService.cs:18`); `driversearch:{term}` → `List<DriverSearchResultDto>` (`RivalService.cs:64`, term is `Trim().ToLowerInvariant()`, ≥2 chars).
- **`WorldRecordService.GetWorldRecordLapSecondsAsync`** currently early-returns `null` when `!cached.IsConfigured` (line 18) **before** the cache, so a seeded `wr:` row is never read in demo. `LapDataService` and `RivalService.SearchDriversAsync` have **no** such guard — they go straight to `GetOrFetchAsync`, so seeded rows are hits. (`LapDataService` is fine as-is; only `WorldRecordService` needs the change.)
- **DTO/helper signatures (verbatim):**
  - `LapAnalysis.Compute(IReadOnlyList<LapDto> laps)` → `(double Mean, double StdDev, double Fastest, double Deg)` (`ApexRacers.Api.Services`).
  - `LapDto(int LapNumber, double LapTimeSeconds, bool Incident, bool Valid)` (`ApexRacers.Api.Dtos`).
  - `DriverLapsDto(int SubsessionId, long CustId, double MeanSeconds, double StdDevSeconds, double FastestLapSeconds, double DegSlopeSecondsPerLap, IReadOnlyList<LapDto> Laps)`.
  - `DriverSearchResultDto(long CustId, string DisplayName)`.
- **Demo identity:** `DemoData.DriverCustId = 100_001`, `DemoData.RivalCustId = 100_002` (`ApexRacers.Core`).
- **`DemoCache`:** `UpsertAsync<T>(AppDbContext db, string key, T value, CancellationToken ct)` writes the row with `ExpiresAt = DemoCache.Sentinel` (`9999-01-01`), serializing with default options (`ApexRacers.Seeder.Demo`).
- **`DemoCacheSeeder`** (`ApexRacers.Seeder.Demo`) is a `sealed class DemoCacheSeeder(AppDbContext db)` with `SeedMembersAsync`/`SeedActivityAsync`/`SeedLeaderboardsAsync`/`SeedStandingsAsync`/`SeedRaceGuideAsync`/`SeedBopAndWeatherAsync` and `SeedAllAsync` (calls the six in order). This plan appends three methods + three calls.
- **Entities for the DB-read seeders:** `SubsessionResult` has `SubsessionId` (int; synthetic = negative), `CustId` (long), `CarId` (int), `BestLapSeconds` (double), and a `Subsession` navigation; `Subsession` has `TrackId` (int). **SQLite-safe pattern (per CLAUDE.md):** project to entity columns server-side (a nav join like `r.Subsession.TrackId` is fine), materialize, then group in memory — do not `GroupBy` a nav property in the SQL.

## Global Constraints

- **Backend coverage:** ≥ 85% line **and** branch. New pure builders + seeder methods + the `WorldRecordService` change get xUnit tests. (The Seeder's top-level `Program` is excluded from coverage; the new `Demo/*` classes are measured via the existing `Tests→Seeder` reference.)
- **Cache fidelity:** write the **real** cached `<T>` via `DemoCache.UpsertAsync` (default `JsonSerializer` options); keys are the exact verbatim strings above. New rows carry `DemoCache.Sentinel`.
- **Reuse real types/helpers** — `DriverLapsDto`/`LapDto`/`DriverSearchResultDto` + `LapAnalysis.Compute`. No mirror types.
- **Determinism:** pure builders take no `DateTimeOffset.UtcNow`/`Random`.
- **No purge change:** the new rows are sentinel rows, already removed by `purge_demo_data.sql`'s `DELETE … WHERE "ExpiresAt" >= '9000-01-01'`. Do **not** edit the purge.
- **No frontend changes.** Backend + docs only.
- **NuGet versions** are centrally managed in `Directory.Packages.props`.

## File Structure

- Modify: `src/ApexRacers.Api/Services/WorldRecordService.cs` (drop the early guard) + `src/ApexRacers.Tests/Services/WorldRecordServiceTests.cs` (add seeded-hit test).
- Create: `src/ApexRacers.Seeder/Demo/DemoWorldRecord.cs` (pure `RecordSeconds`), `DemoLapData.cs` (pure `BuildLaps`), `DemoDriverSearchData.cs` (pure curated map).
- Modify: `src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs` (3 new seed methods + wire into `SeedAllAsync`).
- Tests: `src/ApexRacers.Tests/Seeder/DemoWorldRecordTests.cs`, `DemoLapDataTests.cs`, `DemoDriverSearchDataTests.cs`, `DemoCacheSeederCompletionTests.cs` (integration for the 3 new seed methods), and an addition to `DemoCacheSeederAllTests.cs`.
- Docs: `CLAUDE.md` (tracked); `private/ROADMAP.md`, `private/deployTODO.md` (gitignored).

---

### Task 1: WR overlay — `WorldRecordService` change + `wr:` seeding

**Files:**
- Modify: `src/ApexRacers.Api/Services/WorldRecordService.cs`
- Modify: `src/ApexRacers.Tests/Services/WorldRecordServiceTests.cs`
- Create: `src/ApexRacers.Seeder/Demo/DemoWorldRecord.cs`
- Modify: `src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs`
- Test: `src/ApexRacers.Tests/Seeder/DemoWorldRecordTests.cs`, `src/ApexRacers.Tests/Seeder/DemoCacheSeederCompletionTests.cs`

**Interfaces:**
- Produces: `DemoWorldRecord.RecordSeconds(double fieldBest)` → `double`; `DemoCacheSeeder.SeedWorldRecordsAsync(CancellationToken ct)`. `WorldRecordService.GetWorldRecordLapSecondsAsync` keeps its signature; behavior change only.

- [ ] **Step 1: Update `WorldRecordService` test — add the seeded-hit case (RED)**

Add to `WorldRecordServiceTests.cs` (add `using System.Text.Json;` and `using ApexRacers.Core.Models;` to the usings):
```csharp
    [Fact]
    public async Task GetWorldRecordLapSecondsAsync_NotConfiguredButCacheSeeded_ReturnsCachedValue()
    {
        await using var db = DbContextFactory.Create();
        db.ExternalDataCaches.Add(new ExternalDataCache
        {
            CacheKey = "wr:132:532",
            Payload = JsonSerializer.Serialize<double?>(65.5),
            FetchedAt = DateTimeOffset.UtcNow,
            ExpiresAt = new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync(Ct);
        var service = new WorldRecordService(new CachedIRacingClient(db, new StubServiceProvider(null)));

        Assert.Equal(65.5, (await service.GetWorldRecordLapSecondsAsync(132, 532, Ct))!.Value, precision: 3);
    }
```

- [ ] **Step 2: Run it — verify it fails (RED)**

Run: `dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~WorldRecordServiceTests"`
Expected: the new test FAILS (current code early-returns `null` when `!IsConfigured`, ignoring the seeded row); the existing tests still pass.

- [ ] **Step 3: Remove the early guard in `WorldRecordService`**

In `src/ApexRacers.Api/Services/WorldRecordService.cs`, change `GetWorldRecordLapSecondsAsync` to delete the `if (!cached.IsConfigured) return null;` lines so the body is exactly:
```csharp
    public async Task<double?> GetWorldRecordLapSecondsAsync(
        int carId, int trackId, CancellationToken ct)
    {
        try
        {
            return await cached.GetOrFetchAsync<double?>(
                $"wr:{carId}:{trackId}", TimeSpan.FromHours(24),
                async c => FastestLapSeconds(
                    (await c.GetWorldRecordsAsync(carId, trackId, null, null, ct)).Data.Item2),
                ct);
        }
        catch (IRacingNotConfiguredException)
        {
            return null;
        }
    }
```
(A seeded hit returns the value; a miss with no creds makes `GetOrFetchAsync` throw `IRacingNotConfiguredException` → caught → `null`; with creds present it fetches live — all unchanged except the seeded-hit case.) Update the XML doc's "Returns null when iRacing isn't configured" sentence to "Returns null when iRacing isn't configured **and** the value isn't cached, so the percentile path degrades gracefully."

- [ ] **Step 4: Run the `WorldRecordService` tests — verify GREEN**

Run: `dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~WorldRecordServiceTests"`
Expected: all pass — the new seeded-hit test plus the existing `NotConfigured_ReturnsNullWithoutFetch` (now: miss → throws → caught → null) and `Configured_ReturnsFastestAndCachesOnce`.

- [ ] **Step 5: Write the pure `DemoWorldRecord` helper test (RED)**

Create `src/ApexRacers.Tests/Seeder/DemoWorldRecordTests.cs`:
```csharp
using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoWorldRecordTests
{
    [Fact]
    public void RecordSeconds_IsTwoPercentBelowFieldBest_Rounded()
    {
        Assert.Equal(98.0, DemoWorldRecord.RecordSeconds(100.0), precision: 4);   // 100 * 0.98
        Assert.Equal(65.66, DemoWorldRecord.RecordSeconds(67.0), precision: 2);   // 67 * 0.98 = 65.66
    }

    [Fact]
    public void RecordSeconds_IsFasterThanFieldBest()
    {
        Assert.True(DemoWorldRecord.RecordSeconds(90.0) < 90.0);
    }
}
```

- [ ] **Step 6: Run it — verify it fails (RED)**

Run: `dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoWorldRecordTests"`
Expected: compile error — `DemoWorldRecord` does not exist.

- [ ] **Step 7: Implement `DemoWorldRecord`**

Create `src/ApexRacers.Seeder/Demo/DemoWorldRecord.cs`:
```csharp
namespace ApexRacers.Seeder.Demo;

/// <summary>Pure helper for the synthetic world-record lap: a realistic small margin (2%) below the
/// fastest synthetic field lap for a car+track, so the percentile page's WR gap is positive.</summary>
public static class DemoWorldRecord
{
    public static double RecordSeconds(double fieldBest) => Math.Round(fieldBest * 0.98, 4);
}
```

- [ ] **Step 8: Run the helper test — verify GREEN**

Run: `dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoWorldRecordTests"`
Expected: both pass.

- [ ] **Step 9: Write the seeding integration test (RED)**

Create `src/ApexRacers.Tests/Seeder/DemoCacheSeederCompletionTests.cs` (this file gains the lap-data + driver-search tests in Tasks 2–3):
```csharp
using System.Text.Json;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoCacheSeederCompletionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // One synthetic subsession (negative id) on track 532 with the demo driver's result.
    private static async Task<AppDbContext> SeededResultsAsync()
    {
        var db = DbContextFactory.Create();
        db.Subsessions.Add(new Subsession { Id = -10, SeasonId = 6115, WeekNumber = 0, TrackId = 532 });
        db.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId = -10, CustId = DemoData.DriverCustId, CarId = 132, BestLapSeconds = 90.0,
        });
        db.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId = -10, CustId = 100_050, CarId = 132, BestLapSeconds = 88.0, // field best
        });
        await db.SaveChangesAsync(Ct);
        return db;
    }

    [Fact]
    public async Task SeedWorldRecordsAsync_WritesWrPerCarTrack_BelowFieldBest()
    {
        await using var db = await SeededResultsAsync();

        await new DemoCacheSeeder(db).SeedWorldRecordsAsync(Ct);

        var row = await db.ExternalDataCaches.SingleAsync(c => c.CacheKey == "wr:132:532", Ct);
        var wr = JsonSerializer.Deserialize<double?>(row.Payload);
        Assert.Equal(86.24, wr!.Value, precision: 2);              // 88.0 * 0.98
        Assert.Equal(DemoCache.Sentinel, row.ExpiresAt);
    }
}
```

- [ ] **Step 10: Run it — verify it fails (RED)**

Run: `dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoCacheSeederCompletionTests"`
Expected: compile error — `SeedWorldRecordsAsync` does not exist.

- [ ] **Step 11: Add `SeedWorldRecordsAsync` to `DemoCacheSeeder`**

Append to `DemoCacheSeeder` (the class already has `using Microsoft.EntityFrameworkCore;`):
```csharp
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
```

- [ ] **Step 12: Run the seeding test — verify GREEN**

Run: `dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoCacheSeederCompletionTests"`
Expected: `SeedWorldRecordsAsync_WritesWrPerCarTrack_BelowFieldBest` passes.

- [ ] **Step 13: Commit**

```bash
git add src/ApexRacers.Api/Services/WorldRecordService.cs src/ApexRacers.Tests/Services/WorldRecordServiceTests.cs src/ApexRacers.Seeder/Demo/DemoWorldRecord.cs src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs src/ApexRacers.Tests/Seeder/DemoWorldRecordTests.cs src/ApexRacers.Tests/Seeder/DemoCacheSeederCompletionTests.cs
git commit -m "feat(demo): seed world-record overlay (WorldRecordService reads cache when present)"
```

---

### Task 2: Lap-data — `DemoLapData` builder + `laps:` seeding

**Files:**
- Create: `src/ApexRacers.Seeder/Demo/DemoLapData.cs`
- Modify: `src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs`
- Test: `src/ApexRacers.Tests/Seeder/DemoLapDataTests.cs`, `src/ApexRacers.Tests/Seeder/DemoCacheSeederCompletionTests.cs`

**Interfaces:**
- Consumes: `LapDto`, `DriverLapsDto`, `LapAnalysis.Compute` (Api); `DemoData.DriverCustId`; `DemoCache`.
- Produces: `DemoLapData.BuildLaps(int subsessionId, double bestLap)` → `List<LapDto>`; `DemoCacheSeeder.SeedLapDataAsync(CancellationToken ct)`.

- [ ] **Step 1: Write the failing builder test**

Create `src/ApexRacers.Tests/Seeder/DemoLapDataTests.cs`:
```csharp
using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoLapDataTests
{
    [Fact]
    public void BuildLaps_30Laps_FastestEqualsBest_OneIncident_AscendingNumbers()
    {
        var laps = DemoLapData.BuildLaps(-10, 90.0);

        Assert.Equal(30, laps.Count);
        Assert.Equal(Enumerable.Range(1, 30), laps.Select(l => l.LapNumber));
        Assert.Equal(1, laps.Count(l => l.Incident));                       // exactly one incident lap
        Assert.False(laps.Single(l => l.Incident).Valid);                   // incident lap not green
        var greenMin = laps.Where(l => l.Valid).Min(l => l.LapTimeSeconds);
        Assert.Equal(90.0, greenMin, precision: 3);                         // fastest valid == best
        Assert.All(laps.Where(l => l.Valid), l => Assert.True(l.LapTimeSeconds >= 90.0));
    }

    [Fact]
    public void BuildLaps_IsDeterministic()
    {
        Assert.Equal(DemoLapData.BuildLaps(-10, 90.0), DemoLapData.BuildLaps(-10, 90.0));
    }
}
```

- [ ] **Step 2: Run it — verify it fails (RED)**

Run: `dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoLapDataTests"`
Expected: compile error — `DemoLapData` does not exist.

- [ ] **Step 3: Implement `DemoLapData`**

Create `src/ApexRacers.Seeder/Demo/DemoLapData.cs`:
```csharp
using ApexRacers.Api.Dtos;

namespace ApexRacers.Seeder.Demo;

/// <summary>Pure builder for a deterministic ~30-lap pace trace around a driver's best lap: an out-lap,
/// a green run with a slight positive degradation slope (fastest == best), and one incident lap. The
/// incident lap position is deterministic per subsessionId so re-seeding is stable.</summary>
public static class DemoLapData
{
    public static List<LapDto> BuildLaps(int subsessionId, double bestLap)
    {
        const int lapCount = 30;
        var incidentLap = 6 + Math.Abs(subsessionId) % 8;   // deterministic, in 6..13
        var laps = new List<LapDto>(lapCount);

        for (var n = 1; n <= lapCount; n++)
        {
            if (n == incidentLap)
            {
                // Timed but flagged: red marker on the trace, excluded from green/fastest.
                laps.Add(new LapDto(n, Math.Round(bestLap * 1.15, 3), Incident: true, Valid: false));
                continue;
            }

            // Lap 1 is an out-lap (slower); lap 2 is the fastest (== best); the rest degrade slightly.
            var seconds = n switch
            {
                1 => Math.Round(bestLap * 1.06, 3),
                2 => Math.Round(bestLap, 3),
                _ => Math.Round(bestLap + (n - 2) * 0.015 + (Math.Abs(subsessionId) + n) % 4 * 0.01, 3),
            };
            laps.Add(new LapDto(n, seconds, Incident: false, Valid: true));
        }

        return laps;
    }
}
```

- [ ] **Step 4: Run it — verify GREEN**

Run: `dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoLapDataTests"`
Expected: both pass.

- [ ] **Step 5: Add the seeding integration test (RED)**

Add to `src/ApexRacers.Tests/Seeder/DemoCacheSeederCompletionTests.cs`:
```csharp
    [Fact]
    public async Task SeedLapDataAsync_WritesLapTracePerSubsession_ForDemoDriver()
    {
        await using var db = await SeededResultsAsync();

        await new DemoCacheSeeder(db).SeedLapDataAsync(Ct);

        var row = await db.ExternalDataCaches
            .SingleAsync(c => c.CacheKey == $"laps:-10:{DemoData.DriverCustId}", Ct);
        var dto = JsonSerializer.Deserialize<ApexRacers.Api.Dtos.DriverLapsDto>(row.Payload)!;
        Assert.Equal(-10, dto.SubsessionId);
        Assert.Equal(DemoData.DriverCustId, dto.CustId);
        Assert.Equal(90.0, dto.FastestLapSeconds, precision: 3);   // demo driver's BestLapSeconds
        Assert.Equal(30, dto.Laps.Count);
        Assert.Equal(DemoCache.Sentinel, row.ExpiresAt);
    }
```

- [ ] **Step 6: Run it — verify it fails (RED)**

Run: `dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoCacheSeederCompletionTests"`
Expected: `SeedLapDataAsync_…` fails (compile error — `SeedLapDataAsync` does not exist).

- [ ] **Step 7: Add `SeedLapDataAsync` to `DemoCacheSeeder`**

Add `using ApexRacers.Api.Dtos;` and `using ApexRacers.Api.Services;` to `DemoCacheSeeder.cs`, then append:
```csharp
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
```

- [ ] **Step 8: Run the seeding test — verify GREEN**

Run: `dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoCacheSeederCompletionTests"`
Expected: both `SeedWorldRecordsAsync_…` and `SeedLapDataAsync_…` pass.

- [ ] **Step 9: Commit**

```bash
git add src/ApexRacers.Seeder/Demo/DemoLapData.cs src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs src/ApexRacers.Tests/Seeder/DemoLapDataTests.cs src/ApexRacers.Tests/Seeder/DemoCacheSeederCompletionTests.cs
git commit -m "feat(demo): seed Race Detail lap-trace (laps: per synthetic subsession)"
```

---

### Task 3: Driver-search — `DemoDriverSearchData` + `driversearch:` seeding

**Files:**
- Create: `src/ApexRacers.Seeder/Demo/DemoDriverSearchData.cs`
- Modify: `src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs`
- Test: `src/ApexRacers.Tests/Seeder/DemoDriverSearchDataTests.cs`, `src/ApexRacers.Tests/Seeder/DemoCacheSeederCompletionTests.cs`

**Interfaces:**
- Consumes: `DriverSearchResultDto` (Api); `DemoData`; `DemoCache`.
- Produces: `DemoDriverSearchData.Terms` (`IReadOnlyDictionary<string, List<DriverSearchResultDto>>`); `DemoCacheSeeder.SeedDriverSearchAsync(CancellationToken ct)`.

- [ ] **Step 1: Write the failing builder test**

Create `src/ApexRacers.Tests/Seeder/DemoDriverSearchDataTests.cs`:
```csharp
using ApexRacers.Core;
using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoDriverSearchDataTests
{
    [Fact]
    public void Terms_AreLowercase_MinTwoChars_WithRealResults()
    {
        Assert.NotEmpty(DemoDriverSearchData.Terms);
        Assert.All(DemoDriverSearchData.Terms.Keys, k =>
        {
            Assert.Equal(k.ToLowerInvariant(), k);
            Assert.True(k.Length >= 2);
        });
        Assert.All(DemoDriverSearchData.Terms.Values, v => Assert.NotEmpty(v));
    }

    [Fact]
    public void RivalTerms_ReturnTheRival()
    {
        foreach (var term in new[] { "rival", "racer", "riv" })
            Assert.Contains(DemoDriverSearchData.Terms[term], r => r.CustId == DemoData.RivalCustId);
    }
}
```

- [ ] **Step 2: Run it — verify it fails (RED)**

Run: `dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoDriverSearchDataTests"`
Expected: compile error — `DemoDriverSearchData` does not exist.

- [ ] **Step 3: Implement `DemoDriverSearchData`**

Create `src/ApexRacers.Seeder/Demo/DemoDriverSearchData.cs`:
```csharp
using ApexRacers.Api.Dtos;
using ApexRacers.Core;

namespace ApexRacers.Seeder.Demo;

/// <summary>Curated synthetic driver-search results keyed by lowercased term (matching RivalService's
/// Trim().ToLowerInvariant() normalization + ≥2-char rule). Arbitrary unseeded terms still 503 — a
/// documented demo caveat (infinite terms can't be pre-seeded).</summary>
public static class DemoDriverSearchData
{
    private static readonly DriverSearchResultDto Demo = new(DemoData.DriverCustId, "Demo Driver");
    private static readonly DriverSearchResultDto Rival = new(DemoData.RivalCustId, "Rival Racer");

    public static readonly IReadOnlyDictionary<string, List<DriverSearchResultDto>> Terms =
        new Dictionary<string, List<DriverSearchResultDto>>
        {
            ["demo"] = [Demo],
            ["rival"] = [Rival],
            ["riv"] = [Rival],
            ["racer"] = [Rival],
            ["rac"] = [Rival],
            ["driver"] = [Demo, Rival, new(100_003, "Driver 100003"), new(100_004, "Driver 100004")],
            ["dri"] = [Demo, new(100_003, "Driver 100003")],
        };
}
```

- [ ] **Step 4: Run it — verify GREEN**

Run: `dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoDriverSearchDataTests"`
Expected: both pass.

- [ ] **Step 5: Add the seeding integration test (RED)**

Add to `src/ApexRacers.Tests/Seeder/DemoCacheSeederCompletionTests.cs`:
```csharp
    [Fact]
    public async Task SeedDriverSearchAsync_WritesCuratedTermKeys()
    {
        await using var db = DbContextFactory.Create();

        await new DemoCacheSeeder(db).SeedDriverSearchAsync(Ct);

        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == "driversearch:rival", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == "driversearch:demo", Ct));
        var row = await db.ExternalDataCaches.SingleAsync(c => c.CacheKey == "driversearch:rival", Ct);
        var hits = JsonSerializer.Deserialize<List<ApexRacers.Api.Dtos.DriverSearchResultDto>>(row.Payload)!;
        Assert.Contains(hits, h => h.CustId == DemoData.RivalCustId);
        Assert.Equal(DemoCache.Sentinel, row.ExpiresAt);
    }
```

- [ ] **Step 6: Run it — verify it fails (RED)**

Run: `dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoCacheSeederCompletionTests"`
Expected: `SeedDriverSearchAsync_…` fails (compile error — `SeedDriverSearchAsync` does not exist).

- [ ] **Step 7: Add `SeedDriverSearchAsync` to `DemoCacheSeeder`**

Append:
```csharp
    /// <summary>driversearch:{term} for a curated set of terms matching the demo names. Arbitrary
    /// unseeded terms still 503 (documented caveat).</summary>
    public async Task SeedDriverSearchAsync(CancellationToken ct)
    {
        foreach (var (term, hits) in DemoDriverSearchData.Terms)
            await DemoCache.UpsertAsync(db, $"driversearch:{term}", hits, ct);
    }
```

- [ ] **Step 8: Run the seeding test — verify GREEN**

Run: `dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoCacheSeederCompletionTests"`
Expected: all three seeding tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/ApexRacers.Seeder/Demo/DemoDriverSearchData.cs src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs src/ApexRacers.Tests/Seeder/DemoDriverSearchDataTests.cs src/ApexRacers.Tests/Seeder/DemoCacheSeederCompletionTests.cs
git commit -m "feat(demo): seed curated /compare driver-search terms"
```

---

### Task 4: Wire into `SeedAllAsync` + e2e + docs

**Files:**
- Modify: `src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs` (`SeedAllAsync`)
- Modify: `src/ApexRacers.Tests/Seeder/DemoCacheSeederAllTests.cs`
- Modify: `CLAUDE.md`; `private/ROADMAP.md`, `private/deployTODO.md`

**Interfaces:** none new — `SeedAllAsync` now runs all nine steps.

- [ ] **Step 1: Extend the `SeedAllAsync` test (RED)**

In `src/ApexRacers.Tests/Seeder/DemoCacheSeederAllTests.cs`, the existing test seeds a minimal active season+class+week+car and runs `SeedAllAsync`. Add a synthetic subsession + demo-driver result to its fixture so the new steps have data, and assert a representative new key. Add inside the fixture setup (before `SeedAllAsync`):
```csharp
        db.Subsessions.Add(new Subsession { Id = -10, SeasonId = 6115, WeekNumber = 0, TrackId = 1 });
        db.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId = -10, CustId = DemoData.DriverCustId, CarId = 132, BestLapSeconds = 90.0,
        });
```
and add assertions after `SeedAllAsync`:
```csharp
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == "wr:132:1", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == $"laps:-10:{DemoData.DriverCustId}", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == "driversearch:rival", Ct));
```
(The existing `Assert.All(... ExpiresAt == DemoCache.Sentinel)` now also covers the new rows. Add `using ApexRacers.Core.Models;` if not already present.)

- [ ] **Step 2: Run it — verify it fails (RED)**

Run: `dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoCacheSeederAllTests"`
Expected: the new key assertions FAIL (`SeedAllAsync` doesn't call the three new steps yet).

- [ ] **Step 3: Wire the three steps into `SeedAllAsync`**

In `DemoCacheSeeder.SeedAllAsync`, append the three calls after `SeedBopAndWeatherAsync(ct)`:
```csharp
        await SeedWorldRecordsAsync(ct);
        await SeedLapDataAsync(ct);
        await SeedDriverSearchAsync(ct);
```

- [ ] **Step 4: Run it — verify GREEN, then the full suite**

Run: `dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoCacheSeederAllTests"` then `dotnet test src/ApexRacers.Tests`
Expected: the `SeedAllAsync` test passes; full suite green.

- [ ] **Step 5: End-to-end local re-seed (Docker Postgres up)**

The new lap-data step only populates fresh seeds, and the demo driver's `DisplayName`/results already exist — but `--demo` is idempotent, so a plain re-run seeds the new keys against the existing synthetic data. Run:
```bash
dotnet run --project src/ApexRacers.Seeder -- --demo
echo 'SELECT (SELECT count(*) FROM iracing."ExternalDataCaches" WHERE "CacheKey" LIKE $$wr:%$$) AS wr, (SELECT count(*) FROM iracing."ExternalDataCaches" WHERE "CacheKey" LIKE $$laps:%$$) AS laps, (SELECT count(*) FROM iracing."ExternalDataCaches" WHERE "CacheKey" LIKE $$driversearch:%$$) AS search;' | docker compose exec -T postgres psql -U apexracers -d apexracers
```
Expected: `wr` > 0, `laps` ≈ 425, `search` = 7. (If Docker is unreachable, note it skipped — the integration tests cover the logic.)

- [ ] **Step 6: Update docs**

- `CLAUDE.md` — in the demo "Known demo caveats" paragraph, remove the WR overlay, Race Detail pace trace, and `/compare` driver-search items; keep `/analytics` lazy, race-guide static, and **arbitrary (unseeded) driver-search terms** 503. Note that `WorldRecordService` now reads a cached `wr:` row even when creds are absent (so the demo WR overlay renders).
- `private/ROADMAP.md` — under the demo-data preview entry, move the three "remaining demo gaps" to done (WR overlay, lap trace, curated driver-search shipped; only arbitrary-term search remains a caveat).
- `private/deployTODO.md` §14 — update the "known thin spots" note: drop WR overlay / Race Detail pace trace / driver-search-box; keep `/analytics` lazy, race-guide static, and arbitrary-term search.

- [ ] **Step 7: Commit**

```bash
git add src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs src/ApexRacers.Tests/Seeder/DemoCacheSeederAllTests.cs CLAUDE.md
git commit -m "feat(demo): wire WR/lap/search into SeedAllAsync; document completion"
```
(The `private/` docs are gitignored — no commit needed.)

---

## Verification (whole plan)

- [ ] Backend: `dotnet test src/ApexRacers.Tests` — all pass; then CI-equivalent coverage (`dotnet test --collect:"XPlat Code Coverage" --settings coverage.runsettings`) — line **and** branch ≥ 85%.
- [ ] Frontend: unchanged by this plan (no `src/web` edits) — optionally `npx vitest run` to confirm no regression.
- [ ] Manual smoke (local, with `iracing-demo` on for an Alpha user): the percentile page shows a world-record gap; Race Detail shows the "Your Race Pace" trace; `/compare` driver-search for "rival" returns the rival.

## Self-Review notes (addressed)

- **Spec coverage:** §3.1 WR (service change + `wr:` seed) → Task 1; §3.2 lap-data → Task 2; §3.3 driver-search → Task 3; §3.4 orchestration + §5 docs → Task 4; §5 "no purge change" honored (no purge edits anywhere). §6 scope boundary respected (only `WorldRecordService` changes; driver-search stays partial; no frontend).
- **Cache fidelity:** all three seeders build the real cached `<T>` (`double?`, `DriverLapsDto`, `List<DriverSearchResultDto>`) via `DemoCache.UpsertAsync`; keys verbatim; sentinel asserted.
- **Type consistency:** `DemoWorldRecord.RecordSeconds(double)→double`; `DemoLapData.BuildLaps(int,double)→List<LapDto>`; `LapAnalysis.Compute` tuple `(Mean,StdDev,Fastest,Deg)`; `DriverLapsDto`/`LapDto`/`DriverSearchResultDto` constructors match the verified signatures.
- **SQLite-safe:** the WR + lap-data DB reads project entity columns (nav join allowed) then group/iterate in memory — no nav-property `GroupBy` in SQL.
