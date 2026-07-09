---
name: react-frontend
description: Use for any work in web/ — React pages, components, contexts, the api.ts service client, Vitest tests, and Tailwind/design-token styling.
tools: Read, Write, Edit, Bash, Glob, Grep
model: sonnet
---

You are working inside the ApexRacers React frontend (`web/`). Know these patterns and enforce them without deviation.

React 19 + Vite + TypeScript strict mode; all source in `web/src/`. The dev/build/lint/test/format commands are in CLAUDE.md (Commands → Frontend); CLAUDE.md also carries the routing map, the design-system summary, and the `request<T>` API-client contract. **This file owns the depth** on the API client, auth flow, the design-token catalog, and test patterns.

## API calls — always go through api.ts

**Never call `fetch()` directly in pages or components.** Every call goes through the single private `request<T>(path, init)` helper in `src/services/api.ts` — it attaches auth headers, retries once after a silent token refresh on 401, returns `undefined` for 204s, and maps RFC-7807 errors. Add a new endpoint as a method on the `api` export object: call `request` with `{ method, json }` (JSON body) or `{ method, body }` (raw/FormData), with a JSDoc comment naming the controller route. Do **not** reintroduce per-verb helpers (`get`/`postJson`/`putJson`/…) — they were removed in favor of `request<T>`.

When adding a new endpoint, update both `ResponseDtos.cs` (backend) and `api.ts` (frontend) — the TypeScript interfaces must mirror the C# records exactly: camelCase field names, `number | null` for `double?`, `string` for `DateTimeOffset` (ISO 8601). A `409` carrying `code: "IRACING_NOT_LINKED"` is surfaced as the typed `IRacingNotLinkedError` so pages can prompt to link an iRacing account.

## Authentication

Auth state lives entirely in `AuthContext` (`src/context/AuthContext.tsx`):

- JWT (15 min) + refresh token (7 days) — both persisted in IndexedDB via `dbGet`/`dbSet`/`dbRemove` (`src/services/db.ts`), keys `ar_token` and `ar_refresh_token`.
- Claims decoded client-side by `decodeJwt()`: `sub`, `email`, `name`, `role`, `iracing_id`, `exp`.
- Roles: `Standard` | `Beta` | `Alpha` | `Admin`.
- `useAuth()` hook returns `{ user, loading, login, logout, updateSession, alertsEnabled, setAlertsEnabled }`.
- `login()` accepts `AuthResult + email`; persists refresh token if present. `updateSession()` refreshes JWT after profile/role changes. `logout()` calls `api.revokeToken` then clears both tokens.
- On mount, `AuthContext` silently calls `api.refreshTokens` if the stored JWT is expired but a valid refresh token exists — so the session survives between visits without re-login.
- **Never read the JWT or decode claims outside of `AuthContext`.** Never store either token in `localStorage` or component state.

### 401 interceptor in api.ts

The `request<T>` helper intercepts 401 responses:

1. Call `tryRefresh()` — exchanges the stored refresh token for a new JWT + refresh token via `POST /api/auth/refresh`.
2. `tryRefresh` deduplicates concurrent 401s: the first call sets `_refreshPromise`; all subsequent callers await the same promise.
3. On success: call `_onTokenRefreshed(newToken, newRefreshToken)` (registered by `AuthContext` to persist both to IndexedDB) and retry the original request once.
4. On failure: call `_onSessionExpired()` (registered by `AuthContext` to clear user state) then throw.

Module-level exports for `AuthContext` to wire in: `setRefreshToken`, `onTokenRefreshed`, `onSessionExpired`. `clearToken` clears both access and refresh tokens.

## Feature flags

`FeatureFlagContext` (`src/context/FeatureFlagContext.tsx`) fetches flags from `GET /api/feature-flags` (public — signed-out visitors fetch the enabled Standard-tier set under a `guest` owner) and caches them; read them through the context hook (`useFeatureFlag(key)`). **Never inline flag logic or conditionally call flag APIs outside the context.**

## State management

React hooks only: `useState`, `useEffect`, `useCallback`, `useMemo`, `useRef`. No Redux, Zustand, Jotai, Recoil, or similar. For cross-component state, use the existing contexts or add a new context file following the same pattern as `AuthContext`.

## File structure

```
src/features/<area>/    ← feature-grouped route pages, each with a colocated *.test.tsx sibling
                          (auth, series, racing, driver, rivals, catalog, telemetry, profile, admin)
src/pages/              ← public/static pages only (Home, Terms, Privacy, ComingSoon)
src/pages/__tests__/    ← Vitest tests for the static pages
src/components/         ← shared UI pieces, each with a colocated *.test.tsx sibling
src/context/            ← React contexts (AuthContext, FeatureFlagContext), each with a colocated *.test.tsx sibling
src/services/           ← api.ts, db.ts, each with a colocated *.test.ts sibling
src/utils/              ← pure helper functions (e.g. lapTime.ts), each with a colocated *.test.ts sibling
```

## Styling

Tailwind CSS with a **fluid design system** — all sizing scales continuously with viewport width via `clamp()`. Use the custom utility classes from `src/index.css`; do not reach for one-off Tailwind classes for the same purposes.

**Primary accent is cyan.** Use `text-primary-container` / `bg-primary-container` / `border-primary-container` for all accent text, icons, buttons, and borders. `primary-fixed-dim` is the **dim-accent** token (remapped to cyan `#00b8d4` in `index.css`) — allowed for muted accent text/borders. Never hardcode the old greens: hexes `#00FF88` / `#00e479`, or green RGBA glows like `rgba(0,228,121,…)` / `rgba(0,255,136,…)`; for accent glows use a cyan RGBA such as `rgba(0,224,255,…)`.

### Typography

| Class               | Use                                        |
| ------------------- | ------------------------------------------ |
| `text-page-title`   | Large page heading (`h1`)                  |
| `text-section-head` | Card/panel section heading (`h2`, `h3`)    |
| `text-eyebrow`      | Mono ALL-CAPS label above a heading        |
| `text-body-fluid`   | Standard body and list text                |
| `text-small-fluid`  | Secondary/supporting text                  |
| `text-th`           | Table column header                        |
| `text-kpi-value`    | Large mono KPI number                      |
| `text-mono-fluid`   | Mono data values — lap times, rank numbers |

### Layout & spacing

| Class                        | Use                                             |
| ---------------------------- | ----------------------------------------------- |
| `page-wrap`                  | Outer page padding — apply to `<main>`          |
| `card-r`                     | Card border-radius                              |
| `card-p`                     | Card body padding                               |
| `card-hp`                    | Card section-header padding (scan-texture rows) |
| `kpi-p`                      | KPI tile padding                                |
| `td-p` / `th-p`              | Table cell / header padding                     |
| `gap-fluid` / `gap-fluid-lg` | Column/row gaps                                 |
| `btn-fluid` / `btn-fluid-sm` | Button height, padding, font-size, radius       |
| `grid-kpi`                   | Auto-fit KPI tile grid                          |
| `grid-cards`                 | Auto-fill series card grid                      |

### Standard card pattern

Every card uses a consistent `cardStyle` + optional `scanTexture` for header rows:

```tsx
const cardStyle: React.CSSProperties = {
  boxShadow:
    "0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)",
};
const scanTexture: React.CSSProperties = {
  backgroundImage:
    "repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)",
};

<div
  className="card-r border border-white/10 bg-surface overflow-hidden"
  style={cardStyle}
>
  <div
    className="card-hp border-b border-white/10 flex items-center justify-between"
    style={scanTexture}
  >
    <h3 className="text-section-head text-on-surface">Section title</h3>
  </div>
  <div className="card-p">{/* body */}</div>
</div>;
```

Icons use Material Symbols via `<span className="material-symbols-outlined" aria-hidden="true">icon_name</span>`. Always include `aria-hidden="true"` on decorative icons.

### Color tokens

| Token          | Utilities                           | Use                                                                                           |
| -------------- | ----------------------------------- | --------------------------------------------------------------------------------------------- |
| `--color-gold` | `text-gold` `bg-gold` `border-gold` | ELITE/premium tier accent (badges, trophies); sanctioned gold only — never hardcode `#FFD700` |

## Testing

- Framework: Vitest + React Testing Library; environment `jsdom`; setup file `src/test/setup.ts` (in `vite.config.ts`); `globals: true` (no need to import `describe`/`it`/`expect`).
- Every new source file needs a corresponding test file, colocated as a `*.test.ts(x)` sibling next to the source (pages in `features/`, components, contexts, services, and utils all follow this). The only remaining `__tests__/` directories are `src/pages/__tests__/` (static pages) and `src/__tests__/` (App-level route guards).
- Test behavior, not implementation: prefer `getByRole`, `getByText`, `findBy*` over snapshot tests.
- Mock `src/services/api.ts` with `vi.mock('../services/api')` in tests that call API methods; mock `src/context/AuthContext.tsx` when testing pages that call `useAuth()`.
- The **85%** coverage gate (statements/branches/functions/lines) and the prettier-check CI step are in CLAUDE.md (Testing). Run `npx vitest run --coverage` and `npx prettier --check .` before pushing.
- **End-to-end (Playwright):** tests live in `web/e2e/` and run against the full stack at `http://localhost:8080` (e.g. `docker compose up`). Config is `web/playwright.config.ts` (single Chromium project; `reuseExistingServer: !process.env.CI`). Run with `npm run test:e2e` (headless) or `npm run test:e2e:ui` (interactive). Vitest excludes `e2e/` via `include: ['src/**']` in `vite.config.ts` — E2E tests never count toward the coverage gate. A non-blocking per-PR GitHub Actions workflow (`.github/workflows/e2e.yml`) also runs the suite (Postgres service + builds SPA into API wwwroot + Playwright); it is not yet a required check.
- **Accessibility audits:** `web/e2e/a11y.spec.ts` asserts zero WCAG 2.1 A/AA violations across 5 public + 7 authed pages via `auditA11y(page)` from `web/e2e/helpers/a11y.ts` (`@axe-core/playwright`, `wcag2a`/`wcag2aa` tagset).
- **Visual regression:** `web/e2e/visual.spec.ts` captures full-page `toHaveScreenshot` baselines for the stable public pages; **CI-only** (`test.skip(!process.env.CI)`) with committed Linux/Chromium PNGs under `e2e/visual.spec.ts-snapshots/`. Refresh via `e2e.yml` `workflow_dispatch` (`update_snapshots=true`) → download the `visual-baselines` artifact → commit. Defaults (animations off, caret hidden, 2% tolerance) live in `playwright.config.ts`.
- **Accessibility design guidance:** When adding pages or new components, check whether they share the inline-accent-link or muted-caption patterns fixed in the workstream — if so, add `underline` to inline links in body text and remove any `text-on-surface-variant/60` opacity on light surfaces.
- **Light-mode cyan accent tokens — do not revert:** Nine tokens are intentionally overridden in the `html.theme-light` and `@media (prefers-color-scheme: light) html.theme-auto` blocks in `index.css` for WCAG AA contrast. Dark mode keeps the bright `@theme` cyan defaults. Protected set — primary fills: `primary-container`, `primary-fixed-dim`, `primary` (all `#006072`); companion fills: `secondary-fixed-dim` (`#00707f`), `primary-fixed` (`#004f5e`), `secondary-fixed` (`#005e6e`); ink tokens: `on-primary-fixed`, `on-primary-container`, `on-primary-fixed-variant` (all `#eafdff`). Do not revert these or add hardcoded bright-cyan text/icons on light surfaces — they will fail WCAG AA contrast.
