---
name: react-frontend
description: Use for any work in web/ — React pages, components, contexts, the api.ts service client, Vitest tests, and Tailwind/design-token styling.
tools: Read, Write, Edit, Bash, Glob, Grep
model: sonnet
---

You are working inside the ApexRacers React frontend (`web/`). Know these patterns and enforce them without deviation.

React 19 + Vite + TypeScript strict mode; all source in `web/src/`. The dev/build/lint/test/format commands are in AGENTS.md (Commands → Frontend); AGENTS.md also carries the routing map, the design-system summary, and the `request<T>` API-client contract. **This file owns the depth** on the API client, auth flow, the design-token catalog, and test patterns.

## API calls — always go through api.ts

**Never call `fetch()` directly in pages or components.** Every call goes through `request<T>(path, init)`, exported from `src/services/api.ts` and built on `createHttpClient(...)` in `src/services/http.ts` — it attaches auth headers, retries once after a silent token refresh on 401, returns `undefined` for 204s, and maps RFC-7807 errors. Add a new endpoint as a method on the `api` export object: call `request` with `{ method, json }` (JSON body) or `{ method, body }` (raw/FormData), with a JSDoc comment naming the controller route. Do **not** reintroduce per-verb helpers (`get`/`postJson`/`putJson`/…) — they were removed in favor of `request<T>`.

`services/http.ts` owns the request core itself plus the `ApiError` / `IRacingNotLinkedError` classes and the RFC-7807 message-picking logic (`throwForResponse`, `humanMessageFor`) — `api.ts` re-exports the error classes rather than redeclaring them, so there is exactly one class identity across the app. `http.ts` takes `fetch`, `getAccessToken`, and `refresh` as injected dependencies and knows nothing about where tokens live; `api.ts` is the only place that supplies real ones, wiring both to the `session` singleton (`services/session.ts` — see Authentication below) rather than holding any token state itself. Because those three are injected, `createHttpClient` is directly unit-testable — including the 401-retry branch — without touching global `fetch`; prefer adding a test against `createHttpClient` itself in `http.test.ts` over exercising retry behavior indirectly through an `api` method.

When adding a new endpoint, update both `ResponseDtos.cs` (backend) and `api.ts` (frontend) — the TypeScript interfaces must mirror the C# records exactly: camelCase field names, `number | null` for `double?`, `string` for `DateTimeOffset` (ISO 8601). A `409` carrying `code: "IRACING_NOT_LINKED"` is surfaced as the typed `IRacingNotLinkedError` so pages can prompt to link an iRacing account.

## Authentication

The signed-in session — the token pair, the claims decoded from it, its persistence, and the silent
refresh — is owned by one module, `src/services/session.ts`, not by a context. It exposes an
app-wide `session` singleton built with `createSession(deps)`, behind `restore()` / `adopt()` /
`clear()` / `refresh()` / `subscribe()`. `AuthContext` (`src/context/AuthContext.tsx` +
`AuthProvider.tsx`) is a thin React binding over it — it holds no token state of its own:

- `AuthProvider` subscribes to `session.subscribe()` on mount (unsubscribing on unmount) and awaits
  `session.restore()` once to settle `loading`; `login`/`logout`/`updateSession` call
  `session.adopt(tokens)` / `session.clear()` and derive the displayed `user` from
  `session.claims`.
- Storage is a `KeyValueStore` **seam** (`get`/`set`/`remove`) with two real adapters: `indexedDbStore`
  in the app (wrapping `dbGet`/`dbSet`/`dbRemove` from `src/services/db.ts`) and an in-memory one
  built inline in tests. Keys are `ar_token` and `ar_refresh_token`; JWT is 15 min, refresh token
  7 days.
- Claims are decoded client-side by the pure `decodeJwt()` / `isTokenExpired()` exported from
  `session.ts`: `sub`, `email`, `name`, `role`, `iracing_id`, `theme_preference`, `exp`.
- Roles: `Standard` | `Beta` | `Alpha` | `Admin`.
- `useAuth()` hook returns `{ user, loading, login, logout, updateSession, alertsEnabled, setAlertsEnabled }`.
- The refresh transport is **injected** into `createSession` — the app wires it to a raw `fetch`
  call, deliberately bypassing the intercepting http client: routing it through `http.ts` would call
  back into `session.refresh()` on a 401 and recurse. `session.refresh()` dedupes concurrent callers
  behind one in-flight promise, whichever caller triggered it (boot-time `restore()` or the 401
  interceptor below).
- `session.subscribe(listener)` returns an unsubscribe. It's a list, not a single slot — a second
  subscriber (StrictMode's double-invoke, a multi-provider test) adds a listener instead of silently
  replacing the first.
- **Never read the JWT or decode claims outside of `session.ts`.** Never store either token in
  `localStorage` or component state.

### 401 interceptor

`http.ts`'s `request<T>` calls the injected `refresh()` dependency on a 401 and retries the original
request once if it resolves `true`. `api.ts` wires that dependency straight to `session.refresh()` —
`http.ts` itself knows nothing about tokens, so there is no `AuthContext`-facing refresh API to keep
in sync; a successful or failed refresh reaches `AuthContext` the same way any other session change
does, through `session.subscribe()`.

### Testing session state

`session` is an **app-wide singleton** — any test that touches it, directly or indirectly through
`AuthProvider`, must `await session.clear()` in `beforeEach`, or state leaks between tests (see
`AuthContext.test.tsx`). `session.test.ts` exercises `createSession` directly against an in-memory
store and a stub refresh transport; `AuthContext.test.tsx` mocks `services/db` (the storage seam's
real adapter) and drives the real `session` singleton through `AuthProvider`, asserting the React
binding follows it — it does not mock `session.ts` itself.

## Feature flags

`FeatureFlagProvider` fetches `GET /api/feature-flags` for an owner keyed by guest or user ID + role.
`useFeatureFlags()` exposes `{ isEnabled, ready }`: `ready` is true only when the stored map belongs
to the current owner, including an empty map settled by a failed request. Owner changes therefore
fail closed while refetching without treating the stale map as current.

Use `useFeatureFlag(key)` for a single independent flag. Use `useIracingSurface()` anywhere the
iRacing surface depends on `iracing-live` **OR** `iracing-demo`; it owns both unconditional key reads
and returns `{ enabled, ready }`. Route guards render no fallback while `ready` is false, then choose
the gated content or `ComingSoonPage`. Keep flag combination and loading decisions in these context
hooks rather than re-deriving them in consumers.

## State management

React hooks only: `useState`, `useEffect`, `useCallback`, `useMemo`, `useRef`. No Redux, Zustand, Jotai, Recoil, or similar. For cross-component state, use the existing contexts or add a new context file following the same pattern as `AuthContext`.

## File structure

```
src/features/<area>/    ← feature-grouped route pages, each with a colocated *.test.tsx sibling
                          (auth, series, racing, driver, rivals, catalog, telemetry, profile, admin)
src/pages/              ← public/static pages only (Home, Terms, Privacy, ComingSoon)
src/pages/__tests__/    ← Vitest tests for the static pages
src/components/         ← shared UI pieces, each with a colocated *.test.tsx sibling
src/context/            ← React contexts (AuthContext, FeatureFlagContext) + their Provider components
                          (AuthProvider, …), each with a colocated *.test.tsx sibling
src/hooks/              ← shared hooks (`useResource`) with colocated *.test.tsx siblings
src/services/           ← api.ts, http.ts, session.ts, db.ts, each with a colocated *.test.ts sibling
src/utils/              ← pure helper functions (e.g. lapTime.ts), each with a colocated *.test.ts sibling
src/test/               ← setup.ts (Vitest global setup), apiMock.ts (shared api.ts mock factory — see Testing)
```

### Percentile display contract

API percentile ranks are higher-is-better. Pass the raw `percentileRank` to `PercentileBadge`, which
owns the lower-is-better `TOP X%` conversion through `utils/percentile.toTopPercent`. When only text
is needed, use `topPercentLabel`; never invert or floor the rank in a page or another component.

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

Use the shared `card-shadow` and `scan-texture` classes. Their theme-aware values are owned in
`web/src/index.css`; do not copy their box-shadow or repeating-gradient literals into a component.

```tsx
<div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
  <div className="card-hp scan-texture border-b border-line-2 flex items-center justify-between">
    <h3 className="text-section-head text-on-surface">Section title</h3>
  </div>
  <div className="card-p">{/* body */}</div>
</div>;
```

### Read-only page resources

Use `useResource` for remote data that a page reads without locally mutating the returned collection.
Pass its `AbortSignal` through the matching `api` method to the shared HTTP client, and render loading,
typed `IRACING_NOT_LINKED`, and error states with `ResourceView`. If an optional overlay deliberately
disappears when it is unavailable, declare its typed fallback with `onNotLinked` and/or `onError` at
the hook call instead of reinterpreting every non-`ok` state in the component.

Keep local state machines for mutation-owned collections and domain workflows: admin CRUD lists that
are updated in place after writes, debounced searches, uploads, and the percentile lookup's
idle/not-found/compute flow are not read-only page resources.

Icons use Material Symbols via `<span className="material-symbols-outlined" aria-hidden="true">icon_name</span>`. Always include `aria-hidden="true"` on decorative icons.

### Color tokens

| Token          | Utilities                           | Use                                                                                           |
| -------------- | ----------------------------------- | --------------------------------------------------------------------------------------------- |
| `--color-gold` | `text-gold` `bg-gold` `border-gold` | ELITE/premium tier accent (badges, trophies); sanctioned gold only — never hardcode `#FFD700` |

## Testing

- Framework: Vitest + React Testing Library; environment `jsdom`; setup file `src/test/setup.ts` (in `vite.config.ts`); `globals: true` (no need to import `describe`/`it`/`expect`).
- Every new source file needs a corresponding test file, colocated as a `*.test.ts(x)` sibling next to the source (pages in `features/`, components, contexts, services, and utils all follow this). The only remaining `__tests__/` directories are `src/pages/__tests__/` (static pages) and `src/__tests__/` (App-level route guards).
- Test behavior, not implementation: prefer `getByRole`, `getByText`, `findBy*` over snapshot tests.
- Mock `src/services/api.ts` in tests that call API methods; mock `src/context/AuthContext.tsx` when testing pages that call `useAuth()`. If the page under test does an `instanceof ApiError` / `instanceof IRacingNotLinkedError` check, don't hand-roll a stand-in error class inside the mock factory — replacing the whole module resolves that check against the mock's class, not the real one, so the test can pass for the wrong reason (or hide a real 404/409-handling bug, as `PercentileCarPage.test.tsx` did). The standard way to mock `api.ts` is the shared `mockApiModule` helper (`src/test/apiMock.ts`): it keeps every real export — crucially the error classes — and auto-stubs every method on the `api` object, so a page adding a new call doesn't require touching its test's mock factory:

  ```ts
  vi.mock('../../services/api', async importOriginal => {
    const { mockApiModule } = await import('../../test/apiMock');
    return mockApiModule(importOriginal);
  });
  ```

  Then reject with the real class: `vi.mocked(api.getX).mockRejectedValue(new ApiError(404, 'Not Found'))`. The dynamic import inside the factory is required because `vi.mock` is hoisted above the file's own imports. Two files intentionally keep bespoke factories instead: `context/AuthContext.test.tsx` (mocks only `api.revokeToken` plus `services/db` — the session's storage seam — so it can drive the real `session` singleton through `AuthProvider` rather than a stand-in) and `context/ThemeContext.test.tsx` (needs a capturing `updateTheme` implementation, not a bare stub).
- The **85%** coverage gate (statements/branches/functions/lines) and the prettier-check CI step are in AGENTS.md (Testing). Run `npx vitest run --coverage` and `npx prettier --check .` before pushing.
- **End-to-end (Playwright):** tests live in `web/e2e/` and run against the full stack at `http://localhost:8080` (e.g. `docker compose up`). Config is `web/playwright.config.ts` (single Chromium project; `reuseExistingServer: !process.env.CI`). Run with `npm run test:e2e` (headless) or `npm run test:e2e:ui` (interactive). Vitest excludes `e2e/` via `include: ['src/**']` in `vite.config.ts` — E2E tests never count toward the coverage gate. A non-blocking per-PR GitHub Actions workflow (`.github/workflows/e2e.yml`) also runs the suite (Postgres service + builds SPA into API wwwroot + Playwright); it is not yet a required check.
- **Accessibility audits:** `web/e2e/a11y.spec.ts` asserts zero WCAG 2.1 A/AA violations across 5 public + 7 authed pages via `auditA11y(page)` from `web/e2e/helpers/a11y.ts` (`@axe-core/playwright`, `wcag2a`/`wcag2aa` tagset).
- **Visual regression:** `web/e2e/visual.spec.ts` captures full-page `toHaveScreenshot` baselines for the stable public pages; **CI-only** (`test.skip(!process.env.CI)`) with committed Linux/Chromium PNGs under `e2e/visual.spec.ts-snapshots/`. Refresh via `e2e.yml` `workflow_dispatch` (`update_snapshots=true`) → download the `visual-baselines` artifact → commit. Defaults (animations off, caret hidden, 2% tolerance) live in `playwright.config.ts`.
- **Accessibility design guidance:** When adding pages or new components, check whether they share the inline-accent-link or muted-caption patterns fixed in the workstream — if so, add `underline` to inline links in body text and remove any `text-on-surface-variant/60` opacity on light surfaces.
- **Light-mode cyan accent tokens — do not revert:** Nine tokens are intentionally overridden in the `html.theme-light` and `@media (prefers-color-scheme: light) html.theme-auto` blocks in `index.css` for WCAG AA contrast. Dark mode keeps the bright `@theme` cyan defaults. Protected set — primary fills: `primary-container`, `primary-fixed-dim`, `primary` (all `#006072`); companion fills: `secondary-fixed-dim` (`#00707f`), `primary-fixed` (`#004f5e`), `secondary-fixed` (`#005e6e`); ink tokens: `on-primary-fixed`, `on-primary-container`, `on-primary-fixed-variant` (all `#eafdff`). Do not revert these or add hardcoded bright-cyan text/icons on light surfaces — they will fail WCAG AA contrast.
