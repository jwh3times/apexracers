# Demo-Data Preview — Cache Seeding (Plan 2 of 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Light up the `CachedIRacingClient`-backed half of the iRacing surface for the `iracing-demo` preview by seeding `ExternalDataCaches` with synthetic **mapped DTOs** under each service's exact cache keys, plus the persisted `SeasonCarBop` + per-week `Week.WeatherSummaryJson` gaps — so every gated page renders against the shared demo driver while real creds are absent.

**Architecture:** Extend `ApexRacers.Seeder` with a `--demo` step (a `DemoCacheSeeder` + pure builder classes). **Decision (locked with the user):** the Seeder gains a project reference to `ApexRacers.Api` and builds the **real** cached DTO/record types + reuses the real mappers, so the payloads are byte-compatible by construction (zero drift). The cache write path mirrors `CachedIRacingClient` exactly: `JsonSerializer.Serialize(value)` with **default options** into `ExternalDataCache.Payload`, with a far-future `ExpiresAt` sentinel (doubles as the never-expire TTL and the purge marker). No production app code changes — demo lives entirely in *data*.

**Tech Stack:** .NET 10 / EF Core 10 / Npgsql; `Aydsko.iRacingData` (for `WeatherSummary` + `MemberChartType`, reached transitively via the Api reference); xUnit + in-memory SQLite (`DbContextFactory.Create()`).

This is **Plan 2 of 2**. Plan 1 (`2026-06-23-demo-data-preview-infrastructure.md`, merged in PR #62) built the reversible mechanism (the seeded-disabled `iracing-demo` flag, the `MemberContext` demo override, frontend OR-gating, `DemoBanner`, and `purge_demo_data.sql` with a marked Plan-2 extension point). This plan fills the data so the cached pages stop 503-ing. **Only after this ships is it safe to enable `iracing-demo` in production.**

Spec: `docs/superpowers/specs/2026-06-23-demo-data-preview-design.md` (§5 cache seeding, §6 persisted gaps).

## Background facts (verified against the code — do not re-derive)

- **Cache contract:** `CachedIRacingClient.GetOrFetchAsync<T>` (`src/ApexRacers.Api/Services/CachedIRacingClient.cs`) reads `db.ExternalDataCaches.FirstOrDefault(c => c.CacheKey == key)`; if `row.ExpiresAt > now` it returns `JsonSerializer.Deserialize<T>(row.Payload)` (**default options, case-sensitive PascalCase**). A miss with no creds **throws** `IRacingNotConfiguredException` → 503. So a seeded row is a hit iff its `CacheKey` matches verbatim, `ExpiresAt` is in the future, and `Payload` is `JsonSerializer.Serialize(value)` of the real `<T>` with default options.
- **The table is `iracing."ExternalDataCaches"` (plural).** Entity `ExternalDataCache` (`Core/Models`): `Id`, `CacheKey` (unique), `Payload` (text), `FetchedAt` (DateTimeOffset), `ExpiresAt` (DateTimeOffset).
- **Demo identity:** `ApexRacers.Core.DemoData.DriverCustId = 100_001` (Plan 1). The synthetic driver pool the main seeder generates is custIds `100_001..100_200` (`Program.cs`: `DriverStart = 100_001`, `DriverCount = 200`). This plan adds `DemoData.RivalCustId = 100_002`.
- **Exact cache keys + cached `<T>` (verified):**
  | Key | Cached `<T>` | Source |
  | --- | --- | --- |
  | `profile:{custId}` | `ProfileSnapshot` | MemberStatsService |
  | `career:{custId}` | `List<CategoryCareerDto>` | MemberStatsService |
  | `chart:{custId}:{categoryId}:{(int)MemberChartType.IRating}` | `List<TimeSeriesPointDto>` | MemberStatsService |
  | `summary:{custId}` | `ThisYearSummaryDto` | MemberStatsService |
  | `recap:{custId}` | `RecapSnapshot` | MemberStatsService |
  | `awards:{custId}` | `List<AwardDto>` | AchievementsService |
  | `recent:{custId}` | `List<RecentRaceCacheRow>` | RaceHistoryService |
  | `leaderboard:{categoryId}` | `List<GlobalLeaderboardEntryDto>` | LeaderboardService |
  | `standings:{seasonId}:{classId}` | `List<SeasonStandingDto>` | StandingsService |
  | `tt-standings:{seasonId}:{classId}` | `List<SeasonTtStandingDto>` | StandingsService |
  | `qual:{seasonId}:{classId}:{week}` | `List<SeasonQualifyResultDto>` | StandingsService |
  | `race-guide` | `List<RaceGuideCacheRow>` | RaceGuideService |
- **`ProfileSnapshot`, `RecapSnapshot` (MemberStatsService), `RecentRaceCacheRow` (RaceHistoryService), `RaceGuideCacheRow` (RaceGuideService)** are currently `private` nested records — Task 1 promotes them so the Seeder + Tests can build them. `ProfileSnapshot` carries `LicenseSnapshot` (also promoted).
- **DTO constructors (verbatim, from `Api/Dtos/ResponseDtos.cs`):**
  - `TimeSeriesPointDto(string When, int Value)` — `When` is `"yyyy-MM-dd"`.
  - `CategoryCareerDto(int CategoryId, string CategoryName, int Starts, int Wins, int Top5, int Poles, int AvgStartPosition, int AvgFinishPosition, int Laps, int LapsLed, double WinPercentage, double Top5Percentage)`
  - `ThisYearSummaryDto(int OfficialSessions, int OfficialWins, int LeagueSessions, int LeagueWins)`
  - `FavoriteCarDto(int CarId, string CarName, string? ImageUrl)` · `FavoriteTrackDto(int TrackId, string TrackName, string? ConfigName, string? LogoUrl)`
  - `AwardDto(int AwardId, string Name, string? Description, string? GroupName, int Count, DateTimeOffset AwardDate, string? IconUrl, string? IconBackgroundColor, int Progress, int Threshold)`
  - `GlobalLeaderboardEntryDto(int CategoryId, int Rank, long CustId, string Driver, string Location, int Starts, int Wins, int IRating, int TtRating, int ChampPoints)`
  - `SeasonStandingDto(int Rank, long CustId, string DriverName, int Division, int Starts, int Wins, int Top5, int Poles, int Points, double AvgFinishPosition, int Incidents)`
  - `SeasonTtStandingDto(int Rank, long CustId, string DriverName, int Division, int? TtRating, int Starts, int Wins, int Top5, int Poles, int Points, double AvgFinishPosition, int Incidents)`
  - `SeasonQualifyResultDto(int Rank, long CustId, string DriverName, int Division, int? IRating, double BestQualLapSeconds, int Week)`
- **`SeasonCarBop` entity:** `int SeasonId, int WeekNumber, int CarId, double WeightPenaltyKg, double PowerAdjustPct, double MaxPctFuelFill, int MaxDryTireSets` (composite PK SeasonId+WeekNumber+CarId).
- **Weather:** `Week.WeatherSummaryJson` stores `JsonSerializer.Serialize(weatherSummary)` where `weatherSummary` is `Aydsko.iRacingData.Series.WeatherSummary` (worker `Worker.cs:208-210`; reader `ScheduleService.MapWeather`). Proven construction (from `ScheduleServiceTests.cs`): `new WeatherSummary { TemperatureHigh = 28.9m, TemperatureLow = 28.8m, TemperatureUnits = 1, WindHigh = 5.6m, WindLow = 4.8m, WindUnits = 1, PrecipitationChance = 0m, SkiesHigh = 1, SkiesLow = 1 }`.

## Global Constraints

- **Backend coverage:** ≥ 85% line **and** branch. New pure builders + `DemoCache`/`DemoCacheSeeder` get xUnit tests. The Seeder's top-level `Program` is excluded from coverage in Task 1 (mirrors the existing Program exclusions); only the new classes are measured.
- **Cache fidelity (load-bearing):** write `JsonSerializer.Serialize(value)` with **default options** (no `JsonSerializerOptions`) of the **real** cached `<T>`. Keys are the **exact** verbatim strings above (no `demo:` prefix). `ExpiresAt = DemoCache.Sentinel`; `FetchedAt = DateTimeOffset.UtcNow`.
- **Sentinel:** `DemoCache.Sentinel = new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero)`. It must be `>= '9000-01-01'` so the purge (`purge_demo_data.sql`) and `ExternalDataCacheCleanupService` rules match.
- **Determinism:** pure builders take no `DateTimeOffset.UtcNow`; use the fixed `DemoCache.RefDate` constant for any dates inside payloads, so builders are unit-testable.
- **Idempotency:** every seed step upserts by key/PK (safe to re-run, like the rest of the seeder).
- **Reuse real types/mappers — never mirror.** No locally-redefined DTO shapes.
- **NuGet versions** are centrally managed in `Directory.Packages.props` — never add `Version="..."` to a `.csproj`.
- **Do not enable `iracing-demo` in production until this plan ships and `purge_demo_data.sql`'s Plan-2 block is activated (Task 10).**

## File Structure

- `src/ApexRacers.Seeder/ApexRacers.Seeder.csproj` — add `ProjectReference` to Api (Task 1).
- `src/ApexRacers.Tests/ApexRacers.Tests.csproj` — add `ProjectReference` to Seeder (Task 1).
- `coverage.runsettings` — exclude `[ApexRacers.Seeder]Program` (Task 1).
- `src/ApexRacers.Core/DemoData.cs` — add `RivalCustId` (Task 1).
- `src/ApexRacers.Api/Services/MemberStatsService.cs`, `RaceHistoryService.cs`, `RaceGuideService.cs` — promote the private cache-row records to public top-level (Task 1).
- `src/ApexRacers.Seeder/Demo/DemoCache.cs` — sentinel, ref-date, generic upsert (Task 2).
- `src/ApexRacers.Seeder/Demo/DemoMemberData.cs` — pure builders for profile/career/chart/summary/recap (Task 3).
- `src/ApexRacers.Seeder/Demo/DemoActivityData.cs` — pure builders for awards + recent races (Task 4).
- `src/ApexRacers.Seeder/Demo/DemoLeaderboardData.cs` — pure builder for leaderboards (Task 5).
- `src/ApexRacers.Seeder/Demo/DemoStandingsData.cs` — pure builders for standings/TT/qualify (Task 6).
- `src/ApexRacers.Seeder/Demo/DemoRaceGuideData.cs` — pure builder for race guide (Task 7).
- `src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs` — orchestrates all cache + persisted writes; DB reads for seasons/classes/weeks/cars (Tasks 3–9).
- `src/ApexRacers.Seeder/Program.cs` — `--demo` arg → invoke `DemoCacheSeeder` (Task 9).
- `src/ApexRacers.Data/Seeds/purge_demo_data.sql` — activate the Plan-2 block (Task 10).
- Tests: `src/ApexRacers.Tests/Seeder/*` — one test file per builder + a `DemoCacheSeeder` integration test.

---

### Task 1: Foundation — project refs, promote cache-row records, RivalCustId, coverage exclude

**Files:**
- Modify: `src/ApexRacers.Seeder/ApexRacers.Seeder.csproj`
- Modify: `src/ApexRacers.Tests/ApexRacers.Tests.csproj`
- Modify: `coverage.runsettings`
- Modify: `src/ApexRacers.Core/DemoData.cs`
- Modify: `src/ApexRacers.Api/Services/MemberStatsService.cs`, `RaceHistoryService.cs`, `RaceGuideService.cs`

**Interfaces:**
- Produces: `DemoData.RivalCustId` (`public const long` = `100_002`). Public records `ProfileSnapshot`, `LicenseSnapshot`, `RecapSnapshot`, `RecentRaceCacheRow`, `RaceGuideCacheRow` (same property lists as before, now top-level public in their service files) — consumed by every later task's builders.

- [ ] **Step 1: Add the project references**

In `src/ApexRacers.Seeder/ApexRacers.Seeder.csproj`, add to the existing `<ItemGroup>` of project references:
```xml
    <ProjectReference Include="..\ApexRacers.Api\ApexRacers.Api.csproj" />
```
In `src/ApexRacers.Tests/ApexRacers.Tests.csproj`, add to its project-references `<ItemGroup>`:
```xml
    <ProjectReference Include="..\ApexRacers.Seeder\ApexRacers.Seeder.csproj" />
```

- [ ] **Step 2: Exclude the Seeder's Program from coverage**

In `coverage.runsettings`, add two lines inside `<Exclude>` (after the Ingestion entries):
```
            [ApexRacers.Seeder]Program,
            [ApexRacers.Seeder]<Program>$
```
(Top-level statements compile to `Program`/`<Program>$`; excluding both keeps the existing 700-line CLI out of coverage so only the new `Demo/*` classes are measured.)

- [ ] **Step 3: Add the rival cust id**

In `src/ApexRacers.Core/DemoData.cs`, add inside the class:
```csharp
    /// <summary>A second synthetic pool driver used as the demo /compare rival.</summary>
    public const long RivalCustId = 100_002;
```

- [ ] **Step 4: Promote the private cache-row records to public top-level**

In `src/ApexRacers.Api/Services/MemberStatsService.cs`, delete the `private sealed record ProfileSnapshot(...)`, `private sealed record LicenseSnapshot(...)`, and `private sealed record RecapSnapshot(...)` declarations from inside the class, and add them as public top-level records at the end of the file (after the class), keeping identical property lists:
```csharp
/// <summary>SDK-decoupled cache snapshot for <c>profile:{custId}</c> (public so the demo seeder can build it).</summary>
public sealed record ProfileSnapshot(
    string DisplayName, string? FlairName, string? FlairShortName, string? MemberSince,
    IReadOnlyList<LicenseSnapshot> Licenses);

public sealed record LicenseSnapshot(
    int CategoryId, string? Category, int Irating, double SafetyRating, double Cpi,
    int LicenseLevel, string GroupName, int TtRating, string Color);

/// <summary>SDK-decoupled cache snapshot for <c>recap:{custId}</c>.</summary>
public sealed record RecapSnapshot(FavoriteCarDto? FavoriteCar, FavoriteTrackDto? FavoriteTrack);
```
(`FavoriteCarDto`/`FavoriteTrackDto` are already in `ApexRacers.Api.Dtos`, which `MemberStatsService` already uses.)

In `src/ApexRacers.Api/Services/RaceHistoryService.cs`, move `RecentRaceCacheRow` from `private sealed record` nested → public top-level (after the class), identical fields:
```csharp
/// <summary>SDK-decoupled cache row for <c>recent:{custId}</c> (public so the demo seeder can build it).</summary>
public sealed record RecentRaceCacheRow(
    int SubsessionId, DateTimeOffset SessionStartTime, string SeriesName, string TrackName,
    int CarId, int StartPosition, int FinishPosition, int Incidents,
    int IRatingDelta, double SrDelta, int StrengthOfField, int Points);
```
In `src/ApexRacers.Api/Services/RaceGuideService.cs`, move `RaceGuideCacheRow` to public top-level (after the class):
```csharp
/// <summary>SDK-decoupled cache row for <c>race-guide</c> (public so the demo seeder can build it).</summary>
public sealed record RaceGuideCacheRow(
    int SeriesId, DateTimeOffset Start, DateTimeOffset End, int EntryCount, int RaceWeekNumber);
```

- [ ] **Step 5: Build and run the existing suite (no behavior change)**

Run:
```bash
dotnet build
dotnet test src/ApexRacers.Tests
```
Expected: solution builds; all existing tests pass. (Promotions are visibility-only — serialized JSON is unchanged because System.Text.Json serializes the same public properties.)

- [ ] **Step 6: Commit**

```bash
git add src/ApexRacers.Seeder/ApexRacers.Seeder.csproj src/ApexRacers.Tests/ApexRacers.Tests.csproj coverage.runsettings src/ApexRacers.Core/DemoData.cs src/ApexRacers.Api/Services/MemberStatsService.cs src/ApexRacers.Api/Services/RaceHistoryService.cs src/ApexRacers.Api/Services/RaceGuideService.cs
git commit -m "feat(demo): wire Seeder->Api, promote cache-row records, add RivalCustId"
```

---

### Task 2: `DemoCache` — sentinel, ref-date, and idempotent cache upsert

**Files:**
- Create: `src/ApexRacers.Seeder/Demo/DemoCache.cs`
- Test: `src/ApexRacers.Tests/Seeder/DemoCacheTests.cs`

**Interfaces:**
- Produces: `DemoCache.Sentinel` (`DateTimeOffset`), `DemoCache.RefDate` (`DateTimeOffset`), `DemoCache.UpsertAsync<T>(AppDbContext db, string key, T value, CancellationToken ct)`. Consumed by every later seed step.

- [ ] **Step 1: Write the failing test**

Create `src/ApexRacers.Tests/Seeder/DemoCacheTests.cs`:
```csharp
using System.Text.Json;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoCacheTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Sample(int A, string B);

    [Fact]
    public async Task UpsertAsync_InsertsRow_WithSentinelExpiry_AndRoundTrips()
    {
        await using var db = DbContextFactory.Create();

        await DemoCache.UpsertAsync(db, "sample:1", new Sample(7, "x"), Ct);

        var row = await db.ExternalDataCaches.SingleAsync(Ct);
        Assert.Equal("sample:1", row.CacheKey);
        Assert.Equal(DemoCache.Sentinel, row.ExpiresAt);
        Assert.Equal(new Sample(7, "x"), JsonSerializer.Deserialize<Sample>(row.Payload));
    }

    [Fact]
    public async Task UpsertAsync_SameKeyTwice_UpdatesInPlace_NoDuplicate()
    {
        await using var db = DbContextFactory.Create();

        await DemoCache.UpsertAsync(db, "sample:1", new Sample(1, "a"), Ct);
        await DemoCache.UpsertAsync(db, "sample:1", new Sample(2, "b"), Ct);

        var row = await db.ExternalDataCaches.SingleAsync(Ct);
        Assert.Equal(new Sample(2, "b"), JsonSerializer.Deserialize<Sample>(row.Payload));
    }

    [Fact]
    public void Sentinel_IsBeyondPurgeMarker() =>
        Assert.True(DemoCache.Sentinel >= new DateTimeOffset(9000, 1, 1, 0, 0, 0, TimeSpan.Zero));
}
```

- [ ] **Step 2: Run to verify it fails**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoCacheTests"
```
Expected: compile error — `ApexRacers.Seeder.Demo.DemoCache` does not exist.

- [ ] **Step 3: Implement `DemoCache`**

Create `src/ApexRacers.Seeder/Demo/DemoCache.cs`:
```csharp
using System.Text.Json;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Seeder.Demo;

/// <summary>
/// Writes synthetic demo rows into <c>ExternalDataCaches</c> so the CachedIRacingClient-backed
/// endpoints serve them as hits while real iRacing creds are absent. Payloads are serialized with
/// System.Text.Json default options — identical to <c>CachedIRacingClient.GetOrFetchAsync</c> — so
/// the real services deserialize them as their own cached <c>T</c>.
/// </summary>
public static class DemoCache
{
    /// <summary>Far-future expiry: never treated as a miss; also the purge marker (>= 9000-01-01).</summary>
    public static readonly DateTimeOffset Sentinel = new(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Fixed reference date for deterministic payload dates (keeps builders unit-testable).</summary>
    public static readonly DateTimeOffset RefDate = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task UpsertAsync<T>(AppDbContext db, string key, T value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value);
        var row = await db.ExternalDataCaches.FirstOrDefaultAsync(c => c.CacheKey == key, ct);
        if (row is null)
        {
            db.ExternalDataCaches.Add(new ExternalDataCache
            {
                CacheKey = key,
                Payload = json,
                FetchedAt = DateTimeOffset.UtcNow,
                ExpiresAt = Sentinel,
            });
        }
        else
        {
            row.Payload = json;
            row.FetchedAt = DateTimeOffset.UtcNow;
            row.ExpiresAt = Sentinel;
        }
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoCacheTests"
```
Expected: 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ApexRacers.Seeder/Demo/DemoCache.cs src/ApexRacers.Tests/Seeder/DemoCacheTests.cs
git commit -m "feat(demo): DemoCache sentinel + idempotent ExternalDataCaches upsert"
```

---

### Task 3: Member entries — progression / profile-stats / compare

**Files:**
- Create: `src/ApexRacers.Seeder/Demo/DemoMemberData.cs`
- Create: `src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs` (start it here; later tasks extend it)
- Test: `src/ApexRacers.Tests/Seeder/DemoMemberDataTests.cs`, `src/ApexRacers.Tests/Seeder/DemoCacheSeederMemberTests.cs`

**Interfaces:**
- Consumes: `ProfileSnapshot`/`LicenseSnapshot`/`RecapSnapshot` (Task 1), `DemoCache` (Task 2), `DemoData.DriverCustId`/`RivalCustId`.
- Produces: `DemoMemberData` pure builders + `DemoCategory` list; `DemoCacheSeeder.SeedMembersAsync(AppDbContext db, CancellationToken ct)`.

The demo driver and rival hold three license categories: Sports Car (5), Formula Car (6), Oval (1). These drive `profile.Licenses`, the per-category `chart:` history, progression cards, and compare history.

- [ ] **Step 1: Write the failing builder test**

Create `src/ApexRacers.Tests/Seeder/DemoMemberDataTests.cs`:
```csharp
using ApexRacers.Api.Services;
using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoMemberDataTests
{
    [Fact]
    public void BuildProfile_DemoDriver_HasThreeLicensesAndIdentity()
    {
        var p = DemoMemberData.BuildProfile(100_001);
        Assert.False(string.IsNullOrWhiteSpace(p.DisplayName));
        Assert.Equal(3, p.Licenses.Count);
        Assert.Contains(p.Licenses, l => l.CategoryId == 5); // Sports Car
        Assert.All(p.Licenses, l => Assert.True(l.Irating > 0));
    }

    [Fact]
    public void BuildChart_IsAscendingDatedHistory()
    {
        var pts = DemoMemberData.BuildChart(100_001, 5);
        Assert.True(pts.Count >= 8);
        Assert.All(pts, p => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", p.When));
        Assert.True(pts[^1].Value >= pts[0].Value); // trends up
    }

    [Fact]
    public void Builders_AreDeterministic()
    {
        Assert.Equal(DemoMemberData.BuildProfile(100_001), DemoMemberData.BuildProfile(100_001));
        Assert.Equal(DemoMemberData.BuildCareer(100_001), DemoMemberData.BuildCareer(100_001));
    }

    [Fact]
    public void RivalDiffersFromDemoDriver()
    {
        Assert.NotEqual(
            DemoMemberData.BuildProfile(100_001).DisplayName,
            DemoMemberData.BuildProfile(100_002).DisplayName);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoMemberDataTests"
```
Expected: compile error — `DemoMemberData` does not exist.

- [ ] **Step 3: Implement `DemoMemberData`**

Create `src/ApexRacers.Seeder/Demo/DemoMemberData.cs`:
```csharp
using System.Globalization;
using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;

namespace ApexRacers.Seeder.Demo;

/// <summary>One demo license category: id, slug (PrettifyCategory-friendly), license group/level/color.</summary>
public sealed record DemoCategory(int Id, string Slug, string GroupName, int LicenseLevel, string Color);

/// <summary>
/// Pure builders for the member cache snapshots/DTOs (profile / career / chart / summary / recap).
/// Deterministic per custId so re-running the seeder is stable and the builders are unit-testable.
/// Builds the REAL cached types (no mirroring), so payloads are byte-compatible by construction.
/// </summary>
public static class DemoMemberData
{
    // Sports Car, Formula Car, Oval — the categories the demo driver + rival are rated in.
    public static readonly IReadOnlyList<DemoCategory> Categories =
    [
        new(5, "sports_car",  "Class A", 20, "#fc8a27"),
        new(6, "formula_car", "Class B", 16, "#feec04"),
        new(1, "oval",        "Class C", 12, "#53b14f"),
    ];

    private static int BaseIrating(long custId, int categoryId)
    {
        // Demo driver a touch stronger than the rival; varies a little by category.
        var driverBonus = custId == 100_001 ? 350 : 0;
        return 1800 + driverBonus + categoryId * 40;
    }

    public static ProfileSnapshot BuildProfile(long custId)
    {
        var licenses = Categories
            .Select(c => new LicenseSnapshot(
                c.Id, c.Slug,
                Irating: BaseIrating(custId, c.Id),
                SafetyRating: 3.5,
                Cpi: 80.0,
                LicenseLevel: c.LicenseLevel,
                GroupName: c.GroupName,
                TtRating: 1500 + c.Id * 10,
                Color: c.Color))
            .ToList();

        return new ProfileSnapshot(
            DisplayName: custId == 100_001 ? "Demo Driver" : "Rival Racer",
            FlairName: "United States",
            FlairShortName: "USA",
            MemberSince: "2019-03-01",
            Licenses: licenses);
    }

    public static List<CategoryCareerDto> BuildCareer(long custId) =>
        Categories
            .Select(c => new CategoryCareerDto(
                c.Id, MemberStatsService.PrettifyCategory(c.Slug),
                Starts: 240, Wins: 18, Top5: 96, Poles: 12,
                AvgStartPosition: 8, AvgFinishPosition: 6,
                Laps: 7400, LapsLed: 320,
                WinPercentage: 7.5, Top5Percentage: 40.0))
            .ToList();

    public static List<TimeSeriesPointDto> BuildChart(long custId, int categoryId)
    {
        var start = BaseIrating(custId, categoryId) - 700;
        return Enumerable.Range(0, 12)
            .Select(i => new TimeSeriesPointDto(
                DemoCache.RefDate.AddDays(-7 * (11 - i)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                start + i * 60))
            .ToList();
    }

    public static ThisYearSummaryDto BuildSummary(long custId) =>
        new(OfficialSessions: 84, OfficialWins: 9, LeagueSessions: 12, LeagueWins: 3);

    public static RecapSnapshot BuildRecap(long custId) =>
        new(
            new FavoriteCarDto(132, "BMW M4 GT3", null),
            new FavoriteTrackDto(47, "Laguna Seca", "", null));
}
```

- [ ] **Step 4: Run to verify the builder test passes**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoMemberDataTests"
```
Expected: all 4 tests PASS.

- [ ] **Step 5: Write the seeding integration test**

Create `src/ApexRacers.Tests/Seeder/DemoCacheSeederMemberTests.cs`:
```csharp
using System.Text.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoCacheSeederMemberTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SeedMembersAsync_WritesProfileChartCareerForDemoAndRival()
    {
        await using var db = DbContextFactory.Create();

        await new DemoCacheSeeder(db).SeedMembersAsync(Ct);

        // Demo driver profile is a hit and round-trips as the real ProfileSnapshot.
        var profileRow = await db.ExternalDataCaches
            .SingleAsync(c => c.CacheKey == $"profile:{DemoData.DriverCustId}", Ct);
        var profile = JsonSerializer.Deserialize<ProfileSnapshot>(profileRow.Payload)!;
        Assert.Equal("Demo Driver", profile.DisplayName);
        Assert.Equal(3, profile.Licenses.Count);

        // Per-category iRating chart key uses (int)MemberChartType.IRating.
        var chartKey = $"chart:{DemoData.DriverCustId}:5:{(int)Aydsko.iRacingData.Member.MemberChartType.IRating}";
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == chartKey, Ct));

        // Demo-only entries.
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == $"summary:{DemoData.DriverCustId}", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == $"recap:{DemoData.DriverCustId}", Ct));

        // Rival has profile + career + chart (for /compare) but NOT summary/recap.
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == $"profile:{DemoData.RivalCustId}", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == $"career:{DemoData.RivalCustId}", Ct));
        Assert.False(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == $"summary:{DemoData.RivalCustId}", Ct));
    }
}
```

- [ ] **Step 6: Implement `DemoCacheSeeder.SeedMembersAsync`**

Create `src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs`:
```csharp
using ApexRacers.Core;
using ApexRacers.Data;
using Aydsko.iRacingData.Member;

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
}
```

- [ ] **Step 7: Run both test files**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoMemberDataTests|FullyQualifiedName~DemoCacheSeederMemberTests"
```
Expected: all PASS.

- [ ] **Step 8: Commit**

```bash
git add src/ApexRacers.Seeder/Demo/DemoMemberData.cs src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs src/ApexRacers.Tests/Seeder/DemoMemberDataTests.cs src/ApexRacers.Tests/Seeder/DemoCacheSeederMemberTests.cs
git commit -m "feat(demo): seed member cache entries (progression/profile/compare)"
```

---

### Task 4: Achievements + Race History entries

**Files:**
- Create: `src/ApexRacers.Seeder/Demo/DemoActivityData.cs`
- Modify: `src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs` (add `SeedActivityAsync`)
- Test: `src/ApexRacers.Tests/Seeder/DemoActivityDataTests.cs`

**Interfaces:**
- Consumes: `AwardDto`, `RecentRaceCacheRow` (Task 1), `DemoCache`.
- Produces: `DemoActivityData.BuildAwards(long)` → `List<AwardDto>`; `DemoActivityData.BuildRecentRaces(long)` → `List<RecentRaceCacheRow>`; `DemoCacheSeeder.SeedActivityAsync(ct)` (writes `awards:{demo}`, `recent:{demo}`).

`awards:` and `recent:` are read only for the authenticated demo driver, so seed for `DemoData.DriverCustId` only.

- [ ] **Step 1: Write the failing test**

Create `src/ApexRacers.Tests/Seeder/DemoActivityDataTests.cs`:
```csharp
using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoActivityDataTests
{
    [Fact]
    public void BuildAwards_NonEmpty_NewestFirst_WithThresholds()
    {
        var awards = DemoActivityData.BuildAwards(100_001);
        Assert.NotEmpty(awards);
        for (var i = 1; i < awards.Count; i++)
            Assert.True(awards[i - 1].AwardDate >= awards[i].AwardDate); // newest first
        Assert.All(awards, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
    }

    [Fact]
    public void BuildRecentRaces_NonEmpty_WithDeltasAndRealCarIds()
    {
        var races = DemoActivityData.BuildRecentRaces(100_001);
        Assert.NotEmpty(races);
        Assert.All(races, r => Assert.True(r.SubsessionId < 0)); // synthetic negative ids
        Assert.Contains(races, r => r.IRatingDelta != 0);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoActivityDataTests"
```
Expected: compile error — `DemoActivityData` does not exist.

- [ ] **Step 3: Implement `DemoActivityData`**

Create `src/ApexRacers.Seeder/Demo/DemoActivityData.cs`:
```csharp
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
    // removed by purge_demo_data.sql (Id < 0). Car 132 (BMW M4 GT3) is in the seeded catalog.
    public static List<RecentRaceCacheRow> BuildRecentRaces(long custId) =>
        Enumerable.Range(0, 6)
            .Select(i => new RecentRaceCacheRow(
                SubsessionId: -900_000 - i,
                SessionStartTime: DemoCache.RefDate.AddDays(-i * 3),
                SeriesName: "GT3 Challenge Fixed",
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
```

- [ ] **Step 4: Add `SeedActivityAsync` to `DemoCacheSeeder`**

Append the method to `DemoCacheSeeder`:
```csharp
    /// <summary>awards + recent races for the authenticated demo driver only.</summary>
    public async Task SeedActivityAsync(CancellationToken ct)
    {
        await DemoCache.UpsertAsync(db, $"awards:{DemoData.DriverCustId}", DemoActivityData.BuildAwards(DemoData.DriverCustId), ct);
        await DemoCache.UpsertAsync(db, $"recent:{DemoData.DriverCustId}", DemoActivityData.BuildRecentRaces(DemoData.DriverCustId), ct);
    }
```

- [ ] **Step 5: Run to verify it passes**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoActivityDataTests"
```
Expected: 2 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ApexRacers.Seeder/Demo/DemoActivityData.cs src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs src/ApexRacers.Tests/Seeder/DemoActivityDataTests.cs
git commit -m "feat(demo): seed achievements + race-history cache entries"
```

---

### Task 5: Leaderboards (categories 1–6)

**Files:**
- Create: `src/ApexRacers.Seeder/Demo/DemoLeaderboardData.cs`
- Modify: `src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs` (add `SeedLeaderboardsAsync`)
- Test: `src/ApexRacers.Tests/Seeder/DemoLeaderboardDataTests.cs`

**Interfaces:**
- Produces: `DemoLeaderboardData.Build(int categoryId)` → `List<GlobalLeaderboardEntryDto>` (ranked, ~60 entries; the demo driver is inserted in the categories they hold — 5/6/1); `DemoCacheSeeder.SeedLeaderboardsAsync(ct)` writes `leaderboard:1`..`leaderboard:6`.

- [ ] **Step 1: Write the failing test**

Create `src/ApexRacers.Tests/Seeder/DemoLeaderboardDataTests.cs`:
```csharp
using ApexRacers.Core;
using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoLeaderboardDataTests
{
    [Fact]
    public void Build_IsRankedDescendingByIRating_FromRankOne()
    {
        var rows = DemoLeaderboardData.Build(5);
        Assert.True(rows.Count >= 50);
        Assert.Equal(1, rows[0].Rank);
        for (var i = 1; i < rows.Count; i++)
        {
            Assert.Equal(i + 1, rows[i].Rank);
            Assert.True(rows[i - 1].IRating >= rows[i].IRating);
        }
        Assert.All(rows, r => Assert.Equal(5, r.CategoryId));
    }

    [Fact]
    public void Build_RatedCategory_IncludesDemoDriver()
    {
        Assert.Contains(DemoLeaderboardData.Build(5), r => r.CustId == DemoData.DriverCustId);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoLeaderboardDataTests"
```
Expected: compile error — `DemoLeaderboardData` does not exist.

- [ ] **Step 3: Implement `DemoLeaderboardData`**

Create `src/ApexRacers.Seeder/Demo/DemoLeaderboardData.cs`:
```csharp
using ApexRacers.Api.Dtos;
using ApexRacers.Core;

namespace ApexRacers.Seeder.Demo;

/// <summary>Pure builder for a category's global iRating leaderboard. The demo driver appears
/// in the categories they hold (5/6/1) so LeaderboardsPage can highlight "your" row.</summary>
public static class DemoLeaderboardData
{
    private static readonly int[] RatedCategories = [5, 6, 1];
    private static readonly string[] Locations = ["United States", "United Kingdom", "Germany", "Australia", "Brazil"];

    public static List<GlobalLeaderboardEntryDto> Build(int categoryId)
    {
        const int count = 60;
        var entries = Enumerable.Range(0, count)
            .Select(i =>
            {
                var custId = 200_000L + categoryId * 1000 + i; // distinct synthetic pool, != driver pool
                return (CustId: custId, IRating: 9000 - i * 110);
            })
            .ToList();

        // Drop the demo driver into rated categories at a strong (but not #1) position.
        if (RatedCategories.Contains(categoryId))
            entries[3] = (DemoData.DriverCustId, entries[3].IRating);

        return entries
            .OrderByDescending(e => e.IRating)
            .Select((e, i) => new GlobalLeaderboardEntryDto(
                CategoryId: categoryId,
                Rank: i + 1,
                CustId: e.CustId,
                Driver: e.CustId == DemoData.DriverCustId ? "Demo Driver" : $"Driver {e.CustId}",
                Location: Locations[(int)(e.CustId % Locations.Length)],
                Starts: 500 - i * 4,
                Wins: 80 - i,
                IRating: e.IRating,
                TtRating: e.IRating - 500,
                ChampPoints: 4000 - i * 40))
            .ToList();
    }
}
```

- [ ] **Step 4: Add `SeedLeaderboardsAsync` to `DemoCacheSeeder`**

```csharp
    /// <summary>leaderboard:1..6 (the API allows category 1-6; default 5).</summary>
    public async Task SeedLeaderboardsAsync(CancellationToken ct)
    {
        for (var categoryId = 1; categoryId <= 6; categoryId++)
            await DemoCache.UpsertAsync(db, $"leaderboard:{categoryId}", DemoLeaderboardData.Build(categoryId), ct);
    }
```

- [ ] **Step 5: Run to verify it passes**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoLeaderboardDataTests"
```
Expected: 2 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ApexRacers.Seeder/Demo/DemoLeaderboardData.cs src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs src/ApexRacers.Tests/Seeder/DemoLeaderboardDataTests.cs
git commit -m "feat(demo): seed global leaderboard cache entries (categories 1-6)"
```

---

### Task 6: Standings + Time Trial + Qualifying (per active season × class [× week])

**Files:**
- Create: `src/ApexRacers.Seeder/Demo/DemoStandingsData.cs`
- Modify: `src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs` (add `SeedStandingsAsync` — reads active seasons / SeasonCarClasses / weeks)
- Test: `src/ApexRacers.Tests/Seeder/DemoStandingsDataTests.cs`, `src/ApexRacers.Tests/Seeder/DemoCacheSeederStandingsTests.cs`

**Interfaces:**
- Produces: pure `DemoStandingsData.BuildStandings(int seasonId, int classId)` → `List<SeasonStandingDto>`; `BuildTtStandings(...)` → `List<SeasonTtStandingDto>`; `BuildQualify(int seasonId, int classId, int week)` → `List<SeasonQualifyResultDto>` (each includes the demo driver with a `Division`). `DemoCacheSeeder.SeedStandingsAsync(ct)`.
- Consumes: `db.Seasons` (Active), `db.SeasonCarClasses`, `db.Weeks`.

`StandingsService` keys on the active season id + chosen car class id (`standings:{seasonId}:{classId}`, `tt-standings:{seasonId}:{classId}`, `qual:{seasonId}:{classId}:{week}`). The caller's "division" badge is read client-side from the demo driver's own row, so each list must contain `DemoData.DriverCustId` with a `Division`.

- [ ] **Step 1: Write the failing builder test**

Create `src/ApexRacers.Tests/Seeder/DemoStandingsDataTests.cs`:
```csharp
using ApexRacers.Core;
using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoStandingsDataTests
{
    [Fact]
    public void BuildStandings_RankedFromOne_IncludesDemoDriverWithDivision()
    {
        var rows = DemoStandingsData.BuildStandings(6115, 100);
        Assert.Equal(1, rows[0].Rank);
        for (var i = 1; i < rows.Count; i++) Assert.Equal(i + 1, rows[i].Rank);
        var me = Assert.Single(rows, r => r.CustId == DemoData.DriverCustId);
        Assert.True(me.Division >= 1);
    }

    [Fact]
    public void BuildQualify_HasWeekAndSortedLapTimes()
    {
        var rows = DemoStandingsData.BuildQualify(6115, 100, week: 2);
        Assert.All(rows, r => Assert.Equal(2, r.Week));
        for (var i = 1; i < rows.Count; i++)
            Assert.True(rows[i - 1].BestQualLapSeconds <= rows[i].BestQualLapSeconds);
        Assert.Contains(rows, r => r.CustId == DemoData.DriverCustId);
    }

    [Fact]
    public void BuildTtStandings_HasTtRating() =>
        Assert.All(DemoStandingsData.BuildTtStandings(6115, 100), r => Assert.NotNull(r.TtRating));
}
```

- [ ] **Step 2: Run to verify it fails**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoStandingsDataTests"
```
Expected: compile error — `DemoStandingsData` does not exist.

- [ ] **Step 3: Implement `DemoStandingsData`**

Create `src/ApexRacers.Seeder/Demo/DemoStandingsData.cs`:
```csharp
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
                Rank: i + 1,
                CustId: CustAt(seasonId, classId, i),
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
                Rank: i + 1,
                CustId: CustAt(seasonId, classId, i),
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
                Rank: i + 1,
                CustId: CustAt(seasonId, classId, i),
                DriverName: NameAt(CustAt(seasonId, classId, i)),
                Division: 1 + i % 5,
                IRating: 3000 - i * 45,
                BestQualLapSeconds: 84.0 + i * 0.12,
                Week: week))
            .ToList();
}
```

- [ ] **Step 4: Write the seeding integration test**

Create `src/ApexRacers.Tests/Seeder/DemoCacheSeederStandingsTests.cs`:
```csharp
using ApexRacers.Core.Models;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoCacheSeederStandingsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SeedStandingsAsync_KeysPerActiveSeasonClassAndWeek()
    {
        await using var db = DbContextFactory.Create();
        db.Seasons.Add(new Season { Id = 6115, SeriesId = 444, Active = true, Year = 2026, Quarter = 2 });
        db.Seasons.Add(new Season { Id = 7000, SeriesId = 9, Active = false, Year = 2025, Quarter = 1 }); // inactive → skipped
        db.SeasonCarClasses.Add(new SeasonCarClass { SeasonId = 6115, CarClassId = 100 });
        db.Weeks.Add(new Week { Id = Guid.NewGuid(), SeasonId = 6115, WeekNumber = 0, TrackId = 1 });
        db.Weeks.Add(new Week { Id = Guid.NewGuid(), SeasonId = 6115, WeekNumber = 1, TrackId = 1 });
        await db.SaveChangesAsync(Ct);

        await new DemoCacheSeeder(db).SeedStandingsAsync(Ct);

        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == "standings:6115:100", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == "tt-standings:6115:100", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == "qual:6115:100:0", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == "qual:6115:100:1", Ct));
        Assert.False(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey.StartsWith("standings:7000:"), Ct));
    }
}
```

- [ ] **Step 5: Add `SeedStandingsAsync` to `DemoCacheSeeder`**

Add (needs `using Microsoft.EntityFrameworkCore;`):
```csharp
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
```

- [ ] **Step 6: Run both test files**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoStandingsDataTests|FullyQualifiedName~DemoCacheSeederStandingsTests"
```
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ApexRacers.Seeder/Demo/DemoStandingsData.cs src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs src/ApexRacers.Tests/Seeder/DemoStandingsDataTests.cs src/ApexRacers.Tests/Seeder/DemoCacheSeederStandingsTests.cs
git commit -m "feat(demo): seed standings / time-trial / qualifying cache entries"
```

---

### Task 7: Race Guide

**Files:**
- Create: `src/ApexRacers.Seeder/Demo/DemoRaceGuideData.cs`
- Modify: `src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs` (add `SeedRaceGuideAsync` — reads active series ids)
- Test: `src/ApexRacers.Tests/Seeder/DemoRaceGuideDataTests.cs`

**Interfaces:**
- Produces: `DemoRaceGuideData.Build(IReadOnlyList<int> seriesIds)` → `List<RaceGuideCacheRow>`; `DemoCacheSeeder.SeedRaceGuideAsync(ct)` writes `race-guide`.

**Time-window note (intentional):** `RaceGuideService` filters cached rows by `End > now && Start <= now + 3h` per request. Because the demo row never refetches (sentinel TTL), seed an **always-live window** — `Start` far in the past, `End` far in the future — so each session always passes the filter and the board/`/live` page stays populated. Consequence: sessions render as "in progress" and the `NotificationsBell` "starting ≤30 min" alert won't fire for them (documented demo quirk).

- [ ] **Step 1: Write the failing test**

Create `src/ApexRacers.Tests/Seeder/DemoRaceGuideDataTests.cs`:
```csharp
using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoRaceGuideDataTests
{
    [Fact]
    public void Build_AlwaysLiveWindow_PassesTheNowFilter()
    {
        var rows = DemoRaceGuideData.Build([444, 9]);
        Assert.Equal(2, rows.Count);
        var now = DateTimeOffset.UtcNow;
        Assert.All(rows, r =>
        {
            Assert.True(r.End > now);                       // not yet ended
            Assert.True(r.Start <= now + TimeSpan.FromHours(3)); // within the horizon
            Assert.True(r.EntryCount > 0);
        });
        Assert.Equal(new[] { 444, 9 }, rows.Select(r => r.SeriesId));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoRaceGuideDataTests"
```
Expected: compile error — `DemoRaceGuideData` does not exist.

- [ ] **Step 3: Implement `DemoRaceGuideData`**

Create `src/ApexRacers.Seeder/Demo/DemoRaceGuideData.cs`:
```csharp
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
```

- [ ] **Step 4: Add `SeedRaceGuideAsync` to `DemoCacheSeeder`**

```csharp
    /// <summary>race-guide for the active series (always-live window — see DemoRaceGuideData).</summary>
    public async Task SeedRaceGuideAsync(CancellationToken ct)
    {
        var seriesIds = await db.Seasons.Where(s => s.Active).Select(s => s.SeriesId).Distinct().ToListAsync(ct);
        await DemoCache.UpsertAsync(db, "race-guide", DemoRaceGuideData.Build(seriesIds), ct);
    }
```

- [ ] **Step 5: Run to verify it passes**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoRaceGuideDataTests"
```
Expected: 1 test PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ApexRacers.Seeder/Demo/DemoRaceGuideData.cs src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs src/ApexRacers.Tests/Seeder/DemoRaceGuideDataTests.cs
git commit -m "feat(demo): seed race-guide cache entry (always-live window)"
```

---

### Task 8: Persisted BoP + per-week weather

**Files:**
- Create: `src/ApexRacers.Seeder/Demo/DemoScheduleData.cs`
- Modify: `src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs` (add `SeedBopAndWeatherAsync`)
- Test: `src/ApexRacers.Tests/Seeder/DemoScheduleDataTests.cs`, `src/ApexRacers.Tests/Seeder/DemoCacheSeederScheduleTests.cs`

**Interfaces:**
- Produces: pure `DemoScheduleData.BuildBop(int seasonId, int week, int carId)` → `SeasonCarBop`; `DemoScheduleData.WeatherJson()` → `string` (a serialized `WeatherSummary`). `DemoCacheSeeder.SeedBopAndWeatherAsync(ct)` — for each active season: set `Week.WeatherSummaryJson` on weeks missing it, and upsert a `SeasonCarBop` per (week × season car) missing one.
- Consumes: `db.Seasons` (Active), `db.Weeks`, `db.SeasonCars`, `db.SeasonCarBops`.

These are the §6 gaps (Strategy/Schedule render thin without them). BoP is keyed (SeasonId, WeekNumber, CarId); weather is the `Week.WeatherSummaryJson` column.

- [ ] **Step 1: Write the failing builder test**

Create `src/ApexRacers.Tests/Seeder/DemoScheduleDataTests.cs`:
```csharp
using ApexRacers.Api.Services;
using ApexRacers.Seeder.Demo;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoScheduleDataTests
{
    [Fact]
    public void WeatherJson_RoundTripsThroughMapWeather()
    {
        var w = ScheduleService.MapWeather(DemoScheduleData.WeatherJson());
        Assert.NotNull(w);
        Assert.True(w!.TempHighC > 0);
    }

    [Fact]
    public void BuildBop_HasCompositeKeyAndPlausibleValues()
    {
        var bop = DemoScheduleData.BuildBop(6115, 2, 132);
        Assert.Equal(6115, bop.SeasonId);
        Assert.Equal(2, bop.WeekNumber);
        Assert.Equal(132, bop.CarId);
        Assert.True(bop.MaxPctFuelFill is > 0 and <= 100);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoScheduleDataTests"
```
Expected: compile error — `DemoScheduleData` does not exist.

- [ ] **Step 3: Implement `DemoScheduleData`**

Create `src/ApexRacers.Seeder/Demo/DemoScheduleData.cs`:
```csharp
using System.Text.Json;
using ApexRacers.Core.Models;
using Aydsko.iRacingData.Series;

namespace ApexRacers.Seeder.Demo;

/// <summary>Pure builders for the persisted schedule gaps: per-week weather (a serialized
/// Aydsko WeatherSummary, matching ScheduleService.MapWeather) and per-car BoP rows.</summary>
public static class DemoScheduleData
{
    /// <summary>A fixed warm-dry forecast serialized exactly as the worker stores it.</summary>
    public static string WeatherJson() => JsonSerializer.Serialize(new WeatherSummary
    {
        TemperatureHigh = 26.0m,
        TemperatureLow = 21.0m,
        TemperatureUnits = 1, // Celsius
        WindHigh = 4.5m,
        WindLow = 2.0m,
        WindUnits = 1, // m/s
        PrecipitationChance = 10m,
        SkiesHigh = 1,
        SkiesLow = 1,
    });

    /// <summary>Deterministic per-car BoP: spreads weight/power a little by car id.</summary>
    public static SeasonCarBop BuildBop(int seasonId, int week, int carId) => new()
    {
        SeasonId = seasonId,
        WeekNumber = week,
        CarId = carId,
        WeightPenaltyKg = carId % 3 * 5,        // 0/5/10
        PowerAdjustPct = -(carId % 4) * 0.5,    // 0 to -1.5
        MaxPctFuelFill = 100,
        MaxDryTireSets = 0,
    };
}
```

- [ ] **Step 4: Write the seeding integration test**

Create `src/ApexRacers.Tests/Seeder/DemoCacheSeederScheduleTests.cs`:
```csharp
using ApexRacers.Core.Models;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoCacheSeederScheduleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SeedBopAndWeatherAsync_FillsWeatherAndBop_Idempotently()
    {
        await using var db = DbContextFactory.Create();
        db.Seasons.Add(new Season { Id = 6115, SeriesId = 444, Active = true, Year = 2026, Quarter = 2 });
        db.Weeks.Add(new Week { Id = Guid.NewGuid(), SeasonId = 6115, WeekNumber = 0, TrackId = 1 });
        db.SeasonCars.Add(new SeasonCar { SeasonId = 6115, CarId = 132 });
        await db.SaveChangesAsync(Ct);

        var seeder = new DemoCacheSeeder(db);
        await seeder.SeedBopAndWeatherAsync(Ct);
        await seeder.SeedBopAndWeatherAsync(Ct); // re-run: no duplicates / no overwrite churn

        var week = await db.Weeks.SingleAsync(w => w.SeasonId == 6115 && w.WeekNumber == 0, Ct);
        Assert.False(string.IsNullOrEmpty(week.WeatherSummaryJson));
        Assert.Equal(1, await db.SeasonCarBops.CountAsync(b => b.SeasonId == 6115 && b.CarId == 132, Ct));
    }
}
```

- [ ] **Step 5: Add `SeedBopAndWeatherAsync` to `DemoCacheSeeder`**

```csharp
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
```

- [ ] **Step 6: Run both test files**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoScheduleDataTests|FullyQualifiedName~DemoCacheSeederScheduleTests"
```
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ApexRacers.Seeder/Demo/DemoScheduleData.cs src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs src/ApexRacers.Tests/Seeder/DemoScheduleDataTests.cs src/ApexRacers.Tests/Seeder/DemoCacheSeederScheduleTests.cs
git commit -m "feat(demo): seed persisted BoP + per-week weather gaps"
```

---

### Task 9: `--demo` orchestration + end-to-end local run

**Files:**
- Modify: `src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs` (add `SeedAllAsync`)
- Modify: `src/ApexRacers.Seeder/Program.cs` (parse `--demo`, invoke after the existing steps)
- Test: `src/ApexRacers.Tests/Seeder/DemoCacheSeederAllTests.cs`

**Interfaces:**
- Produces: `DemoCacheSeeder.SeedAllAsync(ct)` — runs members, activity, leaderboards, standings, race-guide, BoP/weather in order. Invoked by `Program` when `--demo` is passed.

- [ ] **Step 1: Write the failing test**

Create `src/ApexRacers.Tests/Seeder/DemoCacheSeederAllTests.cs`:
```csharp
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoCacheSeederAllTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SeedAllAsync_AllRowsUseTheSentinelExpiry()
    {
        await using var db = DbContextFactory.Create();
        db.Seasons.Add(new Season { Id = 6115, SeriesId = 444, Active = true, Year = 2026, Quarter = 2 });
        db.SeasonCarClasses.Add(new SeasonCarClass { SeasonId = 6115, CarClassId = 100 });
        db.Weeks.Add(new Week { Id = Guid.NewGuid(), SeasonId = 6115, WeekNumber = 0, TrackId = 1 });
        db.SeasonCars.Add(new SeasonCar { SeasonId = 6115, CarId = 132 });
        await db.SaveChangesAsync(Ct);

        await new DemoCacheSeeder(db).SeedAllAsync(Ct);

        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == $"profile:{DemoData.DriverCustId}", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == "leaderboard:5", Ct));
        Assert.True(await db.ExternalDataCaches.AnyAsync(c => c.CacheKey == "race-guide", Ct));
        // Every seeded cache row carries the far-future sentinel (purge marker + never-miss).
        Assert.All(await db.ExternalDataCaches.ToListAsync(Ct), r => Assert.Equal(DemoCache.Sentinel, r.ExpiresAt));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoCacheSeederAllTests"
```
Expected: compile error — `SeedAllAsync` does not exist.

- [ ] **Step 3: Add `SeedAllAsync`**

Append to `DemoCacheSeeder`:
```csharp
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
```

- [ ] **Step 4: Run to verify it passes**

Run:
```bash
dotnet test src/ApexRacers.Tests --filter "FullyQualifiedName~DemoCacheSeederAllTests"
```
Expected: PASS.

- [ ] **Step 5: Wire `--demo` into `Program.cs`**

Near the top of `src/ApexRacers.Seeder/Program.cs` (after the `config` block, ~line 12), add:
```csharp
var seedDemo = args.Contains("--demo");
```
At the very end of `Program.cs` (after the existing `Console.WriteLine("\nSeeding complete.");`), add:
```csharp
if (seedDemo)
{
    Console.WriteLine("\nSeeding synthetic demo dataset (--demo)…");
    await new ApexRacers.Seeder.Demo.DemoCacheSeeder(db).SeedAllAsync(CancellationToken.None);
    Console.WriteLine("Demo dataset seeded (ExternalDataCaches + BoP + weather).");
}
```

- [ ] **Step 6: End-to-end local verification (Docker Postgres up)**

Run:
```bash
dotnet run --project src/ApexRacers.Seeder -- --demo
echo 'SELECT count(*) AS demo_cache_rows FROM iracing."ExternalDataCaches" WHERE "ExpiresAt" >= $$9000-01-01$$;' | docker compose exec -T postgres psql -U apexracers -d apexracers
echo 'SELECT count(*) AS bop FROM iracing."SeasonCarBops"; SELECT count(*) AS weather_weeks FROM iracing."Weeks" WHERE "WeatherSummaryJson" IS NOT NULL;' | docker compose exec -T postgres psql -U apexracers -d apexracers
echo 'SELECT "CacheKey" FROM iracing."ExternalDataCaches" WHERE "CacheKey" LIKE $$profile:%$$ OR "CacheKey"=$$race-guide$$ ORDER BY "CacheKey" LIMIT 10;' | docker compose exec -T postgres psql -U apexracers -d apexracers
```
Expected: `demo_cache_rows` > 0; `bop` > 0; `weather_weeks` > 0; the keys list shows `profile:100001`, `profile:100002`, `race-guide`. (If Docker isn't running, note it skipped — the integration tests already cover the write logic.)

- [ ] **Step 7: Commit**

```bash
git add src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs src/ApexRacers.Seeder/Program.cs src/ApexRacers.Tests/Seeder/DemoCacheSeederAllTests.cs
git commit -m "feat(demo): --demo orchestration runs the full demo dataset seed"
```

---

### Task 10: Activate the purge Plan-2 block + docs + final verification

**Files:**
- Modify: `src/ApexRacers.Data/Seeds/purge_demo_data.sql`
- Modify: `CLAUDE.md` (tracked); `private/ROADMAP.md`, `private/PRD.md`, `private/deployTODO.md` (gitignored)

**Interfaces:** none (SQL + docs).

- [ ] **Step 1: Activate the purge's Plan-2 extension block**

In `src/ApexRacers.Data/Seeds/purge_demo_data.sql`, replace the commented Plan-2 block with live statements (the synthetic BoP + weather are removable by active-season scope; the cache rows by the sentinel):
```sql
-- Demo cache rows are marked by the far-future ExpiresAt sentinel (>= 9000-01-01); real
-- cache rows have TTLs of 60 s – 24 h and can never reach it.
DELETE FROM iracing."ExternalDataCaches" WHERE "ExpiresAt" >= '9000-01-01';

-- Synthetic BoP + per-week weather for active seasons (real ingestion re-fills these idempotently).
DELETE FROM iracing."SeasonCarBops"
 WHERE "SeasonId" IN (SELECT "Id" FROM iracing."Seasons" WHERE "Active" = true);
UPDATE iracing."Weeks" SET "WeatherSummaryJson" = NULL
 WHERE "SeasonId" IN (SELECT "Id" FROM iracing."Seasons" WHERE "Active" = true);
```
Update the file's header comment to note the purge now also clears the cache + BoP + weather (no longer "extended by Plan 2").

- [ ] **Step 2: Verify the purge is complete + surgical (local Docker DB, non-destructive check)**

With the demo data seeded (Task 9), confirm the purge removes the sentinel cache rows + synthetic BoP/weather while preserving catalog, inside a rolled-back transaction:
```bash
cat <<'SQL' | docker compose exec -T postgres psql -U apexracers -d apexracers
BEGIN;
SELECT (SELECT count(*) FROM iracing."ExternalDataCaches" WHERE "ExpiresAt" >= '9000-01-01') AS before_cache,
       (SELECT count(*) FROM iracing."SeasonCarBops") AS before_bop;
DELETE FROM iracing."ExternalDataCaches" WHERE "ExpiresAt" >= '9000-01-01';
DELETE FROM iracing."SeasonCarBops" WHERE "SeasonId" IN (SELECT "Id" FROM iracing."Seasons" WHERE "Active" = true);
UPDATE iracing."Weeks" SET "WeatherSummaryJson" = NULL WHERE "SeasonId" IN (SELECT "Id" FROM iracing."Seasons" WHERE "Active" = true);
SELECT (SELECT count(*) FROM iracing."ExternalDataCaches" WHERE "ExpiresAt" >= '9000-01-01') AS after_cache,
       (SELECT count(*) FROM iracing."Cars") AS cars_preserved;
ROLLBACK;
SQL
```
Expected: `before_cache` > 0 → `after_cache` = 0; `cars_preserved` > 0; rollback leaves the DB intact.

- [ ] **Step 3: Add the prod demo-rollout runbook to `private/deployTODO.md` §14**

Demo data is per-database: Plan 2 ships the *tool*, not prod data. Nothing appears in prod until the prod
DB is seeded **and** the flag is enabled for an Alpha user. Append this runbook to §14 (the demo section
Plan 1 created). Use the real values for the resource group / vault / server already in `CLAUDE.md`
(`apexracers-rg`, `apexracers-kv`, `apexracers-pg`).

````markdown
### Enable the `iracing-demo` preview in production (Alpha)

Demo data lives in the database, so it must be seeded into **each** environment separately. Prod shows
nothing until you run the seeder against `apexracers-pg` **and** turn the flag on for an Alpha user.

**Prereqs**
- Plan 1 + Plan 2 are merged to `main` and deployed (CI deploys the API + ingestion on merge). Confirm
  the `iracing-demo` flag row exists: **Admin → Feature Flags** lists it (seeded disabled).
- `private/iracing-api-response-objects/` is present locally (gitignored — the main seeder reads it).
- Network access to `apexracers-pg` (a firewall rule for your IP) + the admin connection string.
- An Alpha-role user account in prod.

**Steps**
1. Allow your IP on the PostgreSQL server (once; remove it in step 7):
   ```bash
   az postgres flexible-server firewall-rule create --resource-group apexracers-rg \
     --name apexracers-pg --rule-name demo-seed \
     --start-ip-address <your-ip> --end-ip-address <your-ip>
   ```
2. Fetch the prod connection string from Key Vault (do not echo/commit it):
   ```bash
   az keyvault secret show --vault-name apexracers-kv --name DATABASE-CONNECTION-STRING --query value -o tsv
   ```
3. Seed the prod DB from the repo root (idempotent; one pass does catalog + synthetic subsessions + the
   demo cache/BoP/weather). It writes **only synthetic data** — negative-id subsessions, sentinel
   `ExternalDataCaches` rows, and synthetic BoP/weather — and never touches real user data:
   - PowerShell: `$env:DATABASE_CONNECTION_STRING = "<prod conn string>"; dotnet run --project src/ApexRacers.Seeder -- --demo`
   - bash: `DATABASE_CONNECTION_STRING="<prod conn string>" dotnet run --project src/ApexRacers.Seeder -- --demo`
4. Verify the seed landed:
   ```bash
   psql "<prod conn string>" -c "SELECT count(*) FROM iracing.\"ExternalDataCaches\" WHERE \"ExpiresAt\" >= '9000-01-01';"
   ```
   Expect a count > 0.
5. Enable the flag: **Admin → Feature Flags → `iracing-demo` IsEnabled = true** (leave `MinimumRole=Alpha`).
6. Make the tester Alpha (Settings role self-service, or an admin), then smoke-test as that user: gated nav
   appears, the demo banner shows, and the cached pages render synthetic data.
7. Remove the firewall rule when finished:
   ```bash
   az postgres flexible-server firewall-rule delete --resource-group apexracers-rg \
     --name apexracers-pg --rule-name demo-seed --yes
   ```

**Teardown (when real iRacing creds arrive — M2):** set `iracing-demo` **off** → run
`purge_demo_data.sql` against prod → confirm the synthetic rows are gone → **only then** enable
`iracing-live`. (Same ordering as the M2 runbook above; the purge is safe in prod because it targets only
negative-id subsessions, the `ExpiresAt` sentinel, and active-season BoP/weather.)

> Scope note: the preview is **Alpha-only** by design. To widen it to all signed-in users later, lower the
> flag's `MinimumRole` to `Standard` — no code change. Known thin spots even when seeded: the percentile
> world-record overlay (deferred), the Race Detail "Your Race Pace" trace (deferred), `/analytics`
> (populates lazily after a Recommendations visit), and the race-guide board (static "in-progress" window).
````

- [ ] **Step 4: Update the remaining docs**

- `CLAUDE.md` (tracked): in the data-source-strategy / cache section, note that `ApexRacers.Seeder --demo` (the `DemoCacheSeeder`) seeds `ExternalDataCaches` (+ BoP/weather) with synthetic mapped DTOs under each service's exact keys (far-future `ExpiresAt` sentinel), and that the Seeder references Api to reuse the real cached types. Note that `iracing-demo` is now fully functional once a DB is seeded (Plan 2 shipped), and point to deployTODO §14 for the prod rollout.
- `private/ROADMAP.md`: move the demo-data-preview item to **done** (Plan 1 + Plan 2 both shipped); keep the M2 teardown runbook step.
- `private/PRD.md`: bump version; note the demo cache surface is now populated (per-DB seed).

- [ ] **Step 5: Commit**

```bash
git add src/ApexRacers.Data/Seeds/purge_demo_data.sql CLAUDE.md
git commit -m "feat(demo): activate purge for demo cache/BoP/weather; document Plan 2 + prod rollout"
```
(The `private/` docs are gitignored — no commit needed.)

---

## Verification (whole plan)

- [ ] Backend: `dotnet test src/ApexRacers.Tests` — all pass; then CI-equivalent coverage (`dotnet test --collect:"XPlat Code Coverage" --settings coverage.runsettings`) — line **and** branch ≥ 85% (the new `Demo/*` builders + `DemoCache`/`DemoCacheSeeder` are measured; the Seeder `Program` is excluded).
- [ ] Frontend: unchanged by this plan (no `src/web` edits) — but run `npx vitest run` once to confirm nothing regressed.
- [ ] Manual smoke (local, the real payoff): `docker compose up`; `dotnet run --project src/ApexRacers.Seeder -- --demo`; enable `iracing-demo` for an Alpha user (Admin → Feature Flags); confirm the previously-503 pages now render synthetic data — **Progression, Profile (driver stats + trophy case), Races, Leaderboards, Compare (with the rival), Standings (Championship/TT/Qualify), Race Now**, plus Strategy/Schedule now showing BoP + weather. Then run `purge_demo_data.sql` and confirm the demo data is gone while the catalog remains.

## Self-Review notes (addressed)

- **Spec coverage:** §5 cache seeding → Tasks 3–7 (every listed page-critical endpoint; WR overlay + per-subsession lap-data deferred per the user's scope choice, documented in Task 10/ROADMAP); §5 TTL+purge-marker sentinel → Task 2 (`DemoCache.Sentinel`) + Task 10 (purge); §6 persisted BoP/weather → Task 8; §8 purge → Task 10; §9 testing (pure builders unit-tested, mirroring `AchievementsMapper`/`StrategyAnalysis`) → every builder task. §3/§4/§7 were Plan 1.
- **Cache fidelity:** every payload is the **real** cached `<T>` serialized with default options (Task 1 promotes the 4 private record types so the builders construct them); keys are verbatim; the chart key uses `(int)MemberChartType.IRating` computed the same way as the service.
- **Type consistency:** `DemoCache.UpsertAsync<T>`, `DemoData.DriverCustId`/`RivalCustId` (`long`), the builder return types match each cached `<T>` exactly (verified against `ResponseDtos.cs` + the promoted records).
- **Coverage trap handled:** Task 1 excludes `[ApexRacers.Seeder]Program`/`<Program>$` so the pre-existing 700-line CLI doesn't sink coverage once Tests references the Seeder.
- **Known demo quirk documented:** the race-guide row uses an always-live window (static sentinel cache can't track the clock), so sessions show as in-progress and the "starting soon" bell alert won't fire for them.
