# Demo-Data Preview — Surface Completion (Design)

**Date:** 2026-06-23
**Status:** Approved
**Author:** brainstorming session (Claude + Jerry)

---

## 1. Problem & context

The demo-data preview (Plans 1 + 2, both merged) lights up the iRacing surface with synthetic data for
Alpha testers. Three surfaces were **deferred** during Plan 2 and remain thin in demo mode:

1. **Percentile page — world-record overlay** (`wr:{carId}:{trackId}`): renders empty.
2. **Race Detail — "Your Race Pace" trace** (`laps:{subsessionId}:{custId}`): blank.
3. **`/compare` — driver-search box** (`driversearch:{term}`): 503s on any term (the suggestions list works).

This increment fills all three. It is **backend-only** and reuses the Plan 2 `DemoCacheSeeder` + far-future
`ExpiresAt` sentinel pattern, plus **one small production-service change** (required for the WR overlay —
see §3.1).

Spec history: `2026-06-23-demo-data-preview-design.md` (original), `…-infrastructure.md` (Plan 1),
`…-cache-seeding.md` (Plan 2).

## 2. Decisions (locked during brainstorming)

| Decision | Choice |
| --- | --- |
| WR overlay | **Small prod change** to `WorldRecordService` (drop the early `!IsConfigured` short-circuit, rely on the existing try/catch) **+ seed `wr:`**. Cache becomes authoritative; not-configured still degrades to null on a miss. |
| WR value | Per `(carId, trackId)`: the fastest synthetic `BestLapSeconds` across the demo results **× 0.98** (a realistic small WR gap below the local field best). |
| Lap-data scope | **All 425** synthetic subsessions, demo driver only (`laps:{subsessionId}:100001`). |
| Lap-data shape | Deterministic ~30-lap trace around the demo driver's seeded `BestLapSeconds` for that subsession; summary computed via the **real** `LapAnalysis.Compute`. |
| Driver-search | **Curated seeded terms** (backend-only) matching the demo names; arbitrary unseeded terms stay a documented caveat. |
| Purge | **No change** — all new rows use `DemoCache.Sentinel`, so the existing `DELETE … WHERE "ExpiresAt" >= '9000-01-01'` already removes them. |

## 3. Component design

### 3.1 WR overlay — `WorldRecordService` change + `SeedWorldRecordsAsync`

**Production change** (`src/ApexRacers.Api/Services/WorldRecordService.cs`): remove the early guard so a
seeded cache row is read even when creds are absent:

```csharp
public async Task<double?> GetWorldRecordLapSecondsAsync(int carId, int trackId, CancellationToken ct)
{
    try
    {
        return await cached.GetOrFetchAsync<double?>(
            $"wr:{carId}:{trackId}", TimeSpan.FromHours(24),
            async c => FastestLapSeconds((await c.GetWorldRecordsAsync(carId, trackId, null, null, ct)).Data.Item2),
            ct);
    }
    catch (IRacingNotConfiguredException)
    {
        return null;
    }
}
```

Behavior matrix (all safe): seeded **hit** → returns the value (the goal); **miss + no creds** →
`GetOrFetchAsync` throws `IRacingNotConfiguredException` → caught → `null` (unchanged from today); **creds
present** → live fetch (unchanged). The `FastestLapSeconds` pure helper is untouched. Update
`WorldRecordServiceTests`: keep the "miss with no creds → null" case, add a "seeded hit returns the value
while not configured" case.

**Seeding** (`DemoCacheSeeder.SeedWorldRecordsAsync`, DB-read): query the fastest `BestLapSeconds` per
`(CarId, Subsession.TrackId)` across the synthetic `SubsessionResults` (joined to `Subsessions` for the
track), then for each distinct `(carId, trackId)` upsert `wr:{carId}:{trackId}` =
`Math.Round(fastest * 0.98, 4)`. One row per car/track in the schedule. A tiny pure helper
(`DemoWorldRecord.RecordSeconds(double fieldBest) => Math.Round(fieldBest * 0.98, 4)`) is unit-tested.

### 3.2 Lap-data — `DemoLapData` + `SeedLapDataAsync`

**Pure builder** (`src/ApexRacers.Seeder/Demo/DemoLapData.cs`):
`BuildLaps(int subsessionId, double bestLap) → List<LapDto>` — a deterministic ~30-lap trace: lap 1 an
out-lap (slower), a green run around `bestLap` with a slight positive degradation slope, the fastest lap
equal to `bestLap`, and one incident lap (`Incident=true`, `Valid=false`) seeded deterministically by
`subsessionId`. Uses the real `LapDto(int LapNumber, double LapTimeSeconds, bool Incident, bool Valid)`.

**Seeding** (`DemoCacheSeeder.SeedLapDataAsync`, DB-read): for each synthetic subsession (`Id < 0`), read
the demo driver's `BestLapSeconds` (from `SubsessionResults` where `CustId = DemoData.DriverCustId`), call
`DemoLapData.BuildLaps`, compute `(mean, std, fastest, deg)` via the **real** `LapAnalysis.Compute(laps)`,
build `DriverLapsDto(subsessionId, DemoData.DriverCustId, mean, std, fastest, deg, laps)`, and upsert
`laps:{subsessionId}:{DemoData.DriverCustId}`. ~425 rows.

### 3.3 Driver-search — `DemoDriverSearchData` + `SeedDriverSearchAsync`

**Pure builder** (`src/ApexRacers.Seeder/Demo/DemoDriverSearchData.cs`): a curated
`IReadOnlyDictionary<string, List<DriverSearchResultDto>>` of **lowercased** terms → results, e.g.:

- `"demo"` → [Demo Driver (100001)]
- `"rival"`, `"riv"`, `"racer"`, `"rac"` → [Rival Racer (100002)]
- `"driver"`, `"dri"` → [Demo Driver, Rival Racer, Driver 100003, Driver 100004, Driver 100005]

Uses the real `DriverSearchResultDto(long CustId, string DisplayName)`. Terms are ≥2 chars (matching
`RivalService`'s length rule) and lowercase (matching its `.ToLowerInvariant()` normalization).

**Seeding** (`DemoCacheSeeder.SeedDriverSearchAsync`): upsert `driversearch:{term}` for each curated entry.
No DB read needed.

### 3.4 Orchestration

`DemoCacheSeeder.SeedAllAsync` gains three steps (order doesn't matter; append after the existing six):
`SeedWorldRecordsAsync`, `SeedLapDataAsync`, `SeedDriverSearchAsync`. `--demo` runs them automatically.

## 4. Testing

- **Pure builders unit-tested:** `DemoLapData.BuildLaps` (lap count, fastest == best, one incident,
  ascending lap numbers, deterministic); `DemoWorldRecord.RecordSeconds` (× 0.98 rounding);
  `DemoDriverSearchData` (curated terms present, lowercased, real DTOs).
- **Seeding integration tests** (`DbContextFactory.Create()`): `SeedWorldRecordsAsync` writes one
  `wr:{car}:{track}` per distinct combo with value < field best; `SeedLapDataAsync` writes
  `laps:{sub}:100001` per synthetic subsession with a round-trippable `DriverLapsDto`;
  `SeedDriverSearchAsync` writes the curated `driversearch:` keys.
- **`WorldRecordService` change:** seeded-hit-returns-value-while-not-configured + miss-returns-null.
- Backend coverage stays ≥ 85% line **and** branch. New rows carry the sentinel; an existing
  `SeedAllAsync` test should assert that still holds.

## 5. Purge & docs

- **Purge:** no change (sentinel rows; the existing `purge_demo_data.sql` statement covers `wr:`/`laps:`/
  `driversearch:`).
- **Docs:** remove WR overlay, Race Detail pace trace, and `/compare` search from the "deferred / known
  caveats" lists in `CLAUDE.md` and `private/deployTODO.md` §14. Remaining documented caveats: `/analytics`
  populates lazily, the race-guide board is static "in-progress", and **arbitrary** (unseeded) driver-search
  terms still 503. Note the `WorldRecordService` change in `CLAUDE.md`. Update `private/ROADMAP.md` (mark the
  three gaps done).

## 6. Scope boundary (what this is *not*)

- No change to the live fetch path or to any service other than `WorldRecordService` (one guard removed).
- Driver-search remains **partial** by design — only curated terms hit; arbitrary terms 503 (documented).
- `/analytics` lazy population and the race-guide static-window quirk are **unchanged** (accepted caveats).
- No frontend changes.
