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
# Requires iracing-api-response-objects/ to be populated first (gitignored — see README).
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

If an action requires multiple steps, extract the logic into a focused service class injected via DI (e.g. `PercentileCalculationService`, `CarRecommendationService`). Do not use MediatR, command handlers, or query handlers.

Services in `src/ApexRacers.Api/Services/`:

- `SeriesService` — active series list
- `WeekCarStatsService` — aggregate lap stats per car for a series week
- `PercentileCalculationService` — compute and cache driver percentile rank
- `CarRecommendationService` — ranked car recommendations based on personal percentile data
- `UserAnalyticsService` — per-car percentile history and stats for the authenticated user
- `AuthService` — registration, login (JWT + refresh token), refresh token rotation, token revocation, profile updates
- `TelemetryUploadService` — parse `.ibt` file, extract valid laps, persist to `PersonalLap`
- `PersonalLapService` — query personal best laps per track+car
- `AdminService` — user role management and feature flag CRUD

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
| `Subsession` | iRacing race session (Id, SeasonId, WeekNumber, TrackId, OfficialSession, EventStrengthOfField, StartTime, EndTime, SplitNum) |
| `SubsessionResult` | One driver's result in a subsession (CarId, CarClassId, FinishPosition, BestLapSeconds, Incidents, …) |
| `PersonalLap` | User's personal best lap per track+car (UserId, CarId, TrackId, LapTimeSeconds, IsValidLap, TrackTempCelsius, TrackWetness, RecordedAt) |
| `CarPercentileResult` | Cached percentile rank (UserId, CarId, SeriesId, WeekId, PercentileRank, SampleSize, ComputedAt) |
| `FeatureFlag` | Feature flag (Id, Key, Name, Description, IsEnabled, MinimumRole, CreatedAt, UpdatedAt) |
| `RefreshToken` | Rotating refresh token (Id, UserId, TokenHash [SHA-256 hex], ExpiresAt, CreatedAt, RevokedAt?); stored in `identity` schema |

### iRacing data ingestion

`ApexRacers.Ingestion` is a standalone `BackgroundService` worker. It uses `Aydsko.iRacingData` registered with `UsePasswordLimitedOAuth()` (four env vars: `IRACING_USERNAME`, `IRACING_PASSWORD`, `IRACING_CLIENT_ID`, `IRACING_CLIENT_SECRET`). The `IDataClient` is resolved per ingestion cycle through `IServiceScopeFactory` to safely use a scoped `AppDbContext` from a singleton service.

### Frontend

Vite dev server proxies all `/api` requests to `http://localhost:5000` (the API). The typed API client is in `src/web/src/services/api.ts` — all fetch calls go through it. Response types in `api.ts` must stay in sync with `ResponseDtos.cs` in the API.

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

The primary accent is cyan, not green. Use `text-primary-container` / `bg-primary-container` / `border-primary-container` for all accent text, icon, button, and border use cases. Do not hardcode old green values (`#00FF88`, `#00e479`, `text-primary-fixed-dim`).

#### Shared components

`src/web/src/components/` contains:

- `Sidebar.tsx` — Persistent left navigation (Dashboard, Series, Analytics, Recommendations, My Laps, Telemetry, Settings, Profile, Admin)
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

#### Contexts

`src/web/src/context/` contains three React contexts:

- `AuthContext` — Manages user session, JWT access token + refresh token, silent token refresh on startup (when JWT is expired but refresh token is valid), login/logout (logout revokes the refresh token), profile updates, role tier selection, and alert toggle. Wrap components that need auth state with `useAuth()`.
- `ThemeContext` — Manages theme preference (`auto` / `light` / `dark`), applies the CSS class to `<html>`, and persists the selection to the API via `PUT /api/auth/theme`.
- `FeatureFlagContext` — Fetches and caches the user's eligible feature flags based on their role. Exposes `hasFlag(key)` for conditional feature rendering.

#### Shared utilities

`src/web/src/utils/lapTime.ts` exports `formatLapTime(seconds: number): string`. **Do not define local copies of this function in page files.** Pages that already import it correctly: `AnalyticsPage`, `ProfilePage`, `TelemetryPage`, `DashboardPage`. Always import from the shared module.

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

Unit test coverage must also remain above **80%** (line coverage). Use `dotnet-coverage` + `reportgenerator` to measure:

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
