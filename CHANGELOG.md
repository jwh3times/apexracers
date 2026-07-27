# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- Migrated the frontend router from `react-router-dom` to `react-router` v8. This is a package swap plus an import rewrite across 62 files, not a version bump: React Router v8 **dropped** `react-router-dom`, which had existed only to re-export the DOM APIs during the v6→v7 upgrade, and no 8.x of it was ever published — so the advisory below could not be cleared by bumping the package the app actually depended on. Every export in use (`BrowserRouter`, `MemoryRouter`, `Routes`, `Route`, `Outlet`, `Navigate`, `Link`, `NavLink`, `useNavigate`, `useLocation`, `useParams`, `useSearchParams`) keeps its name, and the app uses no `RouterProvider`/`HydratedRouter`, so nothing needed the `react-router/dom` subpath. v8's floors were already met (React ≥ 19.2.7 against the app's 19.2.8; Node ≥ 22.22.0 against the repo-wide 26).
- `docker-compose.yml` now raises the API's per-IP rate limits for the local stack (`AUTH_RATE_LIMIT_PERMIT_PER_MINUTE` 1000, `GLOBAL_RATE_LIMIT_PERMIT_PER_MINUTE` 10000), mirroring what `.github/workflows/e2e.yml` already sets. The documented local E2E loop — `docker compose up` then `npm run test:e2e` — drives the stack in parallel from a single loopback IP, so at the production defaults (10/min and 300/min) the limiter began returning 429 partway through the run. That surfaced as unrelated-looking failures rather than as throttling: a registration would silently stay on `/login`, and four specs failed for what appeared to be routing reasons. Both remain overridable from `.env` to exercise the limiter itself.
- Removed the `shell-quote` npm `overrides` entry added in 0.4.25. `concurrently` 10.0.4 depends on the patched `shell-quote@1.9.0` directly, so the override no longer affects resolution and only obscured the real dependency graph.

### Fixed

- Client disconnects are no longer reported as server errors. A browser navigating away from a page with requests in flight aborts them, which unwinds as an exception — `ExceptionHandlingMiddleware` had no case for it, so each one was logged at Error as an unhandled exception and answered with a 500 that no client remained to receive. In a local E2E run, **all 25** unhandled exceptions in the API log were disconnects: 14 Npgsql `OperationCanceledException: Query was cancelled` (wrapping `PostgresException 57014`), 10 `OperationCanceledException: The operation was canceled.`, and one Kestrel `BadHttpRequestException: Unexpected end of request content.` The same suite now records 15 of these as 499 with zero Error-level entries.
- The new pure `ClientDisconnectDetector` matches those exception types **only** when `HttpContext.RequestAborted` is signalled, so a server-side timeout or a genuinely malformed request keeps its existing 500/400 and its Error log — the cancellation token is what separates "the connection dropped" from "we failed". `ExceptionHandlingMiddleware` records a disconnect at Debug, sets **499** (nginx's "Client Closed Request"), and writes no body; `RequestLoggingMiddleware` logs 499 at Information rather than letting it fall into the `>= 400` Warning bucket. Observability-only — no response a client can still receive changes.

### Security

- Cleared the two remaining high-severity advisories failing the `npm audit (web)` job, returning it to zero vulnerabilities:
  - `react-router` updated to 8.3.0, resolving an RSC-mode CSRF bypass that allows action execution before the 400 response (GHSA-qwww-vcr4-c8h2; affects ≥ 7.12.0, < 8.3.0). The advisory only reaches applications using the unstable RSC APIs — ApexRacers is a client-side SPA (`BrowserRouter`, no framework mode, no server actions), so it was never exposed. The upgrade clears the audit gate rather than closing a reachable hole.
  - `brace-expansion` updated 5.0.7 → 5.0.8, resolving a DoS via unbounded expansion length causing an out-of-memory crash (GHSA-mh99-v99m-4gvg). Dev-only, reaching the tree through `minimatch`. The 0.4.25 bump to 5.0.7 did not clear it: the advisory's affected range was later widened to include that version.

## [0.4.25] - 2026-07-24

### Added

- Codex parity for the repo's agent tooling, generated from the existing Claude Code sources so the two tools cannot drift. `scripts/sync-agent-configs.mjs` renders `.claude/agents/*.md` to `.codex/agents/*.toml`, and mirrors `.claude/skills/*/SKILL.md` and `.claude/hooks/*` to the paths Codex discovers (`.agents/skills/`, `.codex/hooks/`). A `tools:` list without `Write`/`Edit` becomes `sandbox_mode = "read-only"`; `model:` is dropped, since Claude model names are not Codex model names. The generator self-validates: it round-trips each generated TOML through an independent parser to catch an escaping regression, and lints the mirrored prose for `claude`→`Codex` substitution artifacts and relative links that would break at the mirrored path depth.
- An **Agent Config Sync** CI check (`.github/workflows/agent-config-sync.yml`) that re-runs the generator with `--check` and fails a PR whose generated tree has drifted from its sources or that leaves an orphaned generated file behind.
- `.codex/config.toml`, the Codex counterpart to Claude Code's `.claude/settings.json` permissions: `sandbox_mode = "workspace-write"` + `approval_policy = "on-request"` with `network_access = true` so the .NET/npm dev loop (restore, install) works while Codex still prompts before acting outside the workspace.

### Changed

- Agent and skill prose is now tool-neutral, so the generator can copy it verbatim instead of find/replacing tool names — the previous hand-made Codex files had been produced by a blind `claude`→`Codex` substitution that emitted broken paths (`.Codex/agents/*.md`), broken doc links (`code.Codex.com`), and self-referential sentences ("the canonical guide is `AGENTS.md`; `AGENTS.md` is only a bare `@AGENTS.md` import").
- The shared agent session-start hook now gates on capability (`apt-get` present and .NET 10 absent) instead of the Claude-specific `CLAUDE_CODE_REMOTE` env var, so the one script correctly bootstraps the .NET 10 SDK under both Claude Code's web sandbox and Codex cloud, and no-ops on local machines. Codex exposes no cloud/remote indicator to key off, and the capability gate needs none.

### Security

- Cleared both high-severity advisories failing the `npm audit (web)` job, which is now back to zero vulnerabilities. Both dependencies are dev-only and never shipped to users:
  - `shell-quote` forced to the patched `^1.9.0` via an npm `overrides` entry, resolving a quadratic-complexity DoS in `parse()` (GHSA-395f-4hp3-45gv). The advisory reaches the tree through `concurrently`, which exact-pins `shell-quote@1.8.4` on its 10.x line, so no bump of `concurrently` could resolve it; the override keeps `concurrently` current instead of downgrading it to 9.2.4 as `npm audit fix --force` proposes.
  - `brace-expansion` updated 5.0.6 → 5.0.7, resolving a DoS via exponential-time expansion of consecutive non-expanding `{}` groups (GHSA-3jxr-9vmj-r5cp). It reaches the tree through `eslint` → `minimatch`.

### Fixed

- Pinned the app's base sans-serif font (`--font-sans` → Inter) so the default typeface no longer inherits Tailwind's Preflight default, whose value changed in `tailwindcss` 4.3.3 and shifted rendering on every element that sets no font family of its own (notably the public landing, terms, and privacy pages).
- Pinned the base monospace font (`--font-mono`) to the generic stack it already resolved to, so — like `--font-sans` — it is app-owned and immune to future changes in Tailwind's Preflight default. No visual change; purely defensive.
- Fixed the public Terms of Service and Privacy Policy pages, whose title and body text used typography utility classes (`font-display-sm`/`text-display-sm` and `font-body-md`/`text-body-md`) that have no matching design-system tokens and therefore emitted no CSS — the text rendered with no defined size or font family. Remapped the page titles to the shared `.text-page-title` style and the body copy to the existing `body-lg` token.

## [0.4.15] - 2026-07-16

### Added

- Public documentation taxonomy, feature overview, and high-level roadmap pages under `docs/`.
- Structured per-request logging middleware (method, path, status code, elapsed time, client IP; log level scales with status), complementing the Application Insights codeless auto-instrumentation already active on the API App Service.

### Fixed

- Per-IP rate limiting now partitions by the real client IP behind the App Service front end (via the `ASPNETCORE_FORWARDEDHEADERS_ENABLED` forwarded-headers app setting), instead of collapsing all clients into one shared bucket.

### Changed

- Public and agent docs now separate public setup/product guidance from maintainer-only planning, deployment, and security runbooks.
- Main-branch release automation now creates standard SemVer `<major>.<minor>.<build>` tags and GitHub Releases, auto-incrementing the build per major/minor line while preserving intentional `x.y.0` bumps.
- Contributor workflow: added a `/ship` skill (`.claude/skills/ship/`) that refreshes docs, rolls `[Unreleased]` into a CHANGELOG section dated for the version the merge will mint, runs the fast checks (Prettier, ESLint, `npm run build`, `dotnet build`), and opens or updates the PR. The mint version now comes from a single `scripts/next-version.sh` helper that the release workflow (`version.yml`) also calls, and a new **Changelog Version** CI check (`.github/workflows/changelog-version.yml`) fails a PR whose dated CHANGELOG section has drifted from that version. This replaces the per-turn docs-freshness Stop hook, which has been removed.

## [0.3.0] - 2026-07-04

### Added

- Baseline security response headers on every API and SPA response (content-type sniffing protection, frame denial, referrer and permissions policies, HSTS over HTTPS) via a unit-tested middleware.
- Global per-IP API rate limit (300 requests/minute, fixed window) as a safety net in front of every endpoint; the stricter per-IP auth limit is unchanged.
- Health endpoints: `/healthz` (liveness) and `/ready` (database readiness), both anonymous and exempt from rate limiting.
- CI dependency-vulnerability audit workflow (npm audit + `dotnet list package --vulnerable`), running on PRs and weekly; non-blocking for now.
- `/admin` accessibility audit in the E2E suite — the panel is provisioned by promoting an in-test-registered user to Admin, then audited with axe-core (zero WCAG 2.1 A/AA violations).
- Seeder `--ci` mode that seeds a fully synthetic catalog (no captured iRacing response objects required) and auto-applies pending migrations, enabling demo-data seeding in CI.
- Accessibility (axe-core WCAG 2.1 A/AA) audits across all 18 iRacing-gated routes, rendered against synthetic demo data in CI.
- `/analytics` first-visit empty state now offers a "Compute my percentiles" action that computes and populates percentile data inline, instead of requiring a prior visit to Recommendations.
- Typed `ApiError` (carrying the HTTP status) in the frontend API client, and a guided "search unavailable" hint on `/compare` that distinguishes a 503 (search backend unavailable) from "no drivers matched" — with demo mode naming the searchable sample drivers.
- Seeder `--verify-demo` / `--verify-teardown` gates — mechanical exit-code checks that the demo surface is fully seeded (a prod `iracing-demo` rollout precondition) or fully torn down (the M2 purge check); `--demo` now self-verifies at the end.
- E2E functional specs — logout/session-protection and password-reset (via the Development token echo) auth flows, and `.ibt` telemetry upload → My Laps — plus a feature-flag gating spec that restores the ComingSoonPage axe audit (asserting gated routes render synthetic demo content when the flag is on, and ComingSoon when off).
- Anonymous/guest feature-flag read: `GET /api/feature-flags` is now public and returns the enabled Standard-tier flag set to signed-out visitors (a GA prerequisite so flag-gated public pages render for guests once `iracing-live` is enabled); the frontend flag provider fetches under a `guest` owner.
- A `--color-gold` design token (Tailwind `text-gold`/`bg-gold`/`border-gold`/`shadow-gold`) replacing hardcoded `#FFD700` across the analytics/profile/settings UI.
- CI-only Playwright visual-regression suite for the stable public pages (`/`, `/login`, `/terms`, `/privacy`) with committed Linux/Chromium screenshot baselines, refreshable via an `e2e.yml` `workflow_dispatch` input.

### Fixed

- Extended light/dark WCAG 2.1 AA link-distinction — a persistent `underline` on inline accent links —
  across Profile, Progression, Analytics, Recommendations, Races, Percentile, and Compare (WCAG 1.4.1);
  extended full-strength muted-text contrast (`on-surface-variant`) to Admin, Series, and Percentile
  (WCAG 1.4.3); and added `/reset-password` and `/verify-email` to the axe audit set.
- Corrected the PR-template coverage checklist figure (80% → 85%) and two stale demo-gating code comments
  (Dashboard/Profile fetch guards reference the live-OR-demo flag check they actually use).
- Admin panel role and minimum-role dropdowns now have accessible names (WCAG 2.1 select-name); the /admin E2E axe audit enforces this.
- Accessibility on iRacing-gated pages — replaced hardcoded red iRating/SR deltas with the semantic error token (darkened for light-mode AA contrast) on Progression, Races, Race Detail, and Compare, and removed a low-contrast opacity on the Strategy weather line.
- `/live` race board no longer shows misleading absolute start times for perpetually-live (sentinel/stale) sessions — a session "live" for over 24 hours renders `—` instead of a bogus start time.

### Changed

- Pinned the local pgAdmin image to a specific version tag (was `latest`) so Dependabot can track it.
- The per-IP auth rate limit is now configurable via `AUTH_RATE_LIMIT_PERMIT_PER_MINUTE` (default 10, unchanged in production).
- The global per-IP rate limit is now configurable via `GLOBAL_RATE_LIMIT_PERMIT_PER_MINUTE` (default 300, unchanged in production).

### Removed

- Retired `docs/IMPLEMENTATION_PLAN.md` — a committed roadmap snapshot now reconciled into the
  maintainer's local planning docs.
- Deleted the stale GT3 SQL seed scripts (`seed_gt3_series.sql`, `remove_gt3_seed.sql`) — they targeted
  the pre-June-2026 `LapTimeEntries` schema and no longer run; the Seeder's `--ci` mode replaces them.
- Removed the dead, unused `.tier-badge-gold` / `.tier-badge-green` CSS utility rules (zero usages).

## [0.2.0] - 2026-06-30

### Added

- Axe-core (`@axe-core/playwright`) accessibility audits in the Playwright E2E suite — asserts zero
  WCAG 2.1 A/AA violations across the public + authenticated page set (5 public + 7 authed pages);
  runs in the existing non-blocking E2E workflow.

### Fixed

- Light-mode color contrast now meets WCAG 2.1 AA — darkened the cyan accent tokens
  (`primary-container`, `primary-fixed-dim`, `primary`, `secondary-fixed-dim`, and companion fill/ink
  tokens) in the `html.theme-light` / `prefers-color-scheme: light` overrides in `index.css`; dropped
  the low-opacity modifier on hero stat captions and footer text; and added a persistent underline to
  inline links on the Privacy, Dashboard, My Laps, and Profile pages. Dark-mode accent colors are unchanged.

## [0.1.0] - 2026-06-30

The first feature release since the initial production deployment. It lands the
full iRacing member/race/competition feature set, the launch-gating and demo-data
preview system, account-security and transactional-email improvements, and a
substantial testing and tooling uplift.

> **Note:** every iRacing-data-backed feature ships behind the seeded-disabled
> `iracing-live` feature flag and is non-functional in production until iRacing
> service-account OAuth credentials are available. See the project roadmap.

### Added

#### iRacing member, race & profile insights

- **Progression** — per-category iRating, Safety Rating, CPI, and Time Trial
  ratings, with iRating history.
- **Driver profile** — enriched profile with identity, per-category license
  badges (with safety rating), career stats, a this-year summary, and recap
  favorites.
- **Race history** — recent official races, with car names resolved from the
  local catalog.
- **Race detail** — the full classified field for a subsession (public), plus an
  authenticated per-lap pace trace.
- **Achievements** — an awards/trophy case on the profile.

#### Schedule, records & competition

- **Season schedule** — active-season schedule with track, weather, and
  Balance-of-Performance per week, plus a personal-best overlay.
- **World records & leaderboards** — fastest car+track lap overlays and a global
  top-200 leaderboard by license category.
- **Standings** — championship, time-trial, and qualifying standings per car
  class (qualifying lap times parsed directly from result chunk files).
- **Race Now guide** — a board of official sessions starting in the next few
  hours.

#### Head-to-head, catalog & strategy

- **Compare & rivals** — driver-vs-driver head-to-head, plus following rivals
  (add/remove/search/suggestions).
- **Catalog explorer** — a browsable car and track catalog with detail pages and
  a "your best laps" overlay.
- **Strategy briefing** — per-week track/pit, weather risk, and per-car BoP +
  shift analysis (public; personalizes when signed in).

#### Accounts & security

- **Password management** — authenticated password change, plus public,
  enumeration-safe forgot/reset password with email-delivered links.
- **Verified email change** — a request → emailed confirmation link → confirm
  flow, with a security notice sent to the old address; active sessions are
  revoked on change.
- **Transactional email** — delivery via Azure Communication Services, with a
  logging fallback when unconfigured.
- **Refresh-token cap** — active refresh tokens are capped per user, revoking the
  oldest past the cap.

#### Launch gating & demo data

- **iRacing-live feature flag** — gates the entire iRacing-dependent surface;
  gated routes render a "Coming Soon" page and their nav items hide until the flag
  is enabled (no redeploy required to flip it).
- **iRacing demo preview** — an `iracing-demo` flag plus a synthetic-data seeder
  (`--demo`) that lets the full product be previewed without live iRacing
  credentials.

#### Platform

- **On-demand iRacing data layer** — a cached client over the iRacing API
  (`CachedIRacingClient` + `ExternalDataCache`) memoizing mapped DTOs with
  per-call TTLs, plus background cleanup of long-expired rows.
- **RFC-7807 error contract** — unhandled exceptions are returned as
  `application/problem+json` with a consistent status mapping.
- **Typed "iRacing not linked" response** — iRacing-linked endpoints return a
  typed `409` when the caller has not linked a customer ID.

#### UI & dashboard

- A support page, top-nav breadcrumbs, a dashboard KPI row, a notifications bell
  with client-derived alerts, a collapsible sidebar/icon rail, and a "Your pct"
  column on week detail.

#### Testing & tooling

- **Playwright E2E** — a harness with a register→dashboard smoke test and a
  non-blocking per-PR E2E CI workflow.

### Changed

- Test coverage gates raised from 80% to 85% (backend line and branch; frontend
  statements, branches, functions, and lines).
- Users are now restricted to a single role at a time (highest tier wins;
  enforced by a database unique index).
- Profile updates no longer change email directly — email changes go through the
  verified email-change flow.
- Backend tests now run against in-memory SQLite (a real relational provider) to
  validate SQL translatability.
- The frontend moved to the repository root and adopted a feature-based
  structure, with tests colocated alongside their modules.
- Numerous dependency updates across npm, NuGet, and GitHub Actions (grouped
  Dependabot).

### Security

- Local development stack ports are bound to `127.0.0.1` instead of all
  interfaces.
- Password reset revokes all of a user's active refresh tokens; reset and
  verification tokens are never logged.

## [0.0.1] - 2026-06-16

Initial release — the version currently deployed to production
(<https://apexracers.gg>).

### Added

#### Platform

- Lap time percentile tracking and car recommendations for iRacing weekly series.
- ASP.NET Core Web API with use-case-oriented controllers backed by focused
  service classes.
- PostgreSQL persistence via EF Core, with the full iRacing catalog modeled
  (series, seasons, weeks, tracks, cars, car classes, subsessions, and results).

#### Authentication & accounts

- User registration and login issuing JWT access tokens and rotating refresh
  tokens.
- Refresh token rotation, logout/revocation, profile updates, and theme
  preference persistence.
- iRacing OAuth 2.0 callback handling.
- Role-based access control with an `AdminOnly` policy.

#### Features

- **Series** — browse active weekly series.
- **Week detail** — cars and aggregate lap stats for a series week.
- **Percentile** — a driver's lap time percentile for a specific car and week,
  computed and cached.
- **Recommendations** — ranked car recommendations for the authenticated user.
- **Analytics** — per-car percentile history and stats.
- **Telemetry** — iRacing `.ibt` file upload with lap extraction, plus personal
  best laps per track and car.
- **Admin** — user role management and feature flag CRUD.
- **Feature flags** — per-user flag evaluation based on role.

#### Data ingestion & seeding

- Standalone ingestion background worker that pulls data from the iRacing API
  via `Aydsko.iRacingData`.
- Idempotent CLI seeder that loads catalog data and generates synthetic lap time
  data across all series for a usable UI without live data.

#### Frontend

- Vite + React + TypeScript single-page app with a typed API client.
- Public marketing landing page, login, terms, and privacy pages.
- Authenticated app shell (sidebar + top nav + footer) with dashboard, series,
  week detail, percentile, analytics, recommendations, my laps, telemetry,
  profile, settings, and admin pages.
- Fluid design system that scales with viewport width, plus light/dark/auto
  theming.

#### Infrastructure & tooling

- Docker Compose stack (PostgreSQL, pgAdmin, API) for local development.
- Azure deployment: API on App Service, ingestion worker as a Container App,
  with Azure Container Registry, Key Vault, and PostgreSQL Flexible Server.
- GitHub Actions CI/CD with enforced 80% test coverage gates (line and branch
  for the backend; statements, branches, functions, and lines for the frontend),
  Prettier formatting checks, and Dependabot dependency updates.

#### Project documentation

- README, contribution guidelines, code of conduct, support guide, and security
  policy.
- Licensed under the GNU Affero General Public License v3.0.

[Unreleased]: https://github.com/jwh3times/apexracers/compare/v0.4.25...HEAD
[0.4.25]: https://github.com/jwh3times/apexracers/compare/v0.4.24...v0.4.25
[0.4.15]: https://github.com/jwh3times/apexracers/compare/v0.4.14...v0.4.15
[0.3.0]: https://github.com/jwh3times/apexracers/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/jwh3times/apexracers/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/jwh3times/apexracers/compare/v0.0.1...v0.1.0
[0.0.1]: https://github.com/jwh3times/apexracers/releases/tag/v0.0.1
