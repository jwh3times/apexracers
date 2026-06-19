# 3.1 — Driver-vs-driver / Rival comparison — design

**Date:** 2026-06-18
**Plan reference:** `private/plan.md` Part 5, slice 3.1
**Branch:** `feat/tier3-rival-comparison`

## Goal

Head-to-head comparison between the signed-in driver and a "rival" they choose:
identity + license badges, per-category career stats, an iRating-trajectory overlay,
and a shared-race head-to-head (finish/iRating/incidents + best-lap-per-shared-track).
Users can search drivers by name, get suggestions from drivers they've actually raced,
and follow/persist a list of rivals. Realizes the PRD "Social Integration" pillar.

## Decisions (from brainstorming)

- **Add a rival via:** name search (`SearchDriversAsync`) **plus** shared-race suggestions
  drawn from local `SubsessionResult` rows. (Raw cust_id entry is not required since search covers it.)
- **Comparison shows all four panels:** identity + license badges, career stats side-by-side,
  iRating trajectory overlay, shared-race head-to-head — **including** best-lap-per-shared-track pace.

## Architecture

Two separated concerns, both reusing existing infrastructure
(`MemberStatsService`, `CachedIRacingClient`, `MemberContext`, local `SubsessionResult`):

1. **Rival management** — persist who you follow; discovery (search + suggestions).
2. **Comparison** — assemble the head-to-head view for `(callerCustId, rivalCustId)`.

### Backend

**Entity + migration**

- `Rival(Id Guid, UserId Guid, RivalCustId long, DisplayName string, CreatedAt DateTimeOffset)`
  in `Core/Models/`.
- `RivalConfiguration` in `Data/EntityConfigurations/` — schema `iracing`, unique index on
  `(UserId, RivalCustId)`.
- `DbSet<Rival> Rivals` on `AppDbContext`.
- Migration `AddRival`.

**Services**

- `RivalService(AppDbContext db, CachedIRacingClient cached)`
  - `ListAsync(userId)` → `IReadOnlyList<RivalDto>`
  - `AddAsync(userId, custId, displayName)` → idempotent upsert (unique on UserId+custId)
  - `RemoveAsync(userId, custId)`
  - `SearchDriversAsync(term)` → `SearchDriversAsync` via the cached client (short TTL;
    503 when iRacing unconfigured)
  - `SuggestionsAsync(userId, callerCustId)` → distinct other drivers from `SubsessionResult`
    rows in subsessions the caller appears in, with a shared-race count; excludes self +
    already-followed rivals; pure DB query.
- `RivalComparisonService(MemberStatsService stats, AppDbContext db)`
  - `CompareAsync(callerCustId, rivalCustId)` → `DriverComparisonDto`.
  - Per-side identity/licenses/career/iRating-history from a new focused
    `MemberStatsService.GetComparisonSideAsync(custId)` (profile + career + per-category iRating
    charts; **no** summary/recap — lighter than `GetDriverProfileAsync`).
  - Shared-race head-to-head assembled from the DB + a pure helper.
- `SharedRaceAnalysis` (pure static helper) — given both drivers' `SubsessionResult` rows,
  compute the "finished ahead" tally and best-lap-per-shared-track. Unit-tested directly
  (mirrors `LapAnalysis` / `SubsessionIndexer`), so the I/O-bound service stays coverable.

**DTOs** (reuse leaf types `TimeSeriesPointDto`, `LicenseBadgeDto`, `CategoryCareerDto`):

- `RivalDto(long CustId, string DisplayName, DateTimeOffset CreatedAt)`
- `DriverSearchResultDto(long CustId, string DisplayName)`
- `RivalSuggestionDto(long CustId, string DisplayName, int SharedRaces)`
- `CategoryHistoryDto(int CategoryId, string CategoryName, IReadOnlyList<TimeSeriesPointDto> Points)`
- `ComparisonSideDto(long CustId, string DisplayName, string? FlairName, string? FlairShortName,
  DateOnly? MemberSince, IReadOnlyList<LicenseBadgeDto> Licenses,
  IReadOnlyList<CategoryCareerDto> Career, IReadOnlyList<CategoryHistoryDto> IRatingHistory)`
- `SharedRaceRowDto(int SubsessionId, DateTimeOffset StartTime, string TrackName, int YourFinish,
  int RivalFinish, int YourIRatingDelta, int RivalIRatingDelta, int YourIncidents, int RivalIncidents)`
- `SharedTrackPaceDto(string TrackName, double YourBestLapSeconds, double RivalBestLapSeconds)`
- `SharedRaceSummaryDto(int TotalShared, int YouAhead, int RivalAhead,
  IReadOnlyList<SharedRaceRowDto> Races, IReadOnlyList<SharedTrackPaceDto> TrackPace)`
- `DriverComparisonDto(ComparisonSideDto You, ComparisonSideDto Rival, SharedRaceSummaryDto Shared)`

**Controllers (thin)**

- `RivalsController` @ `/api/users/me/rivals` (Authorize): `GET` list, `POST` add,
  `DELETE /{custId}` remove, `GET /search?term=`, `GET /suggestions`.
- `CompareController` @ `/api/users/me/compare?rivalCustId=` (Authorize) — caller cust_id via
  `MemberContext`; typed **409** `IRACING_NOT_LINKED` when unlinked.

### Frontend

- `api.ts`: interfaces (camelCase mirrors) + `getRivals` / `addRival` / `removeRival` /
  `searchDrivers` / `getRivalSuggestions` / `compareRival(rivalCustId)`.
- `ComparePage.tsx` @ `/compare`:
  - **Rival manager** — debounced name search, shared-race suggestion chips, saved-rivals list
    with add/remove.
  - **Comparison** (on selecting a rival) — the four panels. iRating overlay has a category selector.
  - 409 → "link your iRacing ID" empty state (mirrors `RecommendationsPage`).
- `IRatingCompareChart.tsx` component — two polylines on a shared scale
  (`Sparkline` is single-series); own test.
- Nav: `{ to: '/compare', label: 'Compare', icon: 'group' }` in `navItems` `AUTH_NAV`;
  route inside `RequireAuth` in `App.tsx`.

### Testing

- Backend: `RivalServiceTests` (add/list/remove idempotency, suggestions query, mocked search),
  `RivalComparisonServiceTests` (canned `MemberStatsService` deps over EF InMemory + shared-race
  assembly), `SharedRaceAnalysisTests` (pure), `MemberStatsServiceTests` for the new side method.
  ≥80% line + branch.
- Frontend: `ComparePage.test.tsx`, `IRatingCompareChart` test. ≥80% all four metrics.

### Docs

CLAUDE.md (controllers, services, models table, routing table, Sidebar nav), `private/PRD.md`,
and mark slice 3.1 done in `private/plan.md`.

## Known limitation

Shared-race head-to-head and suggestions only see **ingested** races where the caller appears —
sparse early. Name search is always available and is the primary add path; the shared-race panels
degrade gracefully to "no shared races yet."

## Notes

- Aydsko: `SearchDriversAsync(searchTerm)` → `Lookups.DriverSearchResult[]`. Installed SDK is
  2601.3.0 (CLAUDE.md references 2603.0.0 — pre-existing discrepancy; methods present).
- `SubsessionResult` already stores the **full field** per subsession (`CustId` + `DisplayName`),
  enabling shared-race intersection without new ingestion.
