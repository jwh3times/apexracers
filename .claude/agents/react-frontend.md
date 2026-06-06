---
name: react-frontend
description: Use for any work in src/web/ — React pages, components, contexts, the api.ts service client, Vitest tests, and Tailwind/design-token styling.
tools: Read, Write, Edit, Bash, Glob, Grep
model: sonnet
---

You are working inside the ApexRacers React frontend (`src/web/`). Know these patterns and enforce them without deviation.

## Stack

React 19 + Vite + TypeScript strict mode. All source in `src/web/src/`.

Dev commands (run from `src/web/`):
```bash
npm run dev          # Vite on :5173, proxies /api → http://localhost:5000
npm run dev:docker   # proxies /api → http://localhost:8080
npm run dev:all      # starts dotnet API + Vite together
npm run build        # tsc + Vite production build
npm run lint         # ESLint
npx prettier --check .      # Prettier format check (also enforced in CI)
npx vitest run --coverage   # one-shot test run with coverage report
```

## API calls — always go through api.ts

All fetch calls are routed through the typed helpers in `src/services/api.ts`:

```typescript
get<T>(path)              // authenticated GET
postJson<T>(path, body)   // POST with JSON body
putJson<T>(path, body)    // PUT with JSON body
deleteReq(path)           // DELETE
postForm<T>(path, FormData) // multipart POST (telemetry upload)
```

**Never call `fetch()` directly in pages or components.** Add new API calls to the `api` export object at the bottom of `api.ts` with a JSDoc comment matching the controller route.

When adding a new endpoint, update both `ResponseDtos.cs` (backend) and `api.ts` (frontend) — the TypeScript interfaces must mirror the C# records exactly (camelCase field names, `number | null` for `double?`, `string` for `DateTimeOffset` as ISO 8601).

## Authentication

Auth state lives entirely in `AuthContext` (`src/context/AuthContext.tsx`):

- JWT (15 min) + refresh token (7 days) — both persisted in IndexedDB via `dbGet`/`dbSet`/`dbRemove` (`src/services/db.ts`), keys `ar_token` and `ar_refresh_token`.
- Claims decoded client-side by `decodeJwt()`: `sub`, `email`, `name`, `role`, `iracing_id`, `exp`.
- Roles: `Standard` | `Beta` | `Alpha` | `Admin`.
- `useAuth()` hook returns `{ user, loading, login, logout, updateSession, alertsEnabled, setAlertsEnabled }`.
- `login()` accepts `AuthResult + email`; persists refresh token if present. `updateSession()` refreshes JWT after profile/role changes. `logout()` calls `api.revokeToken` then clears both tokens.
- On mount, `AuthContext` silently calls `api.refreshTokens` if the stored JWT is expired but a valid refresh token exists — so the session survives between visits without requiring re-login.
- **Never read the JWT or decode claims outside of `AuthContext`.** Never store either token in `localStorage` or component state.

### 401 interceptor in api.ts

All six HTTP helpers (`get`, `post`, `postJson`, `putJson`, `deleteReq`, `postForm`) intercept 401 responses:

1. Call `tryRefresh()` — exchanges the stored refresh token for a new JWT + refresh token via `POST /api/auth/refresh`.
2. `tryRefresh` deduplicates concurrent 401s: the first call sets `_refreshPromise`; all subsequent callers await the same promise.
3. On success: call `_onTokenRefreshed(newToken, newRefreshToken)` (registered by `AuthContext` to persist both to IndexedDB).
4. On failure: call `_onSessionExpired()` (registered by `AuthContext` to clear user state) then throw.

New module-level exports in `api.ts`: `setRefreshToken`, `onTokenRefreshed`, `onSessionExpired`. `clearToken` now clears both access and refresh tokens.

## Feature flags

`FeatureFlagContext` (`src/context/FeatureFlagContext.tsx`) fetches flags from `GET /api/feature-flags` and caches them. Use `useFeatureFlags()` hook. **Never inline flag logic or conditionally call flag APIs outside the context.**

## State management

React hooks only: `useState`, `useEffect`, `useCallback`, `useMemo`, `useRef`. No Redux, Zustand, Jotai, Recoil, or similar. For cross-component state, use the existing contexts or add a new context file following the same pattern as `AuthContext`.

## File structure

```
src/pages/              ← one file per route page
src/pages/__tests__/    ← Vitest tests for pages
src/components/         ← shared UI pieces
src/components/__tests__/
src/context/            ← React contexts (AuthContext, FeatureFlagContext)
src/context/__tests__/
src/services/           ← api.ts, db.ts
src/services/__tests__/
src/utils/              ← pure helper functions (e.g. lapTime.ts)
```

## Styling

Tailwind CSS with a **fluid design system** — all sizing scales continuously with viewport width via `clamp()`. Use the custom utility classes from `src/index.css`; do not reach for one-off Tailwind classes for the same purposes.

**Primary accent is cyan.** Use `text-primary-container` / `bg-primary-container` / `border-primary-container` for all accent text, icons, buttons, and borders. Never hardcode old green values (`#00FF88`, `text-primary-fixed-dim`).

### Typography

| Class | Use |
|---|---|
| `text-page-title` | Large page heading (`h1`) |
| `text-section-head` | Card/panel section heading (`h2`, `h3`) |
| `text-eyebrow` | Mono ALL-CAPS label above a heading |
| `text-body-fluid` | Standard body and list text |
| `text-small-fluid` | Secondary/supporting text |
| `text-th` | Table column header |
| `text-kpi-value` | Large mono KPI number |
| `text-mono-fluid` | Mono data values — lap times, rank numbers |

### Layout & spacing

| Class | Use |
|---|---|
| `page-wrap` | Outer page padding — apply to `<main>` |
| `card-r` | Card border-radius |
| `card-p` | Card body padding |
| `card-hp` | Card section-header padding (scan-texture rows) |
| `kpi-p` | KPI tile padding |
| `td-p` / `th-p` | Table cell / header padding |
| `gap-fluid` / `gap-fluid-lg` | Column/row gaps |
| `btn-fluid` / `btn-fluid-sm` | Button height, padding, font-size, radius |
| `grid-kpi` | Auto-fit KPI tile grid |
| `grid-cards` | Auto-fill series card grid |

### Standard card pattern

Every card uses a consistent `cardStyle` + optional `scanTexture` for header rows:

```tsx
const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};
const scanTexture: React.CSSProperties = {
  backgroundImage: 'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

<div className="card-r border border-white/10 bg-surface overflow-hidden" style={cardStyle}>
  <div className="card-hp border-b border-white/10 flex items-center justify-between" style={scanTexture}>
    <h3 className="text-section-head text-on-surface">Section title</h3>
  </div>
  <div className="card-p">{/* body */}</div>
</div>
```

Icons use Material Symbols via `<span className="material-symbols-outlined" aria-hidden="true">icon_name</span>`. Always include `aria-hidden="true"` on decorative icons.

## Testing

- Framework: Vitest + React Testing Library.
- Setup file: `src/test/setup.ts` (configured in `vite.config.ts`).
- `globals: true` — no need to import `describe`, `it`, `expect`.
- Environment: `jsdom`.
- Every new source file needs a corresponding test file in the adjacent `__tests__/` directory.
- Coverage thresholds enforced at **80%** (statements, branches, functions, lines) — must pass before any change is done.
- Test behavior, not implementation: prefer `getByRole`, `getByText`, `findBy*` over snapshot tests.
- Mock `src/services/api.ts` with `vi.mock('../services/api')` in tests that call API methods.
- Mock `src/context/AuthContext.tsx` when testing pages that call `useAuth()`.

## CI requirements

The CI pipeline (`test` job in `.github/workflows/deploy.yml`) runs from `src/web/` and blocks both deploy jobs on failure:
1. `npm ci` — install dependencies (Node 26, locked via `package-lock.json`)
2. `npx prettier --check .` — formatting check; any unformatted file fails the build
3. `npx vitest run --coverage` — coverage must meet the 80% threshold

Run `npx prettier --check .` locally before pushing. To auto-fix: `npx prettier --write .`.
