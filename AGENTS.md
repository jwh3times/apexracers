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

Docs and release metadata are prepared at **ship time** by the
[`/ship` skill](.agents/skills/ship/SKILL.md): before opening a PR it evaluates the cumulative SemVer
impact, invokes `docs-updater` scoped to the branch diff, then rolls `[Unreleased]` into a CHANGELOG
section dated for the version the merge will mint. Run `/ship` (or say "ship it") when a branch is
ready for review.

## Agent config parity (Claude Code ↔ Codex)

Both tools run the same agents, skills, and session hooks from **one** set of sources per row below.
Agents and hooks are authored for Claude Code, with Codex generated from them; **skills run the
opposite direction** — authored under `.agents/skills/`, with the Claude Code tree generated from
that:

| Source (edit this)               | Generated (never edit)           |
| --------------------------------- | ---------------------------------- |
| `.claude/agents/<name>.md`       | `.codex/agents/<name>.toml`      |
| `.agents/skills/<name>/**`       | `.claude/skills/<name>/**`       |
| `.claude/hooks/<file>`           | `.codex/hooks/<file>`            |

Skills are authored under `.agents/skills/` — not `.claude/skills/` — because that is where
third-party skill installers write; keeping the install target as the authored source means
installing or updating a skill stays a one-way drop-in with no manual copying afterward. The whole
skill directory is mirrored, not just `SKILL.md` — references, `scripts/*.sh`, `agents/*.yaml`, and
any other files a skill carries are all drift-controlled. Each generated `SKILL.md` gets a
`# GENERATED — DO NOT EDIT` banner injected as a YAML comment on line 2 (line 1 stays `---`, so the
frontmatter still parses); every other file in the tree is copied byte-for-byte with no banner and no
text transformation.

**Never replace the generated `.claude/skills/` tree with a symlink back to `.agents/skills/`** (or
any generated path with a symlink to its source) — two independent failure modes rule that out, both
hit for real while building this generator:

1. `readdirSync(dir, { withFileTypes: true })` reports a symlink as `isSymbolicLink()`, not
   `isDirectory()`. A directory-walking generator like this one would see zero skill sources through
   the link and, on the next run, delete every real generated file underneath it as "orphaned."
2. Git may not even be able to store the link: when `git config core.symlinks` is `false` (common on
   a Windows checkout), `git add` walks through the symlink and stages the **target file's contents**
   under the link's path instead of recording a link — duplicating every file rather than linking it.
   `git add -n .claude/skills/<name>` listing individual files (instead of one link entry) confirms
   this is happening. Worse, if the generated directory is left as a stale symlink into the source
   tree while the generator still runs against it, every "generated" write actually lands back on the
   authored source file, silently corrupting it.

Regenerate with `node scripts/sync-agent-configs.mjs` (or `npm run sync:agents`; add `-- --check` /
`--check` to verify without writing) and commit every side that changed; the **Agent Config Sync** CI
check (`.github/workflows/agent-config-sync.yml`) fails a PR whose generated tree has drifted or that
leaves an orphaned generated file behind. The generator copies prose **verbatim** — it never rewrites
wording — so keep agent and skill bodies **tool-neutral**: don't name one tool's entry-point file
where "the project guide" will do, and write repo-root-relative paths as plain text rather than
relative Markdown links (a relative link resolves differently from the mirrored location).

Frontmatter maps as follows: `name`/`description` carry over; a `tools:` list with no `Write`/`Edit`
becomes `sandbox_mode = "read-only"`; `model:` is **dropped** (Claude model names are not Codex model
names, so Codex uses its session default). The generator also self-checks: it round-trips every
generated TOML through an independent parser (catching an escaping regression before it ships) and
lints the mirrored prose for substitution artifacts and depth-fragile relative links.

No formatter currently runs over `.agents/skills/` or `.claude/agents/` (Prettier in this repo is
scoped to the `web/` tree only — see Commands below), so there is no format-before-sync ordering
requirement today. If a formatter is ever pointed at those trees, run it before
`sync-agent-configs.mjs`, not after: regenerating first would mirror unformatted content and drift
again on the next format pass. In that case also point the formatter's ignore file at the generated
tree (`.claude/skills/`), not the authored one.

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
three-part tags. At ship time, `/ship` evaluates the cumulative release impact: a major or minor
decision advances both `web/package.json` and `web/package-lock.json` to the new `x.y.0` line, while a
build decision leaves them unchanged. If no `vX.Y.0` tag exists yet, build `0` is valid and is not
advanced to `1`. Rolling `[Unreleased]` into a versioned changelog section is a separate, deliberate
step the [`/ship` skill](.claude/skills/ship/SKILL.md) performs when a branch is shipped (dating it for
the version that merge will mint) — not during ordinary feature/fix work. After selecting the release
line, `version.yml` and `/ship` compute the exact target from the same `scripts/next-version.sh`, and a
`Changelog Version` CI check
(`.github/workflows/changelog-version.yml`) fails a PR whose dated section has drifted from it. (The
`docs-updater` agent owns the full doc-update matrix.)

> **iRacing-data gating.** Live iRacing-backed features are gated so environments without service
> credentials can still present a functional app. Two feature flags gate that surface:
>
> - `iracing-live` (M1, shipped) — real creds. When off, iRacing routes render `ComingSoonPage` and nav
>   items are hidden.
> - `iracing-demo` (Alpha-gated) — reveals the same surface backed by clearly-labeled **synthetic** demo
>   data. `MemberContext` resolves the caller's Subject Driver to the Demo Driver
>   (`DemoData.DriverCustId`) for any User on the demo surface.
>
> `useIracingSurface` owns the `iracing-live` **OR** `iracing-demo` decision used by `RequireFlag`,
> `visibleNav`, and in-page panels. `RequireFlag` renders nothing until the current flag owner is
> ready, then chooses the child or `ComingSoonPage`; `iracing-demo` is fully functional **only** once
> a DB is seeded with `ApexRacers.Seeder --demo`.

---

## Commands

### .NET (run from repo root)

```bash
dotnet build                                          # build the solution
dotnet test                                           # run the full xUnit suite (Docker engine required for PostgreSQL integration tests)
dotnet test --filter-class ApexRacers.Tests.Models.FieldPercentileTests  # run one test class
dotnet test src/ApexRacers.Tests/ApexRacers.Tests.csproj \
  --configuration Release \
  --coverage --coverage-output coverage.cobertura.xml --coverage-output-format cobertura \
  --coverage-settings coverage.runsettings \
  --report-xunit-trx --report-xunit-trx-filename backend-tests.trx \
  --results-directory ./TestResults                 # coverage + stable CI-style artifacts
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
npm run lint         # Oxlint with TypeScript 7-powered type-aware rules
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
node scripts/sync-agent-configs.mjs           # regenerate the generated trees from the authored sources
node scripts/sync-agent-configs.mjs --check   # what CI runs; exits 1 on drift or orphaned files
npm run sync:agents                           # same as the plain command above
npm run sync:agents -- --check                # same, check mode
```

Needs only Node (no install step — the script has no dependencies; the root `package.json` exists only
to expose these two scripts). Run it after editing anything under `.claude/agents/`, `.agents/skills/`,
or `.claude/hooks/`, and commit every side that changed. See
[Agent config parity](#agent-config-parity-claude-code--codex) for what maps to what — note skills run
the opposite direction from agents/hooks.

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
Tests ← xUnit on Microsoft Testing Platform v2 (references Api + Ingestion + Seeder + Core + Data)
```

The test executable uses `xunit.v3.mtp-v2` on Microsoft Testing Platform v2, selected repo-wide by
`global.json`. Use the current Visual Studio MTP Test Explorer or the current VS Code C# Dev Kit for
IDE discovery/debugging; older VSTest-only environments are unsupported. The coverage command above
writes `TestResults/backend-tests.trx` and `TestResults/coverage.cobertura.xml`.

> **Coverage note:** because Tests references Ingestion and Seeder, both assemblies are in the coverage
> denominator. Microsoft code coverage reads `coverage.runsettings`, which excludes the ingestion
> `Worker` / `Program` I/O shells while their
> decision, mapping, and persistence modules stay covered; it also excludes seeder orchestration/data
> (`Program`, `CiCatalogSeeder`, `CiCatalog`, `Demo.DemoCacheSeeder`) while pure logic like
> `SyntheticLaps` and `Verification.DemoSeedVerifier` stays covered and tested.

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

**Development API docs:** `Microsoft.AspNetCore.OpenApi` generates the `v1` document at
`/openapi/v1.json`, with title `ApexRacers API` and version `v1`; `Scalar.AspNetCore` serves its
interactive reference at `/scalar/v1`. Both endpoints are mapped only inside
`app.Environment.IsDevelopment()` and are exempt from rate limiting. The API project's documentation
stack is `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore`; package versions remain centrally
managed in `Directory.Packages.props`.

**Error handling:** `ExceptionHandlingMiddleware` (registered first) converts unhandled exceptions into
RFC-7807 `application/problem+json`, status from the pure `ExceptionStatusMapper`
(`ArgumentException`/`InvalidOperationException` → 400, `KeyNotFoundException` → 404,
`UnauthorizedAccessException` → 401, `IRacingNotLinkedException` → 409,
`IRacingNotConfiguredException` → 503, else 500 with its message hidden). The not-linked exception is
the deliberate format exception: middleware preserves the established exact JSON
`{ code: "IRACING_NOT_LINKED", message: "…" }` instead of ProblemDetails. Services should just `throw`;
don't catch to `BadRequest(string)`. Controllers still return explicit results for non-exception
outcomes needing a specific code (e.g. AuthController's 423 lockout).
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
Field-percentile rank and field median are one such extraction, shared across services and the Seeder
as `ApexRacers.Core.FieldPercentile` — not a per-service formula.

- `SeriesService`, `WeekCarStatsService` — series list (one card per series, on its Current Season); per-car week lap stats (median via `Core.FieldPercentile`). Current-season/current-week lookups across these two plus `ScheduleService`, `StrategyService`, `StandingsService`, `PercentileCalculationService`, and `CarRecommendationService` go through `SeasonQueries` (`src/ApexRacers.Api/Services/SeasonQueries.cs`) instead of a hand-written predicate. "Which season is current" is `Core.SeasonCalendar.CurrentSeasonId` — the season whose **first race week began most recently**, holding the slot through the inter-season gap until a later season's first race week start date arrives; iRacing flags the incoming season active before racing starts, so `Active` selects which _series_ appear, never which season backs them. A series with no season that has begun falls back to the newest active one. "Which week is the season in" is `Core.SeasonCalendar.CurrentWeekNumber` (start date first, week number to break a tie), with the pre-season fallback left to the caller. Both zero-based race week indexes stay zero-based end-to-end; see `CONTEXT.md` for the Race Week Index / Race Week Number distinction the frontend renders.
- `PercentileCalculationService` — compute + cache percentile (rank + median via `Core.FieldPercentile`); overlays world-record via `WorldRecordService`.
- `CarRecommendationService` — ranked recommendations from personal percentile data (rank via `Core.FieldPercentile`).
- `StrategyService` (+ pure `StrategyAnalysis`) — week briefing from BoP + weather + track/pit; personal overlay.
- `UserAnalyticsService` — per-car percentile history/stats (median via `Core.FieldPercentile`).
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
- `PersonalBestQuery` — shared per-car-and-track personal-best projection (fastest or most-recent
  order), used by `PersonalLapService` and the catalog services' PB overlays instead of each holding
  its own copy. See `dotnet-api` for the two invariants it enforces.
- `ExternalDataCacheCleanupService` (+ pure `PurgeExpiredAsync`) — purges long-expired non-demo cache rows every 6 h.
- `AuthService` — registration, login, JWT issuance, profile/password/email-change, and reset; delegates the refresh-token lifecycle to `RefreshTokenStore`. Needs `AddDefaultTokenProviders()`. The JWT contract (signing key, issuer, audience) is bound once as `JwtSettings` (`Program.cs`) and injected into both the issuing side (`AuthService`) and the validating side (`TokenValidationParameters`) — see `dotnet-api` for the rule.
- `RefreshTokenStore` — owns issue/rotate/revoke/revoke-all/retention cleanup over an injected `TimeProvider`. Its canonical active predicate is `RevokedAt == null && ExpiresAt > now`; issuance caps active tokens at 5 per user, and rotation revokes + inserts in one save. Raw tokens leave only as return values; persistence stores their SHA-256 hashes.
- `IEmailSender` / `AcsEmailSender` / `LoggingEmailSender` (+ pure `AccountEmailTemplates`) — transactional email over the `OutboundEmail` DTO; binds ACS when configured, else logs subject only (links/tokens never logged). Links built from `APP_BASE_URL`.
- `TelemetryUploadService`, `PersonalLapService` — parse `.ibt` → `PersonalLap`; query personal bests.
- `AdminService` — role + flag CRUD; delegates active-flag resolution to `FeatureFlagEligibility`.
  Users are **single-role** (`Standard` < `Beta` < `Alpha` < `Admin`).
- `FeatureFlagEligibility` — single owner of the role hierarchy and active-flag eligibility
  (`MinimumRole` level ≤ user level), shared by `AdminService` and `MemberContext`. Unknown or role-less
  users receive Standard eligibility; an unknown `MinimumRole` fails closed.
- `CachedIRacingClient` — get-or-fetch over `IDataClient`; throws `IRacingNotConfiguredException` when creds absent.
- `MemberContext` — resolves the caller's Subject Driver, i.e. their Claimed Identity's Customer ID:
  optional callers receive null when the caller has no Claimed Identity; required callers use
  `GetRequiredCustIdAsync` / `RequireCustId`, which throw the typed `IRacingNotLinkedException` mapped
  to the exact `409` contract above. The **only** demo-aware branch: under an eligible `iracing-demo`
  flag it resolves the Subject Driver to the Demo Driver (`DemoData.DriverCustId` = 100001) instead.
  `CONTEXT.md`'s Identity section defines Subject Driver / Claimed Identity / Demo Driver.

### Core models (`src/ApexRacers.Core/Models/`)

Domain entities below — see the `postgres-specialist` agent for full schema (PK types, schemas,
indexes, FK/`OnDelete` behavior).

| Model                                           | Purpose                                                                                        |
| ----------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| `ApplicationUser`                               | extends `IdentityUser<Guid>` — adds `DisplayName`, `IRacingCustomerId` (nullable; the user's Claimed Identity — see `docs/adr/0001-drivers-referenced-by-customer-id.md`), `ThemePreference` |
| `Series` / `Season` / `Week`                    | series → season → race week (`Week.Id` is a Guid; carries weather summary JSON as an owned `WeatherForecastSnapshot`) |
| `Track` / `Car` / `CarClass` / `CarClassCar`    | iRacing catalog + car-class membership                                                         |
| `SeasonCar` / `SeasonCarClass` / `SeasonCarBop` | per-season cars/classes; per-week BoP (composite PK)                                           |
| `Subsession` / `SubsessionResult`               | one race session + per-driver result (+ race context; owned weather/track-state snapshot JSON) |
| `WeatherSnapshot` / `WeatherForecastSnapshot` / `TrackStateSnapshot` | SDK-independent persisted JSON contracts with pinned wire names |
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
car/track catalog into `Car`/`Track` via the pure, tested `CatalogIngest` helper. The worker fetches and
coordinates only: `SeasonIngest` owns season/schedule relational upserts, `SubsessionMapper` owns the
subsession/result field mapping, and `WeatherIngest` / `TrackStateIngest` map nested SDK blocks to owned
Core snapshots. (See `dotnet-api`.)

### Data source strategy — persist vs cache (read before adding an iRacing-backed feature)

Three ways iRacing data reaches a read path. Pick deliberately:

1. **Persist into typed entities** (worker/seeder → Postgres): `Series`, `Season`, `Week`, `Track`,
   `Car`, `Subsession`, `SubsessionResult`, `SeasonCarBop`, … Read paths query with SQL/joins. Pure
   SDK→entity mapping goes in a tested helper (mirror `CatalogIngest` / `SubsessionMapper`); relational
   upsert rules belong in a directly tested persistence module (mirror `SeasonIngest`). This includes
   JSON columns nested inside a persisted row: `Subsession.WeatherJson`, `Week.WeatherSummaryJson`, and
   `Subsession.TrackStateJson` serialize the owned `WeatherSnapshot`, `WeatherForecastSnapshot`, and
   `TrackStateSnapshot` (`ApexRacers.Core.Models`), mapped at ingest by the pure, tested `WeatherIngest`
   / `TrackStateIngest` seams (`ApexRacers.Ingestion`).
2. **On-demand cache** (`CachedIRacingClient` → `ExternalDataCache`): fetch live per request, memoize
   **mapped DTOs** as JSON with a per-call TTL. Backs progression, profile, race history, lap data,
   world records, leaderboards, standings, race guide, driver search.
3. **Persisted user-owned data** (not from iRacing): `PersonalLap`, `Rival`, `CarPercentileResult`, Identity.

Choose **persist (#1)** if you need to query/filter/aggregate/join it in SQL, it's canonical/shared, or
you need point-in-time history. Choose **cache (#2)** for read-mostly, staleness-tolerant, per-user/-query
stat lookups displayed roughly as-is (the common member/season-stat case). Don't keep a second cached
copy of something that already has an entity (e.g. the car/track catalog).

**Cache and persisted-JSON rules (#1 and #2):** cache/persist mapped DTOs or owned Core types,
**never** raw Aydsko SDK types (their wire shape drifts and carries `[Obsolete]` fields) — for a
persisted column this matters more than for the cache, since a cache row expires but a persisted row
does not: an SDK rename doesn't just break a fresh fetch, it silently deserializes a historical row
into a default-valued (zeroed) object with no exception.

Every cache key and its TTL is authored once, as a `CacheSpec` factory on
`IRacingCacheKeys` (`src/ApexRacers.Api/Services/IRacingCacheKeys.cs`) — that module is the single
source of truth for key format and freshness window, not this prose; a new cache-backed read path adds
a factory there rather than interpolating a key at the call site. Eviction is TTL-only (lazy);
`ExternalDataCacheCleanupService` purges long-expired rows below the inclusive demo sentinel range and
explicitly preserves that range even if the cleanup cutoff reaches it.

**Demo cache seeding** (`ApexRacers.Seeder --demo` → `DemoCacheSeeder`): seeds `ExternalDataCache` rows
with synthetic mapped DTOs under each service's **exact** runtime cache keys — enforced by both trees
calling the same `IRacingCacheKeys` factories rather than by hand-matching interpolated strings — with
a far-future `ExpiresAt` sentinel so cleanup never evicts them. `Core.DemoData` owns both the inclusive
range threshold (`CacheSentinelThreshold`, `9000-01-01T00:00:00Z`) and the value writers use
(`CacheSentinel`, `9999-01-01T00:00:00Z`); `purge_demo_data.sql` is the explicit UTC SQL mirror of the
threshold and `>=` operator. The seeder also seeds synthetic
`SeasonCarBop`, `Week.WeatherSummaryJson`, the percentile world-record overlay, lap traces, and curated `/compare`
driver-search terms. The Seeder references `ApexRacers.Api` to reuse the real cached DTO types, so seeded
JSON matches what live services write. **Demo caveats** (not page-breakers): `/analytics` populates lazily
after a Recommendations/percentile visit; the race-guide board shows static "in-progress" sessions;
`/compare` search only hits a curated term set (arbitrary terms 503 — use the suggestions list instead);
the percentile page shows its manual customer-ID form rather than resolving the Demo Driver
automatically. That last one is **known and accepted**, not a bug to fix: `MemberContext` is the only
demo-aware resolver, and `PercentileController` deliberately takes a caller-supplied `customerId` so
the page can look up *any* Driver. A demo User has no Claimed Identity (no real `IRacingCustomerId`),
so the `iracing_id` JWT claim the page reads first is absent and it falls through to the form. Enter
`100001` (`DemoData.DriverCustId`) to see the Demo Driver's percentiles.

### Frontend (`web/`)

The typed API client is `web/src/services/api.ts`, built on the HTTP core in
`web/src/services/http.ts`'s `createHttpClient(...).request<T>(path, init)` (attaches auth headers,
retries once after silent refresh on 401, maps RFC-7807 errors, throws typed `IRacingNotLinkedError`
on the `409`). Add endpoints by calling `request` with `{ method, json }` or `{ method, body }`, plus
an optional `AbortSignal` for cancellable reads — don't reintroduce per-verb helpers. `api.ts` response
types must stay in sync with `ResponseDtos.cs`.
Full frontend patterns (auth/`AuthContext`, 401 interceptor, feature flags, **design-token system +
card pattern, read-only page resources**, Vitest rules) are in the `react-frontend` agent — don't
duplicate the detailed contracts here.

**Design system:** all sizing is fluid via `clamp()` utility classes in `web/src/index.css` (not
Tailwind breakpoints); use those classes for new UI. Primary accent is **cyan** —
`text/bg/border-primary-container` for all accent use; never hardcode the old greens (`#00FF88`,
`#00e479`, green RGBA glows). `primary-fixed-dim` is the allowed dim-accent token. (Class catalog +
shared `card-shadow`/`scan-texture` pattern: `react-frontend` agent.)

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
- **`RequireFlag`** (wraps iRacing-dependent routes): renders nothing while feature flags for the
  current guest/user/role owner are unresolved, then renders `ComingSoonPage` when both
  `iracing-live` and `iracing-demo` are off or the child when either is on. Auth-independent.
  `useIracingSurface()` owns that union for the guard, navigation, alerts, Dashboard, and Profile.

#### Components, contexts, utilities

- **Components** (`web/src/components/`): `Sidebar` / `TopNav` (nav filtered by the `iracing-live`
  OR `iracing-demo` flag via shared `visibleNav`), `NotificationsBell` (client-derived alerts via pure
  `deriveAlerts`), `DemoBanner` (shows while `iracing-demo` on), `Footer`, and the SVG charts
  `Sparkline` / `PercentileBadge` / `LapTraceChart` / `IRatingCompareChart` (each returns `null` below
  its minimum data points — guard the wrapper).
- **Contexts** (`web/src/context/`): `AuthContext` (signed-in user, login/logout, `alertsEnabled` — a
  thin React binding over the session module, which owns the token pair, claims, persistence, and
  silent refresh), `ThemeContext` (auto/light/dark, persists via `PUT /api/auth/theme`),
  `FeatureFlagContext` (`useFeatureFlags()` exposes `isEnabled` + owner-specific `ready`;
  `useFeatureFlag(key)` handles one key; `useIracingSurface()` owns the live/demo union).
- **Utilities** (`web/src/utils/`): import the shared `formatLapTime`, `toTopPercent` /
  `topPercentLabel`, `deriveAlerts`, `breadcrumbs`, `raceWeekNumber` / `raceWeekLabel` — **don't**
  re-inline these in pages. `raceWeekNumber(index)` / `raceWeekLabel(index)` convert the zero-based
  Race Week Index the API/router carry into the one-based Race Week Number drivers read (`CONTEXT.md`
  is the canonical definition of both terms); convert only at the display boundary and keep passing
  the original index to `api.ts` calls and route params.

---

## Testing & coverage

Both stacks enforce **85%** coverage; changes aren't done until it passes. The `dotnet-api` and
`react-frontend` agents carry the per-stack test rules — the load-bearing facts:

- **Frontend (Vitest):** thresholds (statements/branches/functions/lines) in `vite.config.ts`; CI also
  runs `npx prettier --check .` from `web/` (unformatted files block deploy). Run:
  `cd web && npx vitest run --coverage`.
- **Backend (.NET, xUnit in `src/ApexRacers.Tests/`):** 85% **line and branch** (CI gates both —
  `irongut/CodeCoverageSummary` for line, a `branch-rate` step for branch). Test services + `Core`
  helpers directly; controllers are excluded. Use the native Microsoft Testing Platform test,
  class-filter, and Cobertura commands under [.NET](#net-run-from-repo-root); inspect the report with
  `reportgenerator` when needed.
- **E2E + accessibility (Playwright):** tests live in `web/e2e/`; run with `npm run test:e2e` against the
  full stack at `http://localhost:8080`. The suite includes axe-core WCAG 2.1 A/AA audits across public
  and authenticated pages (zero-violation gate, `web/e2e/a11y.spec.ts`). A non-blocking per-PR CI workflow
  (`.github/workflows/e2e.yml`) runs the suite. E2E tests are excluded from Vitest coverage. Full detail
  in the `react-frontend` agent.
- **Test DB providers:** `Helpers/DbContextFactory.Create()` gives the fast tests a fresh **in-memory
  SQLite** database. It validates relational SQL translation and column constraints; `Foreign Keys=False`
  deliberately permits minimal partial fixtures. SQLite does **not** validate production foreign keys,
  schemas, max-length enforcement, or migration fidelity. Tests that depend on Npgsql translation,
  PostgreSQL transactions, or production relational constraints join `PostgreSqlCollection` and use
  `PostgreSqlFixture`: one shared pinned `postgres:18.0-alpine` container, a unique database per test,
  and `EnsureCreated` against the current EF model. That mandatory collection covers the production
  `DateTimeOffset` queries and refresh-token/auth persistence invariants, so a running Docker engine is
  required for the full `dotnet test` / coverage commands. **Order/project by entity columns before
  constructing a DTO** — ordering by a positional-record DTO property doesn't translate on Npgsql or
  SQLite.

---

## Agent skills

### Issue tracker

Issues live in GitHub Issues (jwh3times/apexracers), via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default label vocabulary (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context — one `CONTEXT.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.

---

## General principles

- Prefer clarity over cleverness.
- Introduce patterns when complexity demands them, not preemptively.
- Each class has one clear responsibility.
- `// TODO:` stubs are fine during scaffolding — always describe what needs implementing.
