# ApexRacers — Implementation TODO

Tracks what is scaffolded vs. what needs real implementation to reach a working application.
Items are ordered by dependency — nothing later can work without the things above it.

---

## Status key

- `[x]` Complete
- `[ ]` Not yet implemented

---

## Infrastructure

- [x] `docker-compose.yml` — PostgreSQL 16 + pgAdmin 4, named volume, healthcheck
- [x] `.env.example` — all 7 environment variables documented
- [x] `global.json` — .NET 10 SDK pinned
- [x] `Directory.Packages.props` — NuGet Central Package Management with transitive pinning
- [x] `CLAUDE.md` — architectural guidelines
- [x] `README.md`, `LICENSE` (AGPL-3.0)

---

## ApexRacers.Core

- [x] `Series`, `Week`, `Car`, `LapTimeEntry`, `UserProfile`, `CarPercentileResult` — domain models with navigation properties

---

## ApexRacers.Data

- [x] `AppDbContext` with all 6 `DbSet<T>` properties
- [x] 6 `IEntityTypeConfiguration` classes — indexes, FK behaviour, column constraints
- [x] `DesignTimeDbContextFactory` — env var fallback for `dotnet ef` tooling
- [x] `InitialCreate` migration — scaffolded, ready to apply with `dotnet ef database update`

---

## ApexRacers.Ingestion

Structure is complete; all business logic is stubbed.

- [x] `Program.cs` — Aydsko iRacingData client registered with Password Limited OAuth, `AppDbContext` registered
- [x] `Worker.cs` — `BackgroundService` loop with configurable interval
- [ ] **Step 1** — Fetch active series from iRacing API, upsert `Series` records
- [ ] **Step 2** — Fetch weeks and car classes per series, upsert `Week` and `Car` records
- [ ] **Step 3** — Fetch lap time results per week, upsert `LapTimeEntry` records
- [ ] **Step 4** — Log ingestion summary (counts inserted/updated, errors)

> Nothing appears in the database until this worker runs successfully.

---

## ApexRacers.Api

Controllers and DI wiring are complete. All service methods throw `NotImplementedException`.

### Authentication — largest missing piece, blocks recommendations and percentile

- [ ] `GET /api/auth/login` — new endpoint that builds the iRacing OAuth authorize URL with a generated `state` nonce and redirects the browser
- [ ] `AuthService.HandleCallbackAsync` — validate `state` nonce (CSRF); exchange authorization code for iRacing access token; fetch driver profile (`customerId`, `displayName`) from iRacing; upsert `UserProfile`; issue application JWT
- [ ] `Program.cs` — add `UseAuthentication()` and JWT bearer middleware
- [ ] `RecommendationsController` — replace hardcoded `customerId = 0` with `IRacingCustomerId` extracted from authenticated user's claims

### Service implementations

These can be built independently of auth except where noted.

- [ ] `SeriesService.GetActiveSeriesAsync` — query `db.Series`, map to `SeriesDto` list
- [ ] `WeekCarStatsService.GetCarsForWeekAsync` — validate series/week exist; group `LapTimeEntries` by `CarId`; compute entry count, fastest lap (MIN), median lap; map to `WeekCarDto` list
- [ ] `PercentileCalculationService.ComputeAndCacheAsync` — fetch driver's best lap for car+week; count entries that are slower to derive `percentileRank` (0–100, higher = faster relative to field); upsert `CarPercentileResult`; return `PercentileResultDto` *(requires auth for production use)*
- [ ] `CarRecommendationService.GetRecommendationsAsync` — retrieve cached `CarPercentileResults` per car for the user (or compute via `PercentileCalculationService` if not cached); sort descending by `percentileRank`; assign rank 1..N; map to `CarRecommendationDto` list *(requires auth)*

---

## src/web

React Router, typed `api.ts`, and `/api` proxy are complete. All pages render placeholder text.

### Authentication UI

- [ ] `HomePage` — "Sign in with iRacing" button that redirects to `GET /api/auth/login`
- [ ] Handle JWT returned from auth callback — store in `localStorage` or a cookie; attach as `Authorization: Bearer` header in `api.ts` requests

### Page implementations

- [ ] `SeriesPage` — call `api.getSeries()` on mount; render series cards with a link to `/series/:seriesId/weeks/:weekId` (using `currentSeason` as `weekId`) *(no auth required)*
- [ ] `WeekDetailPage` — call `api.getCarsForWeek(seriesId, weekId)` on mount; render table of cars with entry count, fastest lap, median lap; each row links to percentile lookup *(no auth required)*
- [ ] `RecommendationsPage` — resolve `weekId` from query string or a week-picker; call `api.getRecommendations(weekId)`; render ranked car list with percentile and sample size *(requires auth)*

---

## Minimum path to a working demo

If you want the shortest path to something visible in the browser, implement in this order:

1. **Ingestion worker** — populate the database with real series, weeks, and lap times
2. **`SeriesService`** and **`WeekCarStatsService`** — no auth dependency
3. **`SeriesPage`** and **`WeekDetailPage`** — call unauthenticated endpoints, render real data
4. **Auth flow** — login redirect + callback + JWT middleware
5. **`PercentileCalculationService`**, **`CarRecommendationService`**, **`RecommendationsPage`** — complete the personalised experience
