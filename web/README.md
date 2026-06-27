# ApexRacers — Frontend

React + TypeScript + Vite frontend for ApexRacers. All `/api` requests are proxied to the backend API by the Vite dev server.

## Tech stack

- React 19, React Router v7
- TypeScript 6, Vite 8
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
npm run lint     # ESLint
```

## Testing

```bash
npm run test          # Vitest one-shot run
npm run test:watch    # Vitest in watch mode
npx vitest run --coverage   # Coverage report (80% threshold enforced)
npx prettier --check .      # Formatting check (also runs in CI)
npx prettier --write .      # Auto-fix formatting
```

Coverage is enforced at **80%** across statements, branches, functions, and lines in `vite.config.ts`. Keep all four metrics above the threshold when adding new source files.

The CI `test` job runs `npx prettier --check .` before the Vitest coverage step. Any unformatted file blocks both deploy jobs.

## Project structure

```
src/
  features/           ← feature-grouped pages, each with a colocated *.test.tsx sibling
    auth/ series/ racing/ driver/ rivals/ catalog/ telemetry/ profile/ admin/
  pages/              ← public/static pages only (Home, Terms, Privacy, ComingSoon)
  pages/__tests__/    ← Vitest tests for the static pages
  components/         ← shared UI (Sidebar, TopNav, Footer, Sparkline, …) + colocated *.test.tsx siblings
  context/            ← AuthContext, ThemeContext, FeatureFlagContext + colocated *.test.tsx siblings
  services/           ← api.ts (typed fetch client), db.ts (IndexedDB helpers) + colocated *.test.ts siblings
  utils/              ← formatLapTime, topPercentLabel, deriveAlerts, breadcrumbs + colocated *.test.ts siblings
  test/               ← setup.ts (Vitest global setup)
  App.tsx             ← route definitions, AppShell layout
  index.css           ← Tailwind base + fluid design token utilities
```

## API client

All fetch calls go through `src/services/api.ts`. Never call `fetch()` directly in pages or components. Response types in `api.ts` must stay in sync with `ResponseDtos.cs` in `src/ApexRacers.Api/Dtos/`.

The client includes a **401 interceptor**: on a 401 response, it silently exchanges the stored refresh token for a new JWT via `POST /api/auth/refresh`, then retries the original request. Concurrent 401s are deduplicated — only one refresh call is made regardless of how many requests fail simultaneously.

## Authentication

Auth state is managed by `AuthContext` (`src/context/AuthContext.tsx`). Use the `useAuth()` hook — never read tokens or decode JWT claims outside the context.

- **Access token** (15-min JWT) and **refresh token** (7-day, rotating) are both stored in IndexedDB via `src/services/db.ts` under keys `ar_token` and `ar_refresh_token`.
- On app load, `AuthContext` checks whether the stored JWT is expired. If it is but a refresh token exists, it silently calls the refresh endpoint to restore the session without re-login.
- `logout()` revokes the refresh token server-side (`POST /api/auth/logout`) before clearing local state.

## Contexts

| Context              | Hook                | Purpose                                                                               |
| -------------------- | ------------------- | ------------------------------------------------------------------------------------- |
| `AuthContext`        | `useAuth()`         | User session, JWT + refresh token, login/logout, profile updates, role, alerts toggle |
| `ThemeContext`       | `useTheme()`        | `auto`/`light`/`dark` theme; applies class to `<html>`; persists to API               |
| `FeatureFlagContext` | `useFeatureFlags()` | Fetches flags from `/api/feature-flags`; exposes `hasFlag(key)`                       |

## Design system

All sizing scales continuously with viewport width via `clamp()`. Use the utility classes from `index.css` — do not use ad-hoc Tailwind classes for the same purposes.

**Primary accent is cyan** — use `text-primary-container` / `bg-primary-container` / `border-primary-container`. The old green tokens (`text-primary-fixed-dim`, `#00FF88`) are removed.

**Typography:** `text-page-title`, `text-section-head`, `text-eyebrow`, `text-body-fluid`, `text-small-fluid`, `text-th`, `text-kpi-value`, `text-mono-fluid`

**Layout:** `page-wrap`, `card-r`, `card-p`, `card-hp`, `kpi-p`, `td-p`, `th-p`, `gap-fluid`, `gap-fluid-lg`, `btn-fluid`, `btn-fluid-sm`, `grid-kpi`, `grid-cards`

Every card uses `cardStyle` (box-shadow) + optional `scanTexture` (header background) CSS constants — see any existing page for the pattern.
