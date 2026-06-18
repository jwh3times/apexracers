# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

## Commands

### .NET (run from repo root)

```bash
# Build the entire solution
dotnet build

# Run the API (requires DATABASE_CONNECTION_STRING env var)
dotnet run --project src/ApexRacers.Api

# Run the ingestion worker (requires all iRacing + database env vars)
dotnet run --project src/ApexRacers.Ingestion

# Seed the database with catalog data + synthetic lap times for all 7 series (idempotent)
# Requires private/iracing-api-response-objects/ to be populated first (gitignored — see README).
# Requires DATABASE_CONNECTION_STRING or falls back to the local Docker default.
dotnet run --project src/ApexRacers.Seeder

# Remove ONLY the legacy seed_gt3_series.sql data (preserves new seeder data)
Get-Content src\ApexRacers.Data\Seeds\remove_gt3_seed.sql | docker compose exec -T postgres psql -U apexracers -d apexracers

# Remove ALL synthetic seed data so the database can be re-seeded from scratch
Get-Content src\ApexRacers.Data\Seeds\truncate_seed_data.sql | docker compose exec -T postgres psql -U apexracers -d apexracers

# EF Core migrations — always target Data project, startup project Api
dotnet ef migrations add <MigrationName> --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
dotnet ef database update --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
```

> `dotnet-ef` must be installed globally: `dotnet tool install --global dotnet-ef`. The version must match EF Core (currently 10.0.7).

### Frontend (run from `src/web/`)

```bash
npm install

# Dev servers — Vite runs on localhost:5173, proxying /api to the target below
npm run dev          # → http://localhost:5000  (dotnet API running locally)
npm run dev:all      # → http://localhost:5000  (starts dotnet API + Vite together via concurrently)
npm run dev:docker   # → http://localhost:8080  (API running in Docker Desktop)
npm run dev:cloud    # → https://apexracers-api.azurewebsites.net  (Azure deployed API)

npm run build        # tsc + Vite production build
npm run lint         # ESLint
npm run preview      # Serve the production build locally

# Tests
npm run test         # Vitest one-shot run
npm run test:watch   # Vitest in watch mode

# Formatting (required by CI — run before pushing)
npx prettier --check .   # Check formatting (same check CI runs)
npx prettier --write .   # Auto-fix formatting
```

The API proxy target is controlled by `API_TARGET` in the relevant `.env.*` file (`src/web/.env.docker`, `src/web/.env.cloud`). The default (`npm run dev` / `dev:all`) falls back to `http://localhost:5000` with no env file needed.

### Infrastructure

```bash
# Local Docker Desktop (postgres + pgadmin + api)
docker compose up -d

# Local Docker Desktop (postgres + pgadmin + api) with rebuild
docker compose up -d --build

# Include ingestion worker
docker compose --profile ingestion up -d

# Seed the database with initial data
Get-Content src\ApexRacers.Data\Seeds\seed_gt3_series.sql | docker compose exec -T postgres psql -U apexracers -d apexracers
```

Copy `.env.example` to `.env` and fill in `JWT_SIGNING_KEY` before running. `DATABASE_CONNECTION_STRING` is pre-filled for the Docker network.

**Ports:** see the [Ports table in README.md](README.md#ports) for the full map of ports across all config files (Postgres 5432, pgAdmin 5050, API 8080/5000, Vite 5173).

### Azure (resource group: apexracers-rg)

| Resource | Type | Location |
| --- | --- | --- |
| `apexracersacr` | Container Registry | eastus |
| `apexracers-kv` | Key Vault | eastus |
| `apexracers-pg` | PostgreSQL Flexible Server | westus3 |
| `apexracers-plan` | App Service Plan | westus3 |
| `apexracers-api` | App Service (API) | westus3 |
| `apexracers-env` | Container Apps Environment | westus3 |
| `apexracers-ingestion` | Container App (ingestion worker) | westus3 |
| `workspace-apexracersrg0n6Q` | Log Analytics Workspace | westus3 |
| `apexracers.gg` | SSL Certificate | westus3 |

The API is deployed as an App Service; the ingestion worker runs as a Container App. Key Vault secret names use hyphens (e.g. `JWT-SIGNING-KEY`) and are mapped to underscore env var names by `HyphenToUnderscoreSecretManager` in both `Program.cs` files.

---

## Architecture

### Project dependency graph

```text
ApexRacers.Core   ← no dependencies
      ↑
ApexRacers.Data   ← EF Core, Npgsql
      ↑
ApexRacers.Api    ← Web API, Swagger, services, controllers
ApexRacers.Ingestion ← Worker Service, Aydsko.iRacingData
ApexRacers.Seeder ← CLI seeder (synthetic lap data, idempotent)
ApexRacers.Tests  ← xUnit tests (references Api + Core + Data)
```

`Core` is the only project with no external dependencies. Both `Api` and `Ingestion` reference `Core` and `Data` but never each other. `Seeder` references `Core` and `Data` directly and is never referenced by other projects.

### NuGet package management

All package versions are centrally managed in `Directory.Packages.props` at the repo root. **Do not add `Version="..."` attributes to `<PackageReference>` elements in `.csproj` files.** Add new packages via `dotnet add package` — NuGet will write the version into `Directory.Packages.props` automatically.

`CentralPackageTransitivePinningEnabled=true` is set intentionally: `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1` pulls EF Core 10.0.4 as a transitive dependency, which conflicts with the explicit 10.0.7 pin. The transitive pinning forces all projects to resolve 10.0.7 everywhere.

### API request flow

```text
HTTP request → Controller (parameter binding only)
                    ↓
              Service class (all business logic, EF Core queries)
                    ↓
              AppDbContext → PostgreSQL
```

Controllers contain no logic beyond binding HTTP inputs and returning `Ok(result)`. Services live in `src/ApexRacers.Api/Services/`. Response shapes are defined as `record` types in `src/ApexRacers.Api/Dtos/ResponseDtos.cs`.

**Error handling:** `ExceptionHandlingMiddleware` (registered first in the pipeline) converts unhandled exceptions into RFC-7807 `application/problem+json` responses, with the status from the pure `ExceptionStatusMapper` (`ArgumentException`/`InvalidOperationException` → 400, `KeyNotFoundException` → 404, `UnauthorizedAccessException` → 401, `IRacingNotConfiguredException` → 503, else 500; 500 hides its message). Services should just `throw` for these cases rather than catching to `BadRequest(string)`. Controllers still return explicit results for non-exception outcomes that need a specific code (e.g. AuthController's 423 lockout, 404/501) — those bypass the middleware.

### Backend API design — controllers are use-case-oriented, NOT entity-CRUD

Do not create generic CRUD controllers per entity. Each controller represents one user-facing capability:

- `SeriesController` — active weekly series list
- `WeekController` — cars and aggregate lap stats for a series week
- `PercentileController` — driver's lap time percentile for a specific car and week (computes and caches)
- `RecommendationController` — ranked car recommendations for the authenticated user
- `AuthController` — account management: register, login, token refresh (`POST /api/auth/refresh`), logout/revoke (`POST /api/auth/logout`), profile update (`PUT /api/auth/profile`), theme update (`PUT /api/auth/theme`), iRacing OAuth 2.0 callback (`POST /api/auth/callback`)
- `TelemetryController` — iRacing `.ibt` file upload (`POST /api/telemetry/upload`) and personal best laps (`GET /api/telemetry/laps`)
- `AdminController` — user role management and feature flag CRUD (`/api/admin`, requires AdminOnly policy)
- `FeatureFlagsController` — returns the caller's active feature flags (`/api/feature-flags`)
- `UserAnalyticsController` — per-user analytics summary, optionally filtered by series (`/api/users/me/analytics`)
- `ProgressionController` — per-category iRating / SR / CPI / TT with iRating history for the authenticated user (`/api/users/me/progression`); typed `409` when iRacing is unlinked
- `ProfileStatsController` — enriched driver profile (identity, license badges, lifetime career stats, recap favorites) for the authenticated user (`/api/users/me/profile-stats`); typed `409` when iRacing is unlinked
- `RaceHistoryController` — the authenticated driver's recent official races (`/api/users/me/races`); typed `409` when iRacing is unlinked
- `SubsessionController` — full classified field + session context for one ingested subsession (`GET /api/subsessions/{id}`, **public** — official race data; `404` on unknown id); also a driver's per-lap pace trace (`GET /api/subsessions/{id}/laps?customerId=`, Authorize; defaults to the caller's cust_id; typed `409` when unlinked)

If an action requires multiple steps, extract the logic into a focused service class injected via DI (e.g. `PercentileCalculationService`, `CarRecommendationService`). Do not use MediatR, command handlers, or query handlers.

Services in `src/ApexRacers.Api/Services/`:

- `SeriesService` — active series list
- `WeekCarStatsService` — aggregate lap stats per car for a series week
- `PercentileCalculationService` — compute and cache driver percentile rank
- `CarRecommendationService` — ranked car recommendations based on personal percentile data
- `UserAnalyticsService` — per-car percentile history and stats for the authenticated user
- `MemberStatsService` — on-demand iRacing member stats via `CachedIRacingClient` (6 h TTL). `GetProgressionAsync` returns one card per license category (iRating/SR/CPI/TT + iRating history); `GetDriverProfileAsync` returns the enriched profile (identity, license badges, career stats, this-year summary, recap favorites). Reuses the shared `profile:{custId}` cache entry
- `RaceHistoryService` — the authenticated driver's recent official races via `CachedIRacingClient` (10 min TTL); maps iRating/SR deltas and resolves car names from the local `Car` catalog
- `SubsessionDetailService` — reads one ingested subsession (header context + classified field) from the DB; deserializes the stored weather block and normalizes temp/wind units (pure `TempToCelsius`/`WindToKph`/`MapWeather` helpers)
- `LapDataService` — a driver's per-lap pace for one race via `CachedIRacingClient` (24 h TTL; SDK auto-fetches the chunked lap rows); maps laps and computes pace stats via the pure `LapAnalysis` helper (mean/σ/fastest/degradation slope over green laps)
- `AuthService` — registration, login (JWT + refresh token), refresh token rotation, token revocation, profile updates
- `TelemetryUploadService` — parse `.ibt` file, extract valid laps, persist to `PersonalLap`
- `PersonalLapService` — query personal best laps per track+car
- `AdminService` — user role management and feature flag CRUD
- `CachedIRacingClient` — get-or-fetch cache over the iRacing `IDataClient` (TTL per `ExternalDataCache`); throws `IRacingNotConfiguredException` when iRacing creds are absent
- `MemberContext` — resolves the authenticated user's iRacing `cust_id` from the DB; returns null when unlinked, which controllers turn into a typed `409` `NotLinkedDto` (`IRACING_NOT_LINKED`) via `ControllerExtensions.IRacingNotLinked()`

### No over-abstraction

Do not create generic repository interfaces (`IRepository<T>`). Use `AppDbContext` directly in service classes. Introduce abstractions only when they solve a concrete problem.

### Core models

`src/ApexRacers.Core/Models/` contains the domain entities:

| Model | Description |
| --- | --- |
| `ApplicationUser` | Extends `IdentityUser<Guid>`; adds `DisplayName`, `IRacingCustomerId`, `ThemePreference` |
| `Series` | iRacing series (Id, Name, SeasonId, CurrentWeekNumber) |
| `Season` | Season for a series (SeasonId, Year) |
| `Week` | Race week (SeasonId, WeekNumber, TrackId, StartDate) |
| `Track` | Full iRacing track catalog (Id, Name, ConfigName, Category, TrackConfigLength, IsDirt, IsOval, Location, TimeZone, Retired) |
| `Car` | iRacing car (Id, Name, RelativeSpeed) |
| `CarClass` | iRacing car class grouping |
| `CarClassCar` | Many-to-many join between CarClass and Car |
| `SeasonCar` | Cars available in a season |
| `SeasonCarClass` | Car classes available in a season |
| `Subsession` | iRacing race session (Id, SeasonId, WeekNumber, TrackId, OfficialSession, EventStrengthOfField, StartTime, EndTime, SplitNum) + race context (NumCautions, NumCautionLaps, NumLeadChanges, CornersPerLap, EventAverageLapSeconds, EventBestLapSeconds, EventLapsComplete, WeatherJson, TrackStateJson) |
| `SubsessionResult` | One driver's result in a subsession (CarId, CarClassId, FinishPosition, BestLapSeconds, Incidents, …) + DisplayName, QualLapSeconds, New/OldSubLevel, New/OldTtRating |
| `PersonalLap` | User's personal best lap per track+car (UserId, CarId, TrackId, LapTimeSeconds, IsValidLap, TrackTempCelsius, TrackWetness, RecordedAt) |
| `CarPercentileResult` | Cached percentile rank (UserId, CarId, SeriesId, WeekId, PercentileRank, SampleSize, ComputedAt) |
| `FeatureFlag` | Feature flag (Id, Key, Name, Description, IsEnabled, MinimumRole, CreatedAt, UpdatedAt) |
| `RefreshToken` | Rotating refresh token (Id, UserId, TokenHash [SHA-256 hex], ExpiresAt, CreatedAt, RevokedAt?); stored in `identity` schema |
| `ExternalDataCache` | Cached external (iRacing) API response (Id, CacheKey [unique], Payload [serialized JSON, `text`], FetchedAt, ExpiresAt); backs `CachedIRacingClient` get-or-fetch |

### iRacing data ingestion

`ApexRacers.Ingestion` is a standalone `BackgroundService` worker. It uses `Aydsko.iRacingData` registered with `UsePasswordLimitedOAuth()` (four env vars: `IRACING_USERNAME`, `IRACING_PASSWORD`, `IRACING_CLIENT_ID`, `IRACING_CLIENT_SECRET`). The `IDataClient` is resolved per ingestion cycle through `IServiceScopeFactory` to safely use a scoped `AppDbContext` from a singleton service.

### Frontend

Vite dev server proxies all `/api` requests to `http://localhost:5000` (the API). The typed API client is in `src/web/src/services/api.ts` — all fetch calls go through it. Response types in `api.ts` must stay in sync with `ResponseDtos.cs` in the API.

Every method routes through one private `request<T>(path, init)` helper that attaches auth headers, retries once after a silent token refresh on 401, returns `undefined` for 204s, and maps errors via `throwForResponse`. Error messages prefer an RFC-7807 `detail`, then the raw body, then the status line. A typed `IRacingNotLinkedError` is thrown on a `409` carrying `code: "IRACING_NOT_LINKED"` so pages can show a "link your iRacing account" prompt instead of a generic error (see `RecommendationsPage`). Add new endpoints by calling `request` with `{ method, json }` (JSON body) or `{ method, body }` (raw/FormData) — do not reintroduce per-verb helpers.

#### Routing architecture

The app has two layout tiers defined in `src/web/src/App.tsx`:

**Public routes (no AppShell — own layout):**

| Path | Component |
| --- | --- |
| `/` | `HomePage` — marketing landing page with its own header/nav/footer |
| `/login` | `LoginPage` |
| `/terms` | `TermsOfServicePage` |
| `/privacy` | `PrivacyPolicyPage` |

**App routes (nested inside `AppShell` — Sidebar + TopNav + Footer):**

| Path | Component |
| --- | --- |
| `/dashboard` | `DashboardPage` — recent laps, active series, welcome |
| `/series` | `SeriesPage` — browse all series |
| `/series/:seriesId/weeks/:weekNumber` | `WeekDetailPage` — cars and lap stats for a week |
| `/series/:seriesId/weeks/:weekNumber/cars/:carId/percentile` | `PercentileCarPage` — detailed percentile breakdown |
| `/analytics` | `AnalyticsPage` — per-car percentile history with sparklines |
| `/progression` | `ProgressionPage` — per-category iRating/SR/CPI/TT cards with iRating sparklines |
| `/races` | `RacesPage` — recent race history table with iRating/SR deltas and series filter |
| `/races/:subsessionId` | `RaceDetailPage` — full classified field + session context (public; highlights your row) |
| `/recommendations` | `RecommendationsPage` — ranked car recommendations for current week |
| `/my-laps` | `MyLapsPage` — personal best per track+car |
| `/telemetry` | `TelemetryPage` — upload `.ibt` files, view extracted lap summaries |
| `/profile` | `ProfilePage` — user profile with series/lap stats |
| `/settings` | `SettingsPage` — display name, email, iRacing ID, theme, role tier, logout |
| `/admin` | `AdminPage` — user role management and feature flag CRUD (AdminGuard) |

**`AdminGuard`** — wraps `/admin`. Unauthenticated users are sent to `/login`; authenticated non-admin users are sent to `/dashboard` (not `/`).

#### Fluid design system

All sizing in the frontend scales continuously with viewport width via `clamp()` rather than Tailwind responsive breakpoints. The utility classes are defined in `src/web/src/index.css` and must be used for any new UI work — do not reach for one-off Tailwind classes for the same purposes.

##### Typography

| Class | Purpose |
| --- | --- |
| `text-page-title` | Large page heading (`h1`) |
| `text-section-head` | Card / panel section heading (`h2`, `h3`) |
| `text-eyebrow` | Mono ALL-CAPS label above a heading |
| `text-body-fluid` | Standard body and list text |
| `text-small-fluid` | Secondary / supporting text |
| `text-th` | Table column header |
| `text-kpi-value` | Large mono KPI number |
| `text-mono-fluid` | Mono data values — lap times, rank numbers |

##### Layout & spacing

| Class | Purpose |
| --- | --- |
| `page-wrap` | Outer page padding — apply to `<main>` |
| `card-r` | Card border-radius |
| `card-p` | Card body padding |
| `card-hp` | Card section-header padding (scan-texture rows) |
| `kpi-p` | KPI tile padding |
| `td-p` / `th-p` | Table cell / header padding |
| `gap-fluid` / `gap-fluid-lg` | Column/row gaps |
| `btn-fluid` / `btn-fluid-sm` | Button height, padding, font-size, radius |
| `grid-kpi` | Auto-fit KPI tile grid (wraps naturally) |
| `grid-cards` | Auto-fill series card grid |

##### Standard card pattern

Every card uses a consistent combination of a CSS `cardStyle` constant for the box-shadow and optional `scanTexture` for header backgrounds:

```tsx
const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};
const scanTexture: React.CSSProperties = {
  backgroundImage: 'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

// Usage:
<div className="card-r border border-white/10 bg-surface overflow-hidden" style={cardStyle}>
  <div className="card-hp border-b border-white/10 flex items-center justify-between" style={scanTexture}>
    <h3 className="text-section-head text-on-surface">Section title</h3>
  </div>
  <div className="card-p">
    {/* body content */}
  </div>
</div>
```

##### Color tokens

The primary accent is cyan, not green. Use `text-primary-container` / `bg-primary-container` / `border-primary-container` for all accent text, icon, button, and border use cases. `primary-fixed-dim` is the **dim accent** token — it is remapped to cyan `#00b8d4` in `index.css` and is allowed for muted accent text/borders. Do not hardcode old green values: the literal hexes `#00FF88` / `#00e479`, or raw green RGBA glows like `rgba(0,228,121,…)` and `rgba(0,255,136,…)`. For accent glows use a cyan RGBA such as `rgba(0,224,255,…)` or a `var(--color-primary-container)`-based shadow.

#### Shared components

`src/web/src/components/` contains:

- `Sidebar.tsx` — Persistent left navigation (Dashboard, Series, Analytics, Progression, Recommendations, Race History, My Laps, Telemetry, Settings, Profile, Admin)
- `TopNav.tsx` — Global header with user profile tile, logout, theme toggle
- `Footer.tsx` — Global footer (rendered inside AppShell)
- `Sparkline.tsx` — SVG area-chart for percentile history. Accepts `data: number[]`, optional `w` and `h`. Returns `null` when `data.length < 2`. Always guard the wrapper element so an empty flex slot is not created:

```tsx
{sparkData.length >= 2 && (
  <div className="w-full">
    <Sparkline data={sparkData} w={460} h={76} />
  </div>
)}
```

- `PercentileBadge.tsx` — Ring gauge showing "TOP X%". Accepts `pct: number` (the TOP value, e.g. `4` for "TOP 4%"; lower is better) and `size: 'sm' | 'md' | 'lg'`.
- `LapTraceChart.tsx` — SVG per-lap pace trace (line + mean ± 1σ band + red incident markers). Accepts `laps: Lap[]`, `meanSeconds`, `stdDevSeconds`, optional `h`. Returns `null` when fewer than two timed laps. Used by the "Your Race Pace" card on `RaceDetailPage`.

#### Contexts

`src/web/src/context/` contains three React contexts:

- `AuthContext` — Manages user session, JWT access token + refresh token, silent token refresh on startup (when JWT is expired but refresh token is valid), login/logout (logout revokes the refresh token), profile updates, role tier selection, and alert toggle. Wrap components that need auth state with `useAuth()`.
- `ThemeContext` — Manages theme preference (`auto` / `light` / `dark`), applies the CSS class to `<html>`, and persists the selection to the API via `PUT /api/auth/theme`.
- `FeatureFlagContext` — Fetches and caches the user's eligible feature flags based on their role. Exposes `hasFlag(key)` for conditional feature rendering.

#### Shared utilities

`src/web/src/utils/lapTime.ts` exports `formatLapTime(seconds: number): string`. **Do not define local copies of this function in page files.** Pages that import it correctly: `AnalyticsPage`, `ProfilePage`, `TelemetryPage`, `DashboardPage`, `MyLapsPage`, `RecommendationsPage`. Always import from the shared module.

### EF Core design-time factory

`DesignTimeDbContextFactory` in `ApexRacers.Data` reads `DATABASE_CONNECTION_STRING` from the environment and falls back to a hardcoded local dev connection string. This is what allows `dotnet ef` commands to work without setting env vars manually.

---

## Testing & coverage requirements

### Frontend (Vitest)

Coverage thresholds are enforced in `vite.config.ts` at **80%** across statements, branches, functions, and lines. `npx vitest run --coverage` must exit cleanly (no threshold errors) before any frontend change is considered done. When adding new source files, add corresponding tests to keep all four metrics above 80%.

The CI `test` job (`.github/workflows/deploy.yml`) also runs `npx prettier --check .` from `src/web/` after `npm ci` and before the Vitest coverage step. Any unformatted file blocks both deploy jobs. Run `npx prettier --write .` locally to fix formatting before pushing.

Run coverage:

```bash
cd src/web && npx vitest run --coverage
```

### Backend (.NET)

Unit test coverage must also remain above **80%** for both **line** and **branch** coverage. CI enforces both: `irongut/CodeCoverageSummary` gates line coverage, and a follow-up step reads `branch-rate` from the Cobertura report to gate branch coverage. Use `dotnet-coverage` + `reportgenerator` to measure:

```bash
dotnet-coverage collect "dotnet test" -f xml -o coverage.xml
reportgenerator -reports:coverage.xml -targetdir:coverage-report -reporttypes:TextSummary
```

When adding new service logic, add corresponding xUnit tests in `src/ApexRacers.Tests/`. Controllers are excluded from coverage targets (they contain no logic). Services and domain helpers in `Core` are the primary targets.

Current test files in `src/ApexRacers.Tests/Services/`:

- `SeriesServiceTests`
- `WeekCarStatsServiceTests`
- `PercentileCalculationServiceTests`
- `CarRecommendationServiceTests`
- `UserAnalyticsServiceTests`
- `AuthServiceTests`
- `TelemetryUploadServiceTests`
- `PersonalLapServiceTests`
- `IbtParserTests` (telemetry `.ibt` file parsing)

---

## General principles

- Prefer clarity over cleverness
- Introduce patterns when complexity demands them, not preemptively
- Each class has one clear responsibility
- `// TODO:` comments are acceptable stubs during scaffolding — always include a description of what needs implementing
