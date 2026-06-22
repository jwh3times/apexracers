# M1 — Pre-iRacing Launch Mode (feature-flag the iRacing-dependent surface)

**Date:** 2026-06-21
**Status:** Approved design — ready for implementation plan
**Source milestone:** `private/ROADMAP.md` → "M1 — Pre-iRacing launch mode"

---

## Problem

ApexRacers is live on Azure, but every iRacing-data-backed surface is non-functional in
production because the iRacing **service-account OAuth credentials are unavailable** (iRacing
has closed new client registration). Today a visitor who clicks Series / Analytics /
Leaderboards / Cars / Tracks / etc. hits empty states, perpetual spinners, or `503`s. That
reads as "broken," not "coming soon."

## Goal

Gate the entire iRacing-dependent surface behind **one feature flag** so the deployed app
presents as a focused, polished **personal-telemetry tool** while the flag is off, and the
full product is revealed with a **single Admin toggle — no redeploy, no code change** — when
the credentials arrive (that reveal is Milestone M2).

This builds on infrastructure that already exists: the `FeatureFlag` entity,
`FeatureFlagsController` (`/api/feature-flags`), `FeatureFlagProvider` + `useFeatureFlag` /
`isEnabled`, and the Admin → Feature Flags CRUD screen. M1 adds **one flag and the gating
around it** — it invents no new mechanism.

## Non-goals

- Bringing iRacing live data online (that is M2, blocked on service-account creds).
- "Sign in with iRacing" user OAuth (that is M3, blocked on a registered OAuth client).
- A public/anonymous flag-read path so **guests** see real content at GA (deferred to M2 —
  see "Deferred" below).
- Running the seeder against the production DB (explicitly out of scope — the seeder is for
  local Docker dev only).

---

## The flag

- **Key:** `iracing-live` (one master flag).
- **Seeded values:** `IsEnabled = false`, `MinimumRole = Admin`,
  `Name = "Live iRacing data"`, a short `Description`.
- **Role semantics** (existing hierarchy in `AdminService.RoleHierarchy`):
  `Standard(0) < Beta(1) < Alpha(2) < Admin(3)`. A flag is returned to a user when
  `IsEnabled && RoleHierarchy[MinimumRole] <= RoleHierarchy[userRole]`.
  - **Off (M1 default):** nobody resolves the flag → all gated surfaces show ComingSoon /
    are dropped from nav.
  - **Privileged preview in prod:** set `IsEnabled = true, MinimumRole = Alpha` → only
    Alpha/Admin signed-in accounts see the real product; everyone else still sees
    ComingSoon.
  - **GA (M2):** drop `MinimumRole` to `Standard`.
- **Future split (not now):** once partial creds arrive this can split into `iracing-data`
  and `iracing-sign-in`. One flag is correct while both cred types are missing.

---

## Backend design (`ApexRacers.Api` / `ApexRacers.Data`)

### Flag seeding — EF migration `InsertData`

There is **no existing flag seeding to mirror**: the `AddRolesAndFeatureFlags` migration
creates an empty `FeatureFlags` table, and flags are otherwise created only via the Admin UI.
M1 chooses the migration route so the flag is **version-controlled and reproducible** across
prod, CI, and every local DB.

- New EF Core migration (target `ApexRacers.Data`, startup `ApexRacers.Api`).
- `Up()`: `migrationBuilder.InsertData` into `iracing.FeatureFlags` with the seeded values
  above and **fixed, hardcoded** `CreatedAt`/`UpdatedAt` timestamps (migrations must be
  deterministic — do not use `DateTime.UtcNow`).
- `Down()`: `migrationBuilder.DeleteData` keyed on `Key = "iracing-live"`.
- Auto-applies on deploy (the app runs migrations on startup) and via
  `dotnet ef database update` locally.

### No endpoint changes

Live endpoints already throw `IRacingNotConfiguredException` → `503` when iRacing is
unconfigured. That stays as **defense-in-depth behind the UI gate**; the frontend gate
prevents those calls from being made while the flag is off, so no `503` reaches a user.

---

## Frontend design (`src/web/`)

### `RequireFlag` route wrapper

A new layout-route guard, mirroring `AdminGuard` in `App.tsx`, but **flag-based and
auth-independent**:

```tsx
export function RequireFlag() {
  const enabled = useFeatureFlag("iracing-live");
  return enabled ? <Outlet /> : <ComingSoon />;
}
```

- It does **not** redirect to `/login` — it renders `ComingSoon` for everyone (guest or
  signed-in) when the flag is off, so deep links / bookmarks degrade gracefully.
- Note: the context method is **`isEnabled`** (via the `useFeatureFlag(key)` hook), not the
  `hasFlag` named in the current docs. Use the real name; fix the doc drift in `CLAUDE.md`.

### `ComingSoon` page/component

A designed card following the standard pattern (`cardStyle` box-shadow + `scanTexture`
header, cyan accent tokens `text-primary-container` etc. — **no** legacy green hexes):

- Headline: "Live iRacing analytics arriving soon."
- Short body explaining the personal-telemetry tool is available now.
- Links back to the always-on tools (Telemetry, My Laps; Dashboard).
- Rendered inside `AppShell` (so it keeps Sidebar/TopNav/Footer).

### Nav filtering

Filter `GUEST_NAV` / `AUTH_NAV` (`components/navItems.ts`) by the flag where they are
consumed (`Sidebar`, `TopNav`, and the mobile nav). When the flag is off:

- **Guest nav** → drops `/series`, leaving just `Home`.
- **Auth nav** → drops the gated entries, leaving: Dashboard, My Laps, Telemetry, Settings,
  Profile, Support.

Implementation: keep `navItems.ts` as the source of truth and filter in the consuming
components against a known set of gated paths (or tag each item with a `flag?` field). Pick
one approach in the plan; prefer a single shared gated-path predicate to avoid drift.

### Route wrapping in `App.tsx`

Wrap the gated set with `RequireFlag` (a layout route used in **both** the public group and
the `RequireAuth` group, since the gated set spans both):

- Public gated: all `/series*` (`/series`, `/series/:id/schedule`, `/standings`,
  `/weeks/:n`, `/weeks/:n/strategy`, `/weeks/:n/cars/:carId/percentile`),
  `/races/:subsessionId`, `/cars`, `/cars/:carId`, `/tracks`, `/tracks/:trackId`.
- Auth gated: `/analytics`, `/progression`, `/recommendations`, `/live`, `/races`,
  `/leaderboards`, `/compare`.

Routes that stay ungated: `/`, `/login`, `/forgot-password`, `/reset-password`, `/terms`,
`/privacy`, `/dashboard`, `/my-laps`, `/telemetry`, `/profile`, `/support`, `/settings`,
`/admin`.

### Dashboard & Profile degradation

Both pages keep their **local-only** content unconditionally and gate the iRacing-backed
widgets on `useFeatureFlag('iracing-live')`.

**`DashboardPage`** (audited — of 7 KPI tiles only 3 are local):

| Element                        | Data source                 | Flag off |
| ------------------------------ | --------------------------- | -------- |
| KPI: Laps recorded             | `getMyLaps` (local)         | keep     |
| KPI: Cars tracked              | `getMyLaps` (local)         | keep     |
| Card: Personal bests           | `getMyLaps` (local)         | keep     |
| KPI: Active series             | `getSeries` (worker)        | hide     |
| KPI: Best percentile           | `getMyAnalytics` (iRacing)  | hide     |
| KPI: iRating / SR / Avg finish | `getProfileStats` (iRacing) | hide     |
| Card: This week                | `getSeries` (worker)        | hide     |
| Right column: Active series    | `getSeries` (worker)        | hide     |

- The `grid-kpi` auto-fit grid reflows naturally as tiles are removed.
- When off, the 2-column body (`lg:grid-cols-[1.55fr_1fr]`) collapses to the single
  Personal-bests column (drop the right Active-series column).
- Skip the iRacing fetches (`getSeries` / `getProfileStats` / `getMyAnalytics`) when the
  flag is off so no needless calls fire.

**`ProfilePage`** — keep local series/lap stats; gate the enriched driver stats and the
achievements trophy case. (Exact widget list to be enumerated in the plan from the current
`ProfilePage.tsx`.)

### HomePage

No copy change in M1 (YAGNI — revisit if desired later).

---

## Deferred (intentional, recorded so it is not a surprise)

**Guest flag-reveal at GA.** `/api/feature-flags` is `[Authorize]`, and `FeatureFlagProvider`
only fetches flags for authenticated users (`owner == null` → no fetch), so an unauthenticated
guest always resolves `iracing-live = false`. This is **correct for M1**: while off, guests
should see ComingSoon on the public pages; and the privileged-preview path uses signed-in
Alpha/Admin accounts. The only unhandled case is letting **guests** browse the public iRacing
pages again at true GA — that requires a public/anonymous flag-read mechanism and belongs to
**M2** (which is creds-blocked regardless). No public flag endpoint is built in M1.

---

## Testing (both 85% coverage gates must stay green)

**Frontend (Vitest):**

- `RequireFlag`: renders `Outlet` when enabled, `ComingSoon` when disabled (both branches);
  does not redirect for guests.
- `ComingSoon`: renders headline + back-links.
- Nav filtering: gated items present when on, absent when off, for both guest and auth nav.
- `DashboardPage`: local widgets always present; iRacing widgets present when on, absent when
  off; no iRacing fetch fired when off.
- `ProfilePage`: local stats always present; gated widgets present/absent by flag.

**Backend (xUnit):**

- The migration is data-only. If useful for branch coverage, assert via `AdminService`
  (e.g. a seeded-flag fixture resolves for an Admin and not for a Standard user) — but the
  primary verification is that the migration applies and the row exists.

---

## Definition of done

- With `iracing-live` **off**: every route is functional or shows `ComingSoon` — no `500`s,
  no blank pages, no infinite spinners, no `503`s reaching the user; nav shows only the
  working items.
- With `iracing-live` **on** (verified locally via the seeder or real creds, or by flipping
  the flag with seeded data): the full product returns exactly as it is today.
- Both coverage gates green; `npx prettier --check .` and `npm run lint` pass.
- Docs updated: `CLAUDE.md` (routing table, nav list, feature-flags section, `isEnabled` vs
  `hasFlag` fix), `private/ROADMAP.md` (tick M1), `private/PRD.md`.

---

## Affected files (anticipated)

**Backend**

- `src/ApexRacers.Data/Migrations/*_SeedIracingLiveFlag.cs` (new)
- `src/ApexRacers.Data/Migrations/AppDbContextModelSnapshot.cs` (regenerated)

**Frontend**

- `src/web/src/App.tsx` (RequireFlag, route wrapping)
- `src/web/src/pages/ComingSoonPage.tsx` (new) + test
- `src/web/src/components/navItems.ts` (gated-path tagging/predicate)
- `src/web/src/components/Sidebar.tsx`, `TopNav.tsx`, mobile nav (filtering)
- `src/web/src/pages/DashboardPage.tsx`, `ProfilePage.tsx` (degradation)
- Corresponding `__tests__` files

**Docs**

- `CLAUDE.md`, `private/ROADMAP.md`, `private/PRD.md`
