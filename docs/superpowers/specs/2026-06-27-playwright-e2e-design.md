# Playwright E2E — Thin Vertical Slice (Design)

**Date:** 2026-06-27
**Status:** Approved (design); pending implementation plan
**Backlog item:** ROADMAP "E2E testing, accessibility, and visual regression (Playwright)" → workstream (1) Playwright e2e suite

## Goal

Stand up a Playwright end-to-end test harness for the ApexRacers web app and land the
plumbing as a **thin vertical slice**: harness + config + **one** smoke test that exercises
the real stack end-to-end, plus a green CI job. The smoke test de-risks the harness and CI
topology before we invest in a broad test suite.

This is the first of the roadmap's three related workstreams. Accessibility (axe-core) and
visual regression (`toHaveScreenshot()`) are **out of scope** here and bolt onto this harness
later.

## Decisions (locked during brainstorming)

| Decision           | Choice                                                                       |
| ------------------ | ---------------------------------------------------------------------------- |
| First-cut scope    | Thin vertical slice: harness + 1 smoke test + green CI, expand in follow-ups |
| Test architecture  | Full real stack — Postgres + the real .NET API + the built SPA              |
| CI gating          | Runs on every PR, **non-blocking** (not a required check) until proven stable |
| Orchestration      | Approach A — single origin: the API serves the built SPA from `wwwroot`      |

## Why these were safe to decide

Verified against the codebase (not assumed):

- **Prod topology is single-origin.** `src/ApexRacers.Api/Program.cs` serves the SPA via
  `app.UseStaticFiles()` + `app.MapFallbackToFile("index.html")`; the `Dockerfile` builds
  `web/` and copies `dist` → `wwwroot`. Approach A mirrors prod exactly — no proxy/CORS layer.
- **The API self-migrates on boot.** `Program.cs` runs `await db.Database.MigrateAsync()` and
  creates the `Standard/Beta/Alpha/Admin` roles before the pipeline accepts traffic, so CI
  needs only an empty Postgres — no separate `dotnet ef database update` step.
- **`JWT_SIGNING_KEY` is required to boot** (`Program.cs` throws if unset) — supplied as a
  CI-only env var.
- **Registration is a tab on `/login`.** `web/src/features/auth/LoginPage.tsx` renders a
  `role="tablist"` with `signin`/`register` tabs (accessible roles), and
  `api.register(email, password)` logs the user in. There is no `/register` route.
- **Frontend lives at `web/`** (the roadmap's `src/web/` paths are stale).

## Scope

### In scope (this deliverable)

- Playwright harness + `web/playwright.config.ts`.
- One smoke test (`web/e2e/smoke.spec.ts`):
  1. Visit `/` (landing) and assert it renders.
  2. Navigate to `/login`, switch to the **Register** tab.
  3. Submit a fresh unique account (unique email per run).
  4. Assert redirect to `/dashboard` + an authenticated element is visible.
  5. Reload and confirm the session persists (refresh-token rehydration).
- Test helpers: `uniqueEmail()` and `registerNewUser(page)`.
- A new CI workflow (`.github/workflows/e2e.yml`) running the full real stack on every PR,
  non-blocking.

### Explicitly deferred (tracked as follow-ups — NOT built here)

- Broader auth flows (logout, password reset, email change).
- Telemetry upload + personal laps (a `.ibt` fixture can be generated via the existing
  `FakeIbtBuilder` approach).
- Public catalog pages (series / cars / tracks) — these need catalog seeding via the Seeder.
- `ComingSoonPage` gating for flagged routes.
- Accessibility (axe-core) — workstream (2).
- Visual regression (`toHaveScreenshot()`) — workstream (3).

## Topology (Approach A — single origin)

### Local (Windows)

- `docker compose up` brings up Postgres + the API on `:8080` (the API image already bundles
  the built SPA).
- Playwright `baseURL=http://localhost:8080`, `webServer.reuseExistingServer=true` so it
  attaches to the already-running stack rather than launching its own.

### CI

The workflow **prepares** the SPA build + API binary; **Playwright's `webServer` launches the
API** (and tears it down). The two never both start the API.

1. Postgres as a GitHub Actions `services:` container (health-gated).
2. `npm ci && npm run build` in `web/`.
3. Copy `web/dist` → `src/ApexRacers.Api/wwwroot`.
4. `dotnet publish src/ApexRacers.Api` (prepare the binary; the SPA is already in `wwwroot`).
5. `npx playwright install --with-deps chromium`.
6. `npx playwright test`. Playwright's `webServer.command` runs the published API; the
   workflow exports the API's env at job/step scope so the launched process inherits it:
   - `ASPNETCORE_URLS=http://localhost:8080`
   - `ASPNETCORE_ENVIRONMENT=Development`
   - `DATABASE_CONNECTION_STRING` → the Postgres service
   - a CI-only `JWT_SIGNING_KEY`

   `webServer.url` polls `GET /` for readiness (returns `index.html` 200 once up; migrations
   have completed before the server listens).

No iRacing creds, no catalog seeding for this slice.

## Files

| File                                    | Purpose                                                                                              |
| --------------------------------------- | --------------------------------------------------------------------------------------------------- |
| `web/playwright.config.ts`              | `testDir: 'e2e'`, `baseURL`, `webServer` (`reuseExistingServer: true` so it attaches to `docker compose` locally and launches the published API in CI), single Chromium project, `retries: CI ? 2 : 0`, `trace: 'on-first-retry'` |
| `web/e2e/smoke.spec.ts`                 | The smoke test                                                                                       |
| `web/e2e/helpers/`                      | `uniqueEmail()` + `registerNewUser(page)`                                                            |
| `web/package.json`                      | Add `@playwright/test` devDep; scripts `test:e2e`, `test:e2e:ui`                                     |
| `.gitignore`                            | Ignore `web/playwright-report/`, `web/test-results/`                                                 |
| `.github/workflows/e2e.yml`             | New non-blocking workflow                                                                            |

## Test conventions (for the suite to grow into)

- Prefer role/label selectors (`getByRole('tab', { name: /register/i })`, `getByLabel`) over
  CSS — the auth UI already exposes ARIA roles.
- Each test self-provisions its data (unique email per run) so runs are independent and
  idempotent against a persistent dev DB. No truncation needed.
- Keep `e2e/` out of the Vitest `include` (separate runner, separate tsconfig/lint scope) so
  unit coverage and the E2E suite don't collide.

## CI workflow shape

- Triggers: `pull_request` + `workflow_dispatch`.
- **Not** added to branch-protection required checks (the non-blocking decision); promote once
  stable.
- Steps: checkout → setup Node 26 + .NET 10 → build SPA into `wwwroot` → `dotnet publish` the
  API → install Chromium → `playwright test` (its `webServer` launches the API) → upload
  `playwright-report` artifact on failure.
- Independent of the `format`/`test` jobs and the unit-coverage threshold.

## Risks & mitigations

- **Flake:** CI retries (2) + `trace: 'on-first-retry'`; single browser to start.
- **No health endpoint:** poll `GET /` for readiness. (Optional future nicety: a tiny
  `/healthz` — out of scope here.)
- **Persistent dev DB state:** unique emails per run keep tests independent; no cleanup step.
- **CI cost:** an SPA build per run (seconds) — far cheaper than building the API Docker image.

## Success criteria

- `npm run test:e2e` (or `npx playwright test`) passes locally against `docker compose up`.
- The `e2e.yml` workflow runs on a PR and goes green, exercising register → dashboard against
  the real API + Postgres.
- The harness/conventions are in place so follow-up flows (and later a11y + visual) bolt on
  without rework.

## Follow-ups (out of scope, to be tracked in ROADMAP after this lands)

1. Broaden auth flows (logout, password reset via the Development token echo, email change).
2. Telemetry upload + personal laps (generate an `.ibt` fixture).
3. Public catalog pages (requires Seeder catalog data in CI).
4. `ComingSoonPage` gating.
5. axe-core accessibility checks bolted onto each page visit.
6. Visual regression via `toHaveScreenshot()`, pinned to `mcr.microsoft.com/playwright` in CI.
7. Promote the E2E workflow to a required check once stable.
