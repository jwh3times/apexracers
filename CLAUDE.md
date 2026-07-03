# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> **Specialist agents carry the deep detail.** This file is the high-altitude map every session
> reads; the per-domain rules live in `.claude/agents/` and load only when that subagent runs. Pointers
> below say which agent owns what — don't duplicate their content back into this file.

| Work type                                                           | Agent                  |
| -------------------------------------------------------------------- | ---------------------- |
| .NET/EF features, services, auth, ingestion patterns                | `dotnet-api`           |
| React pages/components, design tokens, Vitest/Playwright rules      | `react-frontend`       |
| Schema, indexes, query patterns, migrations                         | `postgres-specialist`  |
| Dockerfiles, Compose, image builds                                  | `docker-containers`    |
| Azure resources, Key Vault map, deploy commands                     | `azure-infrastructure` |
| Reviewing a diff for correctness/security before merging            | `code-reviewer`        |
| Security testing (JWT/auth flows, data isolation, CORS, admin)      | `penetration-tester`   |
| Documentation sync after changes (CHANGELOG, CLAUDE.md, README, …) | `docs-updater`         |

---

## Ground Rules

- **Don't build on unverified assumptions — ask.** When a task depends on a fact you can't confirm
  from the code, the docs, or a quick check — especially **external or domain facts** (iRacing API
  response shapes, the `Aydsko.iRacingData` SDK's wire types and their version drift / `[Obsolete]`
  fields, the chunked result/lap-data structure, iRacing semantics like percentile / BoP /
  license-category / lap-time fields) — stop and ask before designing against a guess. **Ground truth
  here is usually not a live call:** the iRacing OAuth credentials are unavailable (the standing
  blocker — see ROADMAP.md), so you typically can't fetch a fresh sample. Verify **before** writing the
  implementation against what _is_ obtainable:

  - the captured field shapes in `private/iracing-api-response-objects/` — the authoritative shape
    reference; read the relevant endpoint before mapping it;
  - the `Aydsko.iRacingData` SDK's own typed models (the SDK is the wire contract — map from it, never
    serialize raw SDK types into the cache);
  - a real local dataset — `docker compose up` + `ApexRacers.Seeder` populates Postgres with catalog
    data and synthetic laps for all 7 series, so query/SQL/aggregation shape can be checked live.

  If the shape isn't in the samples and can't be reached through the SDK types or a local seed, say so
  and ask — don't infer it. Designing a structure to "discover" an unknown shape at runtime is still
  building on an assumption.

- **A sensible default for a genuinely low-stakes choice is fine — state it and proceed.** The bar:
  would being wrong force a rework or ship something incorrect (a wrong percentile, a mis-mapped lap
  time, a bad EF migration, a cache keyed on the wrong shape)? If yes, it's load-bearing — ask.

---

## Planning & project docs

Planning/status docs live in `private/` (gitignored — local working docs); the one **shipped** exception is the root `CHANGELOG.md`.

| Doc                                     | What it is                                                                                                           |
| --------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `private/ROADMAP.md`                    | **Canonical** record of **remaining** work — blockers, milestones, backlog. Read first for "what's next".            |
| `private/archive.md`                    | **Canonical** record of **completed** work (single chronological log, newest first; build-era detail at the bottom). |
| `CHANGELOG.md` (repo root)              | Public release notes — Keep a Changelog + SemVer; `docs-updater` maintains the `[Unreleased]` section.               |
| `private/PRD.md`                        | Product spec — feature definitions, screen inventory, API & data-model summaries.                                    |
| `private/deployTODO.md`                 | Azure deployment runbook (resource creation, Key Vault, GitHub Actions, DNS/SSL).                                    |
| `private/iracing-api-response-objects/` | Authoritative iRacing API JSON field shapes — read before mapping any endpoint.                                      |

**After completing a feature/fix:** remove the shipped item from `private/ROADMAP.md`, **prepend** its
summary to `private/archive.md` (newest first), and add a bullet under the `CHANGELOG.md` `[Unreleased]`
section (correct `Added`/`Changed`/`Fixed`/`Removed`/`Security` category); update `private/PRD.md`,
this `CLAUDE.md`, and `README.md` as relevant. ROADMAP tracks only what remains; `archive.md` is the
record of what shipped. **Cutting a release** — rolling `[Unreleased]` into a versioned `[x.y.z]` section
and tagging — is a separate, deliberate step (SemVer; currently `0.2.0`). (The `docs-updater` agent owns
the full doc-update matrix.)

> **iRacing blocker (canonical note — referenced elsewhere).** The deployed app lacks iRacing OAuth
> credentials, so iRacing-data features are non-functional in production. Two seeded-**disabled**
> feature flags gate that surface:
>
> - `iracing-live` (M1, shipped) — real creds. When off, iRacing routes render `ComingSoonPage` and nav
>   items are hidden.
> - `iracing-demo` (Alpha-gated) — reveals the same surface backed by clearly-labeled **synthetic** demo
>   data. `MemberContext` resolves demo users to `DemoData.DriverCustId`.
>
> `RequireFlag` / `visibleNav` gate on `iracing-live` **OR** `iracing-demo`. `iracing-demo` is fully
> functional **only** once a DB is seeded with `ApexRacers.Seeder --demo` (Plan 2) — do **not** enable it
> in prod before then (cached pages would 503). See `private/deployTODO.md` §14 for the prod rollout.

---

## Commands

### .NET (run from repo root)

```bash
dotnet build                                          # build the solution
dotnet run --project src/ApexRacers.Api               # run the API (needs DATABASE_CONNECTION_STRING)
dotnet run --project src/ApexRacers.Ingestion         # run the ingestion worker (needs iRacing + DB env vars)
dotnet run --project src/ApexRacers.Seeder            # seed catalog + synthetic laps for 7 series (idempotent)
dotnet run --project src/ApexRacers.Seeder -- --demo  # + seed the synthetic demo cache (Plan 2)

# EF Core migrations — always target Data, startup project Api
dotnet ef migrations add <Name> --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
dotnet ef database update      --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
```

Seeder needs `private/iracing-api-response-objects/` populated first (gitignored — see README) and
`DATABASE_CONNECTION_STRING` (else falls back to the local Docker default). `dotnet-ef` must be
installed globally and match EF Core (currently 10.0.9). SQL seed/cleanup scripts live in
`src/ApexRacers.Data/Seeds/` (`seed_gt3_series.sql`, `remove_gt3_seed.sql`, `truncate_seed_data.sql`),
piped in via `Get-Content … | docker compose exec -T postgres psql -U apexracers -d apexracers`.

### Frontend (run from `web/`)

```bash
npm install
npm run dev          # Vite :5173, proxies /api → http://localhost:5000 (local dotnet API)
npm run dev:all      # starts dotnet API + Vite together
npm run dev:docker   # proxies /api → http://localhost:8080 (API in Docker)
npm run dev:cloud    # proxies /api → https://apexracers-api.azurewebsites.net
npm run build        # tsc + Vite production build
npm run lint         # ESLint
npm run test         # Vitest one-shot   (test:watch for watch mode)
npm run test:e2e     # Playwright E2E — requires app at http://localhost:8080 (e.g. docker compose up)
npm run test:e2e:ui  # Playwright UI mode (interactive)
npx prettier --check .   # CI runs this exact check — fix with: npx prettier --write .
```

Note: `npm run format` / `npm run format:check` exist but only cover `src/**` — CI's prettier check
covers the whole `web/` tree, so prefer the `npx prettier … .` forms above.

Proxy target is `API_TARGET` in the relevant `web/.env.*` file; the default falls back to
`http://localhost:5000`.

### Infrastructure

```bash
docker compose up -d                       # postgres + pgadmin + api
docker compose up -d --build               # …with rebuild
docker compose --profile ingestion up -d   # …include the ingestion worker
```

Copy `.env.example` to `.env` and set `JWT_SIGNING_KEY` first; `DATABASE_CONNECTION_STRING` is
pre-filled for the Docker network. See the **Ports** table in [README.md](README.md#ports) (Postgres
5432, pgAdmin 5050, API 8080/5000, Vite 5173). For Dockerfile/Compose work see the `docker-containers`
agent.

### Azure

API = App Service (`apexracers-api`), ingestion worker = Container App (`apexracers-ingestion`), both
in resource group `apexracers-rg`. Key Vault secret names use hyphens (`JWT-SIGNING-KEY`) and are mapped
to underscore env vars by `HyphenToUnderscoreSecretManager` in both `Program.cs` files. Full resource
inventory, Key Vault map, and deploy commands live in the `azure-infrastructure` agent.

---

## Architecture

### Projects

```text
Core  ← no deps          Data  ← EF Core, Npgsql (references Core)
Api / Ingestion / Seeder ← reference Core + Data (Api and Ingestion never reference each other)
Tests ← xUnit (references Api + Core + Data)
```

Package versions are centrally managed in `Directory.Packages.props` — **never** add `Version="…"` to a
`.csproj`; use `dotnet add package`. `CentralPackageTransitivePinningEnabled=true` is intentional. Full
.NET/EF/auth patterns are in the `dotnet-api` agent; schema/index/query patterns in `postgres-specialist`.

### API request flow

```text
HTTP request → Controller (binds inputs only) → Service (all logic + EF Core) → AppDbContext → PostgreSQL
```

Controllers do no logic beyond binding inputs and returning `Ok(result)`. Services live in
`src/ApexRacers.Api/Services/`; response shapes are `record` types in `Dtos/ResponseDtos.cs`. If an
action needs multiple steps, extract a focused service class injected via DI — no MediatR, no
command/query handlers, no `IRepository<T>` (use `AppDbContext` directly).

**Error handling:** `ExceptionHandlingMiddleware` (registered first) converts unhandled exceptions into
RFC-7807 `application/problem+json`, status from the pure `ExceptionStatusMapper`
(`ArgumentException`/`InvalidOperationException` → 400, `KeyNotFoundException` → 404,
`UnauthorizedAccessException` → 401, `IRacingNotConfiguredException` → 503, else 500 with its message
hidden). Services should just `throw`; don't catch to `BadRequest(string)`. Controllers still return
explicit results for non-exception outcomes needing a specific code (e.g. AuthController's 423 lockout).

### Controllers — use-case-oriented, NOT entity-CRUD

Each controller is one user-facing capability (not a per-entity CRUD surface). `[Authorize]` unless
marked **public**; iRacing-linked endpoints return a typed `409` (`IRACING_NOT_LINKED`) when unlinked.

| Controller                            | Capability                                                                                                                                                                                |
| ------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SeriesController`                    | active weekly series list                                                                                                                                                                 |
| `WeekController`                      | cars + aggregate lap stats for a week (**public**); `my-percentiles` (Authorize) for "Your pct"                                                                                           |
| `PercentileController`                | driver's lap-time percentile for a car+week (computes + caches)                                                                                                                           |
| `RecommendationController`            | ranked car recommendations for the user                                                                                                                                                   |
| `StrategyController`                  | week strategy briefing — track/pit, weather risk, per-car BoP + shift (**public**; personalizes)                                                                                          |
| `AuthController`                      | register/login/refresh/logout, profile, theme, role self-service, password change + reset, email-change verify, iRacing OAuth callback (reset/forgot/confirm-email-change are **public**) |
| `TelemetryController`                 | `.ibt` upload + personal best laps                                                                                                                                                        |
| `AdminController`                     | user role + feature flag CRUD (AdminOnly)                                                                                                                                                 |
| `FeatureFlagsController`              | caller's active feature flags                                                                                                                                                             |
| `UserAnalyticsController`             | per-user analytics, optional series filter                                                                                                                                                |
| `ProgressionController`               | per-category iRating/SR/CPI/TT + iRating history                                                                                                                                          |
| `ProfileStatsController`              | enriched driver profile (identity, licenses, career, recap)                                                                                                                               |
| `AchievementsController`              | awards trophy case                                                                                                                                                                        |
| `RaceHistoryController`               | recent official races                                                                                                                                                                     |
| `SubsessionController`                | full classified field for one subsession (**public**); per-lap pace trace (Authorize)                                                                                                     |
| `ScheduleController`                  | active-season schedule + weather + BoP + PB overlay (**public**)                                                                                                                          |
| `LeaderboardController`               | global top-200 by iRating for a category                                                                                                                                                  |
| `StandingsController`                 | championship / TT / qualifying standings per car class (**public**)                                                                                                                       |
| `RaceGuideController`                 | official sessions starting in the next ~3 h (**public**)                                                                                                                                  |
| `RivalsController`                    | rivals a user follows — list/add (idempotent)/remove, search, suggestions                                                                                                                 |
| `CompareController`                   | head-to-head between caller and a rival                                                                                                                                                   |
| `CarsController` / `TracksController` | browsable car/track catalog + detail (**public**; "your best laps" overlay; `404`)                                                                                                        |

### Services (`src/ApexRacers.Api/Services/`)

One responsibility per class; pure heuristics/mappers/parsers are extracted and unit-tested directly.

- `SeriesService`, `WeekCarStatsService` — series list; per-car week lap stats.
- `PercentileCalculationService` — compute + cache percentile; overlays world-record via `WorldRecordService`.
- `CarRecommendationService` — ranked recommendations from personal percentile data.
- `StrategyService` (+ pure `StrategyAnalysis`) — week briefing from BoP + weather + track/pit; personal overlay.
- `UserAnalyticsService` — per-car percentile history/stats.
- `MemberStatsService` — progression / driver profile / comparison-side via `CachedIRacingClient` (6 h).
- `AchievementsService` (+ pure `AchievementsMapper`) — awards trophy case (6 h).
- `RaceHistoryService` — recent official races (10 min); resolves car names from local catalog.
- `SubsessionDetailService` — one ingested subsession from the DB; normalizes stored weather units.
- `LapDataService` (+ pure `LapAnalysis`) — per-lap pace + pace stats (24 h).
- `ScheduleService` — active-season schedule (weeks + track + weather/BoP) + PB overlay.
- `WorldRecordService` — fastest car+track lap (24 h); null when iRacing unconfigured.
- `LeaderboardService` (+ pure `LeaderboardCsvParser`) — category global top-200 (24 h).
- `StandingsService` (+ pure `QualifyResultsParser`, `IChunkDownloader`) — driver/TT/qualifying standings (24 h). Qualifying is special-cased: the SDK omits the qual lap time, so it downloads + parses the chunk files itself.
- `RaceGuideService` — "race now" board (60 s).
- `RivalService` — follow/search (30 min/term)/suggestions (from shared `SubsessionResult` rows).
- `RivalComparisonService` (+ pure `SharedRaceAnalysis`) — assembles the head-to-head DTO.
- `CarCatalogService` / `TrackCatalogService` (+ pure `CarCatalogMapper` / `TrackCatalogMapper`) — catalog read from the **persisted** `Car`/`Track` tables + PB overlay; no creds at read time.
- `ExternalDataCacheCleanupService` (+ pure `PurgeExpiredAsync`) — purges long-expired cache rows every 6 h.
- `AuthService` — registration, login (JWT + rotating refresh token), profile/password/email-change, reset. Caps active refresh tokens at 5 per user; revokes all on password/email change. Needs `AddDefaultTokenProviders()`.
- `IEmailSender` / `AcsEmailSender` / `LoggingEmailSender` (+ pure `AccountEmailTemplates`) — transactional email over the `OutboundEmail` DTO; binds ACS when configured, else logs subject only (links/tokens never logged). Links built from `APP_BASE_URL`.
- `TelemetryUploadService`, `PersonalLapService` — parse `.ibt` → `PersonalLap`; query personal bests.
- `AdminService` — role + flag CRUD. Users are **single-role** (`Standard` < `Beta` < `Alpha` < `Admin`); flag eligibility is hierarchical (`MinimumRole` level ≤ user level).
- `CachedIRacingClient` — get-or-fetch over `IDataClient`; throws `IRacingNotConfiguredException` when creds absent.
- `MemberContext` — resolves the user's iRacing `cust_id`; null when unlinked → typed `409`. The **only** demo-aware branch: under `iracing-demo` it resolves to `DemoData.DriverCustId` (= 100001).

### Core models (`src/ApexRacers.Core/Models/`)

Domain entities below — see the `postgres-specialist` agent for full schema (PK types, schemas,
indexes, FK/`OnDelete` behavior).

| Model                                           | Purpose                                                                                        |
| ----------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| `ApplicationUser`                               | extends `IdentityUser<Guid>` — adds `DisplayName`, `IRacingCustomerId`, `ThemePreference`      |
| `Series` / `Season` / `Week`                    | series → season → race week (`Week.Id` is a Guid; carries weather summary JSON)                |
| `Track` / `Car` / `CarClass` / `CarClassCar`    | iRacing catalog + car-class membership                                                         |
| `SeasonCar` / `SeasonCarClass` / `SeasonCarBop` | per-season cars/classes; per-week BoP (composite PK)                                           |
| `Subsession` / `SubsessionResult`               | one race session + per-driver result (+ race context, weather/track-state JSON)                |
| `PersonalLap`                                   | user's personal best per track+car (from telemetry)                                            |
| `CarPercentileResult`                           | cached percentile rank per (UserId, CarId, SeriesId, WeekId)                                   |
| `FeatureFlag`                                   | feature flag (`Key` unique; `MinimumRole`)                                                     |
| `RefreshToken`                                  | rotating refresh token (SHA-256 `TokenHash`; `identity` schema)                                |
| `ExternalDataCache`                             | cached iRacing response (`CacheKey` unique, serialized DTO JSON) — backs `CachedIRacingClient` |
| `Rival`                                         | a driver a user follows (unique on (UserId, RivalCustId); cascade FK to Users)                 |

### iRacing data ingestion

`ApexRacers.Ingestion` is a `BackgroundService` using `Aydsko.iRacingData` with
`UsePasswordLimitedOAuth()` (four `IRACING_*` env vars). Resolve `IDataClient` + `AppDbContext` per cycle
through `IServiceScopeFactory` (scoped services from a singleton worker). Each run refreshes the full
car/track catalog into `Car`/`Track` via the pure, tested `CatalogIngest` helper. (See `dotnet-api`.)

### Data source strategy — persist vs cache (read before adding an iRacing-backed feature)

Three ways iRacing data reaches a read path. Pick deliberately:

1. **Persist into typed entities** (worker/seeder → Postgres): `Series`, `Season`, `Week`, `Track`,
   `Car`, `Subsession`, `SubsessionResult`, `SeasonCarBop`, … Read paths query with SQL/joins. Pure
   SDK→entity mapping goes in a tested helper (mirror `SubsessionIndexer` / `CatalogIngest`).
2. **On-demand cache** (`CachedIRacingClient` → `ExternalDataCache`): fetch live per request, memoize
   **mapped DTOs** as JSON with a per-call TTL. Backs progression, profile, race history, lap data,
   world records, leaderboards, standings, race guide, driver search.
3. **Persisted user-owned data** (not from iRacing): `PersonalLap`, `Rival`, `CarPercentileResult`, Identity.

Choose **persist (#1)** if you need to query/filter/aggregate/join it in SQL, it's canonical/shared, or
you need point-in-time history. Choose **cache (#2)** for read-mostly, staleness-tolerant, per-user/-query
stat lookups displayed roughly as-is (the common member/season-stat case). Don't keep a second cached
copy of something that already has an entity (e.g. the car/track catalog).

**Cache rules (#2):** cache mapped DTOs, **never** raw Aydsko SDK types (their wire shape drifts and
carries `[Obsolete]` fields). TTL guidance: race guide 60 s; recent races 10 min; driver search 30 min;
member profile/career/chart 6 h; world records / leaderboards / standings 24 h. Eviction is TTL-only
(lazy); `ExternalDataCacheCleanupService` purges long-expired rows.

**Demo cache seeding** (`ApexRacers.Seeder --demo` → `DemoCacheSeeder`): seeds `ExternalDataCache` rows
with synthetic mapped DTOs under each service's **exact** runtime cache keys, with a far-future
`ExpiresAt` sentinel (`>= 9000-01-01`) so cleanup never evicts them; also seeds synthetic `SeasonCarBop`,
`Week.WeatherSummaryJson`, the percentile world-record overlay, lap traces, and curated `/compare`
driver-search terms. The Seeder references `ApexRacers.Api` to reuse the real cached DTO types, so seeded
JSON matches what live services write. **Demo caveats** (not page-breakers): `/analytics` populates lazily
after a Recommendations/percentile visit; the race-guide board shows static "in-progress" sessions;
`/compare` search only hits a curated term set (arbitrary terms 503 — use the suggestions list instead).

### Frontend (`web/`)

The typed API client is `web/src/services/api.ts` — **all** fetch calls route through one private
`request<T>(path, init)` helper (attaches auth headers, retries once after silent refresh on 401, maps
RFC-7807 errors, throws typed `IRacingNotLinkedError` on the `409`). Add endpoints by calling `request`
with `{ method, json }` or `{ method, body }` — don't reintroduce per-verb helpers. `api.ts` response
types must stay in sync with `ResponseDtos.cs`. Full frontend patterns (auth/`AuthContext`, 401
interceptor, feature flags, **design-token system + card pattern**, Vitest rules) are in the
`react-frontend` agent — don't duplicate the design-token tables here.

**Design system:** all sizing is fluid via `clamp()` utility classes in `web/src/index.css` (not
Tailwind breakpoints); use those classes for new UI. Primary accent is **cyan** —
`text/bg/border-primary-container` for all accent use; never hardcode the old greens (`#00FF88`,
`#00e479`, green RGBA glows). `primary-fixed-dim` is the allowed dim-accent token. (Class catalog +
`cardStyle`/`scanTexture` pattern: `react-frontend` agent.)

#### Routing (`web/src/App.tsx`)

Two tiers. **Public** (no AppShell): `/`, `/login`, `/forgot-password`, `/reset-password`,
`/verify-email`, `/terms`, `/privacy`. **App** (inside `AppShell` — Sidebar + TopNav + Footer):

| Path                                                                    | Page                                              |
| ----------------------------------------------------------------------- | ------------------------------------------------- |
| `/dashboard`                                                            | recent laps, active series, driver-stat KPIs      |
| `/series` · `/series/:id/schedule` · `/series/:id/standings`            | series list / schedule / standings tabs           |
| `/series/:id/weeks/:n` · `…/strategy` · `…/cars/:carId/percentile`      | week detail / strategy / percentile breakdown     |
| `/analytics` · `/progression` · `/races` · `/leaderboards` · `/compare` | per-user iRacing surfaces                         |
| `/cars` · `/cars/:id` · `/tracks` · `/tracks/:id`                       | catalog grid + detail                             |
| `/live` · `/races/:subsessionId`                                        | race-now board / race detail (public)             |
| `/recommendations` · `/my-laps` · `/telemetry`                          | recommendations / personal bests / `.ibt` upload  |
| `/profile` · `/settings` · `/support` · `/admin`                        | profile / settings / support / admin (AdminGuard) |

- **`AdminGuard`** (wraps `/admin`): unauthenticated → `/login`; authed non-admin → `/dashboard`.
- **`RequireFlag`** (wraps iRacing-dependent routes): renders `ComingSoonPage` when **both**
  `iracing-live` and `iracing-demo` are off; else renders the child. Both hooks called unconditionally
  and OR-ed. Auth-independent. Dashboard/Profile degrade gracefully but their iRacing panels still gate
  on `iracing-live` **only** (a known Plan-1 limitation — the dedicated routes render the demo data).

#### Components, contexts, utilities

- **Components** (`web/src/components/`): `Sidebar` / `TopNav` (nav filtered by the `iracing-live`
  OR `iracing-demo` flag via shared `visibleNav`), `NotificationsBell` (client-derived alerts via pure
  `deriveAlerts`), `DemoBanner` (shows while `iracing-demo` on), `Footer`, and the SVG charts
  `Sparkline` / `PercentileBadge` / `LapTraceChart` / `IRatingCompareChart` (each returns `null` below
  its minimum data points — guard the wrapper).
- **Contexts** (`web/src/context/`): `AuthContext` (session, JWT + refresh, silent refresh,
  `alertsEnabled`), `ThemeContext` (auto/light/dark, persists via `PUT /api/auth/theme`),
  `FeatureFlagContext` (`useFeatureFlag(key)`).
- **Utilities** (`web/src/utils/`): import the shared `formatLapTime`, `topPercentLabel`,
  `deriveAlerts`, `breadcrumbs` — **don't** re-inline these in pages.

---

## Testing & coverage

Both stacks enforce **85%** coverage; changes aren't done until it passes. The `dotnet-api` and
`react-frontend` agents carry the per-stack test rules — the load-bearing facts:

- **Frontend (Vitest):** thresholds (statements/branches/functions/lines) in `vite.config.ts`; CI also
  runs `npx prettier --check .` from `web/` (unformatted files block deploy). Run:
  `cd web && npx vitest run --coverage`.
- **Backend (.NET, xUnit in `src/ApexRacers.Tests/`):** 85% **line and branch** (CI gates both —
  `irongut/CodeCoverageSummary` for line, a `branch-rate` step for branch). Test services + `Core`
  helpers directly; controllers are excluded. Measure with `dotnet-coverage collect "dotnet test" -f xml`
  then `reportgenerator`.
- **E2E + accessibility (Playwright):** tests live in `web/e2e/`; run with `npm run test:e2e` against the
  full stack at `http://localhost:8080`. The suite includes axe-core WCAG 2.1 A/AA audits across 5 public
  + 7 authed pages (zero-violation gate, `web/e2e/a11y.spec.ts`). A non-blocking per-PR CI workflow
  (`.github/workflows/e2e.yml`) runs the suite. E2E tests are excluded from Vitest coverage. Full detail
  in the `react-frontend` agent.
- **Test DB provider:** `Helpers/DbContextFactory.Create()` uses **in-memory SQLite** (a real relational
  provider — queries must translate), with `Foreign Keys=False` so tests use minimal partial fixtures.
  Narrow exception `CreateInMemory()` is for the few production queries valid on Npgsql but untranslatable
  by SQLite (**`DateTimeOffset` range filters/ordering** — `ExternalDataCacheCleanupService`,
  `RivalService.ListAsync`). **Order/project by entity columns before constructing a DTO** — ordering by
  a positional-record DTO property doesn't translate on Npgsql or SQLite.

---

## General principles

- Prefer clarity over cleverness.
- Introduce patterns when complexity demands them, not preemptively.
- Each class has one clear responsibility.
- `// TODO:` stubs are fine during scaffolding — always describe what needs implementing.
