# Frontend architecture (`web/`)

Part of the agent guide: the root `AGENTS.md` holds the ground rules, development flow, commands and
testing rules; this file holds the frontend architecture, and loads when work touches `web/`. Edit
this file, not `web/CLAUDE.md`. Pattern detail stays in the `react-frontend` agent.

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

## Routing (`web/src/App.tsx`)

Two tiers. **Public** (no AppShell): `/`, `/login`, `/forgot-password`, `/reset-password`,
`/verify-email`, `/terms`, `/privacy`. Every other route renders inside `AppShell` (Sidebar + TopNav +
Footer); `web/src/App.tsx` is the route list.

- **`AdminGuard`** (wraps `/admin`): unauthenticated → `/login`; authed non-admin → `/dashboard`.
- **`RequireFlag`** (wraps iRacing-dependent routes): renders nothing while feature flags for the
  current guest/user/role owner are unresolved, then renders `ComingSoonPage` when both
  `iracing-live` and `iracing-demo` are off or the child when either is on. Auth-independent.
  `useIracingSurface()` owns that union for the guard, navigation, alerts, Dashboard, and Profile.

## Components, contexts, utilities

- **Components** (`web/src/components/`): `Sidebar` / `TopNav` (nav filtered by the `iracing-live`
  OR `iracing-demo` flag via shared `visibleNav`), `NotificationsBell` (client-derived alerts via pure
  `deriveAlerts`), `DemoBanner` (shows while `iracing-demo` on), `Footer`, and the SVG charts
  `Sparkline` / `PercentileBadge` / `LapTraceChart` / `IRatingCompareChart` (each returns `null` below
  its minimum data points — guard the wrapper).
  `DriverDisclosure` supplies bounded protected personal/shared cards and own-User withdrawal;
  positive protected output remains limited to the controlled synthetic topology.
- **Contexts** (`web/src/context/`): `AuthContext` (signed-in user, login/logout, `alertsEnabled` — a
  thin React binding over the session module, which owns the token pair, claims, persistence, and
  silent refresh), `ThemeContext` (auto/light/dark, persists via `PUT /api/auth/theme`),
  `FeatureFlagContext` (`useFeatureFlags()` exposes `isEnabled` + owner-specific `ready`;
  `useFeatureFlag(key)` handles one key; `useIracingSurface()` owns the live/demo union), and
  `PaceSourceContext` (the app-lifetime official-only vs. Uploaded-Lap evidence choice; resets when
  the signed-in User changes).
- **Utilities** (`web/src/utils/`): import the shared `formatLapTime`, `toTopPercent` /
  `topPercentLabel`, `deriveAlerts`, `breadcrumbs`, `raceWeekNumber` / `raceWeekLabel`, `splitLabel`, `unrepresentedEntriesNote`,
  `lapEvidenceLabel` / `lapEvidenceDescription` / `isUploadedEvidence` — **don't**
  re-inline these in pages. `raceWeekNumber(index)` / `raceWeekLabel(index)` convert the zero-based
  Race Week Index the API/router carry into the one-based Race Week Number drivers read (`CONTEXT.md`
  is the canonical definition of both terms); convert only at the display boundary and keep passing
  the original index to `api.ts` calls and route params.
