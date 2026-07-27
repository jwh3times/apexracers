# AGENTS.md

This file is the canonical guidance for coding agents working in this repository. Claude Code reads it
via a one-line `@AGENTS.md` import in `CLAUDE.md`; other agent tools read it directly. Edit this file —
not `CLAUDE.md` — when project guidance changes.

> **Specialist agents carry the deep detail.** This file is the high-altitude map every session
> reads; the per-domain rules live in `.claude/agents/` and load only when that subagent runs. Pointers
> below say which agent owns what — don't duplicate their content back into this file.

| Work type                                                          | Agent                  |
| ------------------------------------------------------------------ | ---------------------- |
| .NET/EF features, services, auth, ingestion patterns               | `dotnet-api`           |
| React pages/components, design tokens, Vitest/Playwright rules     | `react-frontend`       |
| Schema, indexes, query patterns, migrations                        | `postgres-specialist`  |
| Dockerfiles, Compose, image builds                                 | `docker-containers`    |
| Cloud deployment patterns and runtime configuration                | `azure-infrastructure` |
| Reviewing a diff for correctness/security before merging           | `code-reviewer`        |
| Security testing (JWT/auth flows, data isolation, CORS, admin)     | `penetration-tester`   |
| Documentation sync after changes (CHANGELOG, AGENTS.md, README, …) | `docs-updater`         |

Docs are refreshed at **ship time** by the [`/ship` skill](.claude/skills/ship/SKILL.md): before
opening a PR it invokes `docs-updater` scoped to the branch diff, then rolls `[Unreleased]` into a
CHANGELOG section dated for the version the merge will mint. Run `/ship` (or say "ship it") when a
branch is ready for review.

## Agent config parity (Claude Code ↔ Codex)

Both tools run the same agents, skills, and session hooks from **one** set of sources. Claude Code's
files are canonical; the Codex tree is **generated**:

| Source (edit this)               | Generated (never edit)           |
| -------------------------------- | -------------------------------- |
| `.claude/agents/<name>.md`       | `.codex/agents/<name>.toml`      |
| `.claude/skills/<name>/SKILL.md` | `.agents/skills/<name>/SKILL.md` |
| `.claude/hooks/<file>`           | `.codex/hooks/<file>`            |

Regenerate with `node scripts/sync-agent-configs.mjs` and commit both sides; the **Agent Config Sync**
CI check (`.github/workflows/agent-config-sync.yml`) fails a PR whose generated tree has drifted or
that leaves an orphaned generated file behind. The generator copies prose **verbatim** — it never
rewrites wording — so keep agent and skill bodies **tool-neutral**: don't name one tool's entry-point
file where "the project guide" will do, and write repo-root-relative paths as plain text rather than
relative Markdown links (a relative link resolves differently from the mirrored location).

Frontmatter maps as follows: `name`/`description` carry over; a `tools:` list with no `Write`/`Edit`
becomes `sandbox_mode = "read-only"`; `model:` is **dropped** (Claude model names are not Codex model
names, so Codex uses its session default). The generator also self-checks: it round-trips every
generated TOML through an independent parser (catching an escaping regression before it ships) and
lints the mirrored prose for substitution artifacts and depth-fragile relative links.

`.codex/hooks.json` and `.codex/config.toml` are **hand-maintained**, not generated. `config.toml` is
the Codex counterpart to `.claude/settings.json` permissions — Codex has no per-command allowlist, so
it sets `sandbox_mode = "workspace-write"` + `approval_policy = "on-request"` with
`network_access = true` (the .NET/npm dev loop needs the network). Lifecycle hooks are defined in
`.codex/hooks.json` and need no feature flag. The shared session hook
(`.claude/hooks/session-start.sh` → `.codex/hooks/session-start.sh`) is capability-gated (runs only
where `apt-get` exists and .NET 10 is absent), so the one verbatim-mirrored script is correct for both
Claude Code's web sandbox and Codex cloud.

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

## Project docs

Public docs live in the repo root and `docs/`. Maintainer-only planning, raw API samples, deployment
runbooks, security findings, and implementation archives live in `private/`, which is gitignored.

| Doc                                     | What it is                                                                                             |
| --------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| `docs/README.md`                        | Public documentation map and ownership rules.                                                          |
| `docs/features.md`                      | Public product capabilities and workflows.                                                             |
| `docs/roadmap.md`                       | Public high-level status and roadmap.                                                                  |
| `CHANGELOG.md` (repo root)              | Public release notes — Keep a Changelog + SemVer; `docs-updater` maintains the `[Unreleased]` section. |
| `private/ROADMAP.md`                    | Maintainer-only detailed remaining work, blockers, and active milestones.                              |
| `private/archive.md`                    | Maintainer-only detailed completed-work log.                                                           |
| `private/PRD.md`                        | Maintainer-only full product spec.                                                                     |
| `private/azure-deployment-runbook.md`   | Maintainer-only Azure resource names, command targets, and deployment details.                         |
| `private/iracing-api-response-objects/` | Local captured iRacing API JSON field shapes — read before mapping any endpoint when available.        |

**After completing a feature/fix:** update public docs when product capabilities, setup, or contribution
workflow changes; update private planning docs when maintainer-only roadmap/archive detail changes; and
add a bullet under the `CHANGELOG.md` `[Unreleased]` section (correct
`Added`/`Changed`/`Fixed`/`Removed`/`Security` category). Releases are automatic on merges to `main`:
`.github/workflows/version.yml`
creates a standard SemVer tag/GitHub Release in `<major>.<minor>.<build>` form, where
`web/package.json` selects the major/minor release line and the build auto-increments from existing
three-part tags. For an intentional major/minor bump, set `web/package.json` to `x.y.0`; if no `vX.Y.0`
tag exists yet, build `0` is valid and is not advanced to `1`. Rolling `[Unreleased]` into a versioned
changelog section is a separate, deliberate step the [`/ship` skill](.claude/skills/ship/SKILL.md)
performs when a branch is shipped (dating it for the version that merge will mint) — not during
ordinary feature/fix work. `version.yml` and `/ship` compute that version from the same
`scripts/next-version.sh`, and a `Changelog Version` CI check
(`.github/workflows/changelog-version.yml`) fails a PR whose dated section has drifted from it. (The
`docs-updater` agent owns the full doc-update matrix.)

> **iRacing-data gating.** Live iRacing-backed features are gated so environments without service
> credentials can still present a functional app. Two feature flags gate that surface:
>
> - `iracing-live` (M1, shipped) — real creds. When off, iRacing routes render `ComingSoonPage` and nav
>   items are hidden.
> - `iracing-demo` (Alpha-gated) — reveals the same surface backed by clearly-labeled **synthetic** demo
>   data. `MemberContext` resolves demo users to `DemoData.DriverCustId`.
>
> `RequireFlag` / `visibleNav` gate on `iracing-live` **OR** `iracing-demo`. `iracing-demo` is fully
> functional **only** once a DB is seeded with `ApexRacers.Seeder --demo`.

---

## Commands

### .NET (run from repo root)

```bash
dotnet build                                          # build the solution
dotnet run --project src/ApexRacers.Api               # run the API (needs DATABASE_CONNECTION_STRING)
dotnet run --project src/ApexRacers.Ingestion         # run the ingestion worker (needs iRacing + DB env vars)
dotnet run --project src/ApexRacers.Seeder            # seed catalog + synthetic laps for 7 series (idempotent)
dotnet run --project src/ApexRacers.Seeder -- --demo  # + seed the synthetic demo cache (Plan 2)
dotnet run --project src/ApexRacers.Seeder -- --ci    # fully synthetic catalog, no response objects (CI/E2E; add --demo for the cache)
dotnet run --project src/ApexRacers.Seeder -- --verify-demo      # gate: exit 0 iff the demo surface is fully seeded (also auto-runs at the end of --demo)
dotnet run --project src/ApexRacers.Seeder -- --verify-teardown  # gate: exit 0 iff no demo rows remain (M2 purge check)

# EF Core migrations — always target Data, startup project Api
dotnet ef migrations add <Name> --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
dotnet ef database update      --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
```

Seeder's default/`--demo` modes need `private/iracing-api-response-objects/` populated first (gitignored —
see README); `--ci` mode does **not** (it fabricates a fully synthetic catalog, so CI/E2E can seed without
the captured shapes — see `CiCatalogSeeder`). All modes read `DATABASE_CONNECTION_STRING` (else fall back
to the local Docker default) and auto-apply pending migrations on start. `dotnet-ef` must be
installed globally and match EF Core (currently 10.0.9). SQL cleanup scripts live in
`src/ApexRacers.Data/Seeds/` (`truncate_seed_data.sql`, `purge_demo_data.sql`),
piped in via `Get-Content … | docker compose exec -T postgres psql -U apexracers -d apexracers`.
(The old GT3 seed scripts were deleted 2026-07 — they targeted the pre-June-2026 `LapTimeEntries`
schema; the Seeder's `--ci` mode replaces them.)

### Frontend (run from `web/`)

```bash
npm install
npm run dev          # Vite :5173, proxies /api → http://localhost:5000 (local dotnet API)
npm run dev:all      # starts dotnet API + Vite together
npm run dev:docker   # proxies /api → http://localhost:8080 (API in Docker)
npm run dev:cloud    # proxies /api to the configured cloud API host
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

### Agent config (run from repo root)

```bash
node scripts/sync-agent-configs.mjs           # regenerate the Codex tree from the Claude Code sources
node scripts/sync-agent-configs.mjs --check   # what CI runs; exits 1 on drift or orphaned files
```

Needs only Node (no install step — the script has no dependencies). Run it after editing anything under
`.claude/agents/`, `.claude/skills/`, or `.claude/hooks/`, and commit the regenerated files. See
[Agent config parity](#agent-config-parity-claude-code--codex) for what maps to what.

### Cloud Deployment

The API is deployed as a containerized web app that serves the React SPA from `wwwroot`; the ingestion
worker is deployed as a separate background container. Runtime secrets are provided through the cloud
secret store and mapped into app configuration. Secret names use hyphens in the store and are mapped to
underscore env vars by `HyphenToUnderscoreSecretManager` in both `Program.cs` files. Public docs should
describe the pattern, not exact maintainer resource names or private deployment runbooks.

---

## Architecture

### Projects

```text
Core  ← no deps          Data  ← EF Core, Npgsql (references Core)
Api / Ingestion / Seeder ← reference Core + Data (Api and Ingestion never reference each other)
Tests ← xUnit (references Api + Ingestion + Seeder + Core + Data)
```

> **Coverage note:** because Tests references Seeder, the Seeder assembly is in the coverage denominator.
> `coverage.runsettings` excludes the seeder orchestration/data (`Program`, `CiCatalogSeeder`, `CiCatalog`,
> `Demo.DemoCacheSeeder`) as I/O infrastructure; pure logic like `SyntheticLaps` and the
> `Verification.DemoSeedVerifier` stay covered and tested.

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
**Client disconnects are handled ahead of that mapping:** the pure `ClientDisconnectDetector` matches an
`OperationCanceledException` or `BadHttpRequestException` raised while `HttpContext.RequestAborted` is
signalled, and the middleware records it at Debug, sets **499** (nginx's "Client Closed Request"), and
writes no body — there is no client left to receive one. Both types are matched only alongside the
cancellation token, so a server-side timeout or a genuinely malformed request keeps its 500/400 and its
Error log. Browsers navigating away mid-request produce these constantly; without the branch they read
as server faults (an E2E run logged 15).

**Cross-cutting middleware & ops endpoints** (`Program.cs`, in pipeline order): `RequestLoggingMiddleware`
(outermost — one structured log line per request: method, path, status code, elapsed ms, client IP;
level scales with status; skips `/healthz` and `/ready`) → `ExceptionHandlingMiddleware`
→ `SecurityHeadersMiddleware` (baseline headers on every API + SPA response: nosniff, frame-deny,
referrer/permissions policy, `frame-ancestors` CSP, HSTS over HTTPS — full CSP deferred). Rate limiting: a
global per-IP safety net, configurable via `GLOBAL_RATE_LIMIT_PERMIT_PER_MINUTE` (**default 300**; CI/E2E
raises it), plus a stricter per-IP `auth` policy on `AuthController` whose limit is configurable via
`AUTH_RATE_LIMIT_PERMIT_PER_MINUTE` (**default 10**; CI/E2E raises it since the serial suite shares one
runner IP). Those are the **API** defaults — `docker-compose.yml` raises both for the local stack too
(1000 / 10000), because the documented local E2E loop drives it in parallel from one loopback IP and at
the API defaults the limiter starts returning 429 mid-run, which reads as unrelated test failures rather
than as throttling. Set either var in `.env` to exercise the limiter locally. Health probes (anonymous, rate-limit-exempt): `GET /healthz` (liveness, no
dependency checks) and `GET /ready` (DB readiness via `AddDbContextCheck`). Behind App Service, per-IP
limiting needs forwarded headers enabled in deployed reverse-proxy environments. The hosted API uses
platform telemetry for requests, dependencies, exceptions, and `ILogger` traces; `RequestLoggingMiddleware`
adds one structured per-request log line that flows into that telemetry pipeline and the console.

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
| `FeatureFlagsController`              | caller's active feature flags (**public** — anonymous callers get the enabled Standard-tier set)                                                                                          |
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
  and OR-ed. Auth-independent. Dashboard/Profile degrade gracefully; their in-page iRacing panels
  OR-gate the same way (`showIracing = liveFlag || demoFlag`), so they render under demo too.

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
  full stack at `http://localhost:8080`. The suite includes axe-core WCAG 2.1 A/AA audits across public
  and authenticated pages (zero-violation gate, `web/e2e/a11y.spec.ts`). A non-blocking per-PR CI workflow
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
