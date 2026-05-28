---
name: react-frontend
description: Use for any work in src/web/ — React pages, components, contexts, the api.ts service client, Vitest tests, and Tailwind/design-token styling.
tools: Read, Write, Edit, Bash, Glob, Grep
model: sonnet
---

You are working inside the ApexRacers React frontend (`src/web/`). Know these patterns and enforce them without deviation.

## Stack

React 18 + Vite + TypeScript strict mode. All source in `src/web/src/`.

Dev commands (run from `src/web/`):
```bash
npm run dev          # Vite on :5173, proxies /api → http://localhost:5000
npm run dev:docker   # proxies /api → http://localhost:8080
npm run dev:all      # starts dotnet API + Vite together
npm run build        # tsc + Vite production build
npm run lint         # ESLint
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

- JWT persisted in IndexedDB via `dbGet`/`dbSet`/`dbRemove` (`src/services/db.ts`), keys prefixed `ar_`.
- Claims decoded client-side by `decodeJwt()`: `sub`, `email`, `name`, `role`, `iracing_id`.
- Roles: `Standard` | `Beta` | `Alpha` | `Admin`.
- `useAuth()` hook returns `{ user, loading, login, logout, updateSession, alertsEnabled, setAlertsEnabled }`.
- `login()` accepts `AuthResult + email`; `updateSession()` refreshes token and role after profile/role changes.
- **Never read the JWT or decode claims outside of `AuthContext`.** Never store the token in `localStorage` or component state.

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

Tailwind CSS with project-specific design tokens. Use these class names consistently:

| Token | Use |
|---|---|
| `glass-panel` | Card/panel backgrounds |
| `text-on-surface` | Primary text |
| `text-on-surface-variant` | Secondary/muted text |
| `text-primary-fixed-dim` | Accent text (lap times, links, highlights) |
| `font-display-lg` / `font-headline-md` / `font-headline-sm` | Heading hierarchy |
| `font-body-lg` / `font-body-sm` | Body text |
| `font-label-caps` / `text-label-caps` | Caps labels, navigation |
| `font-data-lg` / `font-data-md` | Numeric data displays |

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
