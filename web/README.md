# ApexRacers — Frontend

React + TypeScript + Vite frontend for ApexRacers. All `/api` requests are proxied to the backend API by the Vite dev server.

## Tech stack

- React 19, React Router v7
- TypeScript 7, Vite 8
- Tailwind CSS v4
- Vitest + Testing Library
- Node.js 26+ (required; enforced via `engines` in `package.json`)

## Dev servers

Run from this directory (`web/`):

```bash
npm install

npm run dev          # Proxy → http://localhost:5000  (local dotnet API)
npm run dev:all      # Starts dotnet API + Vite together via concurrently
npm run dev:docker   # Proxy → http://localhost:8080  (Docker Desktop API)
npm run dev:cloud    # Proxy → https://apexracers-api.azurewebsites.net
```

The dev server runs on `http://localhost:5173`. The proxy target is set by `API_TARGET` in the relevant `.env.*` file; the default (`dev` / `dev:all`) needs no env file.

## Building

```bash
npm run build    # tsc + Vite production build → dist/
npm run preview  # Serve the production build locally
npm run lint     # Oxlint with TypeScript 7-powered type-aware rules
```

## Testing

### Unit / integration (Vitest)

```bash
npm run test          # Vitest one-shot run
npm run test:watch    # Vitest in watch mode
npx vitest run --coverage   # Coverage report (85% threshold enforced)
npx prettier --check .      # Formatting check (also runs in CI)
npx prettier --write .      # Auto-fix formatting
```

Coverage is enforced at **85%** across statements, branches, functions, and lines in `vite.config.ts`. Keep all four metrics above the threshold when adding new source files.

The CI `test` job runs `npx prettier --check .` before the Vitest coverage step. Any unformatted file blocks both deploy jobs.

### End-to-end (Playwright)

Tests live in `web/e2e/`. They run against the full local stack served at `http://localhost:8080`.

```bash
# Start the full stack first (Postgres + API serving the SPA at :8080)
docker compose up -d

npm run test:e2e      # Playwright headless run (Chromium)
npm run test:e2e:ui   # Playwright UI mode (interactive)
```

Config is `web/playwright.config.ts` (single Chromium project; `baseURL` `http://localhost:8080`;
`reuseExistingServer: !process.env.CI` so Playwright attaches to the running stack locally and always
starts a fresh server in CI). E2E tests are excluded from Vitest coverage (`vite.config.ts` scopes
Vitest to `src/**`). The suite also runs in CI via a non-blocking per-PR workflow
(`.github/workflows/e2e.yml`) — it is not yet a required check.

**Accessibility audits:** `web/e2e/a11y.spec.ts` runs WCAG 2.1 A/AA axe-core checks across 5 public
pages and 7 authenticated pages and asserts zero violations. The shared helper
`web/e2e/helpers/a11y.ts` exports `auditA11y(page, opts?)` (runs `@axe-core/playwright` against the
`wcag2a`/`wcag2aa` tagset) and `formatViolations(violations)` (human-readable summary for test
failure output).

**Functional specs:** `auth.spec.ts` (logout; password reset via the Development token echo),
`telemetry.spec.ts` (`.ibt` upload → My Laps, from the committed `e2e/fixtures/demo-session.ibt`),
`admin.spec.ts` (provisions an Admin, then axe-audits `/admin`), and `gating.spec.ts` (feature-flag
gating — gated routes render synthetic demo content when `iracing-demo` is on, ComingSoon when off).

**Visual regression:** `web/e2e/visual.spec.ts` captures full-page screenshot baselines for the
stable public pages (`/`, `/login`, `/terms`, `/privacy`). It is **CI-only**
(`test.skip(!process.env.CI)`) — the committed baselines under `e2e/visual.spec.ts-snapshots/` are
Linux/Chromium PNGs, and screenshot defaults (animations off, caret hidden, 2% pixel tolerance) live
in `playwright.config.ts`. Refresh them by running `e2e.yml` via `workflow_dispatch` with
`update_snapshots=true`, downloading the `visual-baselines` artifact, and committing it.

## Project structure

```
e2e/
  fixtures/
    demo-session.ibt  ← committed .ibt telemetry fixture (from FakeIbtBuilder)
  helpers/
    a11y.ts           ← auditA11y() / formatViolations() — shared axe-core helper
    admin.ts          ← promoteToAdmin() — swaps a test user to the Admin role
    db.ts             ← runSql() — psql / docker-compose SQL runner
    users.ts          ← registerNewUser(), login(), logout() test-user helpers
  a11y.spec.ts        ← WCAG 2.1 A/AA audits: 5 public + 7 authed pages (axe-core)
  admin.spec.ts       ← provisions an Admin and axe-audits /admin
  auth.spec.ts        ← logout + password-reset auth flows
  gating.spec.ts      ← feature-flag gating (demo content vs ComingSoon)
  smoke.spec.ts       ← register → dashboard smoke test
  telemetry.spec.ts   ← .ibt upload → My Laps
  visual.spec.ts      ← CI-only visual regression (baselines in visual.spec.ts-snapshots/)
src/
  features/           ← feature-grouped pages, each with a colocated *.test.tsx sibling
    auth/ series/ racing/ driver/ rivals/ catalog/ telemetry/ profile/ admin/
  pages/              ← public/static pages only (Home, Terms, Privacy, ComingSoon)
  pages/__tests__/    ← Vitest tests for the static pages
  components/         ← shared UI (Sidebar, TopNav, Footer, ResourceView, …) + colocated *.test.tsx siblings
  context/            ← AuthContext + AuthProvider, ThemeContext, FeatureFlagContext + colocated
                          *.test.tsx siblings
  hooks/              ← useResource (read-only page request lifecycle) + colocated *.test.tsx sibling
  services/           ← api.ts (typed fetch client), http.ts (request core + error classes),
                          session.ts (signed-in session: tokens, claims, persistence, silent
                          refresh), db.ts (IndexedDB helpers) + colocated *.test.ts siblings
  utils/              ← formatLapTime, toTopPercent/topPercentLabel, deriveAlerts, breadcrumbs,
                          raceWeekNumber/raceWeekLabel (0-based Race Week Index → 1-based Race Week
                          Number) + colocated *.test.ts siblings
  test/               ← setup.ts (Vitest global setup), apiMock.ts (shared api.ts mock factory for tests)
  App.tsx             ← route definitions, AppShell layout
  index.css           ← Tailwind base + fluid design token utilities
```

`PercentileBadge` accepts the API's higher-is-better percentile rank and owns the conversion to the
displayed lower-is-better `TOP X%` value through `toTopPercent`. Pass the raw rank to the badge; use
`topPercentLabel` when only the formatted label is needed.

## API client

All fetch calls go through `src/services/api.ts`, which builds on the request core in `src/services/http.ts`. Never call `fetch()` directly in pages or components. Response types in `api.ts` must stay in sync with `ResponseDtos.cs` in `src/ApexRacers.Api/Dtos/`.

The client includes a **401 interceptor**: on a 401 response, it silently exchanges the stored refresh token for a new JWT via `POST /api/auth/refresh`, then retries the original request. Concurrent 401s are deduplicated — only one refresh call is made regardless of how many requests fail simultaneously.

Read-only page requests use `src/hooks/useResource.ts`. Its fetcher receives an `AbortSignal`; pass
that signal through the matching `api` method so dependency changes and unmounts cancel the request.
The hook owns loading, stale-result suppression, typed `IRACING_NOT_LINKED` classification, and generic
errors. Render those non-data states with `ResourceView`, and declare deliberately optional overlays
with the hook's typed `onNotLinked` / `onError` fallbacks. Mutation-owned lists, debounced searches,
uploads, and domain workflows keep their focused local state machines.

## Authentication

The signed-in session — tokens, decoded claims, persistence, and the silent refresh — is owned by `src/services/session.ts`, not by a React context. `AuthContext` (`src/context/AuthContext.tsx` + `AuthProvider.tsx`) is a thin binding over it. Use the `useAuth()` hook — never read tokens or decode JWT claims outside `session.ts`.

- **Access token** (15-min JWT) and **refresh token** (7-day, rotating) are both stored in IndexedDB via `src/services/db.ts` under keys `ar_token` and `ar_refresh_token`.
- On app load, `AuthProvider` awaits `session.restore()`, which checks whether the stored JWT is expired. If it is but a refresh token exists, it silently calls the refresh endpoint to restore the session without re-login.
- `logout()` revokes the refresh token server-side (`POST /api/auth/logout`) before clearing local state via `session.clear()`.

## Contexts

| Context              | Hook(s)                                                           | Purpose                                                                                                                                                |
| -------------------- | ----------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `AuthContext`        | `useAuth()`                                                       | User (binding over `session.ts`), login/logout, profile updates, role, alerts toggle                                                                   |
| `ThemeContext`       | `useTheme()`                                                      | `auto`/`light`/`dark` theme; applies class to `<html>`; persists to API                                                                                |
| `FeatureFlagContext` | `useFeatureFlags()`, `useFeatureFlag(key)`, `useIracingSurface()` | Fetches the public/current-user flag set; exposes `isEnabled` + owner-specific `ready`, a single-key helper, and the shared live-or-demo surface state |

## Design system

All sizing scales continuously with viewport width via `clamp()`. Use the utility classes from `index.css` — do not use ad-hoc Tailwind classes for the same purposes.

**Primary accent is cyan** — use `text-primary-container` / `bg-primary-container` / `border-primary-container`. The old green tokens (`text-primary-fixed-dim`, `#00FF88`) are removed.

**Typography:** `text-page-title`, `text-section-head`, `text-eyebrow`, `text-body-fluid`, `text-small-fluid`, `text-th`, `text-kpi-value`, `text-mono-fluid`

**Gold accent** — `text-gold` / `bg-gold` / `border-gold` / `shadow-gold` are the only sanctioned gold tokens, reserved for the ELITE/premium tier accent on badges and trophies. Never hardcode `#FFD700`.

**Layout:** `page-wrap`, `card-r`, `card-shadow`, `scan-texture`, `card-p`, `card-hp`, `kpi-p`, `td-p`, `th-p`, `gap-fluid`, `gap-fluid-lg`, `btn-fluid`, `btn-fluid-sm`, `grid-kpi`, `grid-cards`

Use `card-shadow` for shared card elevation and `scan-texture` for the optional diagonal header/hero
texture. Their theme-aware values live in `index.css`; do not copy the underlying CSS literals into
components.
