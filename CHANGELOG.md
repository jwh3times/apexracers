# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Axe-core (`@axe-core/playwright`) accessibility audits in the Playwright E2E suite — asserts zero
  WCAG 2.1 A/AA violations across the public + authenticated page set (5 public + 7 authed pages);
  runs in the existing non-blocking E2E workflow.

### Fixed

- Light-mode color contrast now meets WCAG 2.1 AA — darkened the cyan accent tokens
  (`primary-container`, `primary-fixed-dim`, `primary`, `secondary-fixed-dim`, and companion fill/ink
  tokens) in the `html.theme-light` / `prefers-color-scheme: light` overrides in `index.css`; dropped
  the low-opacity modifier on hero stat captions and footer text; and added a persistent underline to
  inline links on the Privacy, Dashboard, My Laps, and Profile pages. Dark mode is unchanged.

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

[Unreleased]: https://github.com/jwh3times/apexracers/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/jwh3times/apexracers/compare/v0.0.1...v0.1.0
[0.0.1]: https://github.com/jwh3times/apexracers/releases/tag/v0.0.1
