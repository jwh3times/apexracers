# Demo-Data Preview — Alpha-Tester Reveal of the iRacing Surface

**Date:** 2026-06-23
**Status:** Approved (pending written-spec review)
**Author:** brainstorming session (Claude + Jerry)

---

## 1. Problem & context

Every iRacing-data-backed feature is non-functional in production: the service-account OAuth
credentials are unavailable (iRacing closed new client registration), so the ingestion worker never
runs and the persisted tables stay empty, while the `CachedIRacingClient` endpoints throw
`IRacingNotConfiguredException` → 503. Milestone **M1** (shipped) gates that entire surface behind the
`iracing-live` feature flag (seeded **disabled**): iRacing routes render `ComingSoonPage` and their nav
items are hidden.

The result is that the deployed product is a working personal-telemetry tool wrapped around a large set
of "Coming Soon" pages. This spec implements the **M2 backlog "guest-reveal / demo-data preview"**: let
signed-in **Alpha** testers explore the *full* iRacing surface backed by clearly-labeled **synthetic
data**, so they get a coherent end-to-end product to click through while the real credentials remain
blocked.

This is independent of the creds blocker (the synthetic data is generated locally) and independent of
"Sign in with iRacing" (M3). It was deferred from M1 only because surfacing non-real numbers needs a
product decision to avoid misleading anyone — that decision is now made (Alpha-gated + persistently
labeled).

## 2. Decisions (locked during brainstorming)

| Decision | Choice |
| --- | --- |
| Audience / goal | **Alpha-tester engagement** — signed-in Alpha users, in production, persistent, clearly labeled |
| Coverage | **Full surface** — light up the `CachedIRacingClient` half too, not just the seeder-backed persisted half |
| Cached-half mechanism | **Seed `ExternalDataCache`** with synthetic mapped DTOs (approach "A") — *not* a request-time demo-mode bypass in the services (approach "D") |
| Why A over D | Keeps production **app code clean** (demo lives in *data*, trivially added/purged) vs. permanent demo branches threaded through ~10 services; more faithful (reuses real mappers); clutter is bounded + purgeable |
| Demo identity | **Shared demo driver** — one reserved synthetic `cust_id` resolved in `MemberContext` only; **no real account data mutated** |
| Activation | A **single new `iracing-demo` flag** (`MinimumRole=Alpha`, seeded disabled); `RequireFlag`/`visibleNav` gate on `iracing-live` **OR** `iracing-demo` |
| Labeling | **Persistent, non-dismissible global banner** in `AppShell` while `iracing-demo` is on |
| Purge | One-command purge tooling; required step in the M2 "real creds on" runbook (before flipping `iracing-live`) |

## 3. Activation & flag model

New seeded `FeatureFlag` **`iracing-demo`** — `MinimumRole=Alpha`, `IsEnabled=false`, seeded via an EF
`InsertData` migration that mirrors `20260622_SeedIracingLiveFlag`.

`iracing-demo` is the **single switch** for the whole demo. To avoid a two-flag dance, `RequireFlag`
(frontend route guard) and the `visibleNav` helper gate on **`iracing-live` OR `iracing-demo`**. So
flipping `iracing-demo` on (Alpha) simultaneously:

1. reveals the gated routes + nav items (RequireFlag stops rendering `ComingSoonPage`),
2. shows the demo banner (§7), and
3. activates the `MemberContext` demo override (§4).

Semantics stay clean: *"show the iRacing surface if real data **or** demo data is available."*
`iracing-live` remains the independent "real creds are live" flag.

**M2 handoff (operational rule):** turn `iracing-demo` **off** → run the purge (§6) → turn
`iracing-live` **on**. The two are never *required* to be on together. If they ever are, the demo
override **takes precedence** (the safe choice during a transition window).

## 4. Backend — the only app-code demo branch

A single override in **`MemberContext`** (`src/ApexRacers.Api/Services/MemberContext.cs`): when
`iracing-demo` is enabled for the caller, resolve `cust_id` to a reserved constant
**`DemoDriver.CustId = 100001`** (one of the synthetic pool drivers, range 100001–100200) instead of the
user's real or absent link. The override is **unconditional while the flag is on** — real `cust_id`s
have no backing data or creds anyway, so demo-mode points everyone at the demo driver.

`MemberContext` must read flag state to decide this; it gets the flag-evaluation dependency injected
(same source the `FeatureFlagsController` / `AdminService.GetFlagsForRoleAsync` hierarchy uses). This is
the **whole** backend demo footprint — one branch in one resolver, never threaded through the cached
services. The cached services need no demo awareness: with the cache pre-seeded (§5),
`CachedIRacingClient.GetOrFetchAsync` simply returns a hit and never reaches the (uncredentialed) live
fetch.

## 5. Cache seeding (the bulk of the work)

Extend `ApexRacers.Seeder` with a **`--demo`** step (a `DemoCacheSeeder` class) that generates synthetic
**mapped DTOs** and writes them to `ExternalDataCache` under the **exact cache keys** each service's
`GetOrFetchAsync` call uses (e.g. `progression:100001`, `profile:100001`, `leaderboard:5`,
`standings:…`). Reuse the real pure mappers where practical (`AchievementsMapper`, `MapPoints` /
`MapLicenses` / `MapCareer`, `LeaderboardCsvParser`, etc.) so the payloads match the real DTO shapes and
can't drift.

Endpoints covered (cust-specific ones keyed to the demo driver):

- Progression (`MemberStatsService.GetProgressionAsync`)
- Profile Stats (`GetDriverProfileAsync`)
- Achievements (`AchievementsService`)
- Race History (`RaceHistoryService`)
- Compare-side (`GetComparisonSideAsync`) — demo driver **plus one synthetic rival** so `/compare` works
- Leaderboards (category 1–6)
- Standings (championship / Time Trial / qualifying, per active series + car class)
- Race Guide
- *(optional)* World Records overlays, Lap Data per subsession

**Exact cache keys must be enumerated during planning** by reading each service — the seeder reproduces
them verbatim (a `demo:` prefix is **rejected**: it would break key matching, since services look up the
un-prefixed key).

### TTL + purge marker in one mechanism

Every demo row is seeded with a **far-future `ExpiresAt` sentinel** (e.g. `9999-01-01T00:00:00Z`). That
single choice does three jobs:

1. `CachedIRacingClient` never treats the row as a miss — **critical**, because a miss with no creds
   *throws* `IRacingNotConfiguredException` rather than refreshing.
2. `ExternalDataCacheCleanupService` (deletes rows expired beyond a 2-day grace) never touches it.
3. It is the **purge marker** — no schema change, no key prefix needed. Real cache rows have TTLs of
   60 s – 24 h and can never reach this sentinel.

## 6. Persisted synthetic data — mostly already present

`ApexRacers.Seeder` already fills `Tracks` / `Cars` / `CarClasses` / `Series` / `Seasons` / `Weeks` /
`SeasonCars` / `Subsessions` / `SubsessionResults` (synthetic; **negative subsession IDs**; 200 synthetic
drivers in custId range 100001–100200). The demo step adds the gaps:

- **`SeasonCarBop`** (per-week BoP) and **per-week weather** (`Week.WeatherSummaryJson`) — currently
  unseeded, so Strategy/Schedule render thin. Add synthetic values via the existing pure heuristics' input
  shapes.

**Known caveat — `/analytics`:** `UserAnalyticsService` reads pre-computed `CarPercentileResult` keyed by
*UserId* (the real Alpha user's GUID), which cannot be pre-seeded for unknown future users. It populates
**lazily** the first time the user visits Recommendations / "your percentiles" (which compute-and-upsert
percentiles from the demo driver's laps). Accepted as a minor follow-up rather than introducing a second
demo branch (e.g. a demo-aware `UserAnalyticsService`). Documented for testers.

## 7. Labeling UI

A new small `DemoBanner` component, rendered in `AppShell` whenever `useFeatureFlag('iracing-demo')` is
on: a **persistent, non-dismissible** slim bar —

> 🧪 **Demo data** — figures are synthetic, not real iRacing results.

Cyan accent using the existing fluid design tokens (`text-small-fluid`, `primary-container`); no new
one-off Tailwind. Sits above the routed content so it's visible on every gated page. No dismissible /
remembered state (intentional — the point is that it can't be missed).

## 8. Purge tooling

`src/ApexRacers.Data/Seeds/purge_demo_data.sql` (and an optional seeder `--purge-demo` mode wrapping the
same statements), run as one command:

```sql
BEGIN;
DELETE FROM iracing."Subsessions" WHERE "Id" < 0;                       -- SubsessionResults cascade
DELETE FROM iracing."ExternalDataCache" WHERE "ExpiresAt" >= '9000-01-01';
DELETE FROM iracing."CarPercentileResults";                            -- safe: see ordering rule
DELETE FROM iracing."SeasonCarBop" WHERE <seeded seasons>;             -- synthetic BoP rows
UPDATE iracing."Weeks" SET "WeatherSummaryJson" = NULL WHERE <seeded seasons>;  -- weather is a column
COMMIT;
```

> **Implementation note (Plan 2, shipped):** the illustrative SQL above predates implementation. The
> shipped `purge_demo_data.sql` uses the **actual EF table names, which are plural** —
> `iracing."ExternalDataCaches"` and `iracing."SeasonCarBops"` (the singular names above are not real
> tables) — and scopes the BoP/weather deletes to active seasons. Also: there is **no `progression:`
> cache key** (an example used elsewhere in this spec) — progression is composed at read time from the
> `profile:` + per-category `chart:` entries.

**Catalog is intentionally retained.** Like `truncate_seed_data.sql`, the purge leaves
`Series` / `Seasons` / `Weeks` / `Cars` / `Tracks` / `CarClasses` in place — these are reference/catalog
data (captured from real iRacing responses), not synthetic clutter, and the real ingestion worker
reconciles them idempotently (`CatalogIngest` upserts) once creds land. The purge targets only the
synthetic **results**, **derived percentiles**, **cache rows**, and **BoP/weather**.

**Ordering rule (documented):** truncating `CarPercentileResults` wholesale is safe **only because demo
teardown happens before any real ingestion exists** — at teardown time every percentile row is
demo-derived. This is enforced by the M2 runbook ordering (purge **before** `iracing-live` on). The
existing `truncate_seed_data.sql` stays the blunt dev-only tool; `purge_demo_data.sql` is the surgical,
prod-safe one (negative IDs + the `ExpiresAt` sentinel).

**M2 runbook addition:** add "turn `iracing-demo` off → run `purge_demo_data.sql` → confirm empty → turn
`iracing-live` on" as a required step in `deployTODO.md` / `ROADMAP.md` §M2.

## 9. Testing

- **Pure synthetic-DTO builders** (the demo-data generators) unit-tested directly, mirroring
  `AchievementsMapper` / `StrategyAnalysis`.
- **`MemberContext` override**: flag-on → demo `cust_id`; flag-off → existing resolve/`null` behavior.
- **Frontend**: `DemoBanner` renders when the flag is on / hidden when off; `RequireFlag` and `visibleNav`
  honor **either** flag (new branch coverage).
- Both suites stay **≥ 85%** across all metrics (backend line+branch, frontend stmts/branches/fns/lines).

## 10. Scope boundary (what this is *not*)

- No "Sign in with iRacing" (M3) and no per-user OAuth.
- No changes to the real ingestion worker or `CachedIRacingClient` fetch path.
- No per-user distinct demo drivers (single shared demo driver only).
- No dismissible / remembered banner state.
- Demo is **Alpha-only** and is never enabled alongside live data in normal operation.

## 11. Effort shape

The cache-seeding (§5) is ~70% of the work — roughly a dozen endpoints' worth of synthetic DTO builders,
plus enumerating their exact cache keys. The `MemberContext` override (§4), the `iracing-demo` flag +
migration (§3), the `DemoBanner` + guard changes (§7), and the purge tooling (§8) are each small and
contained.
