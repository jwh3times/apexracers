# M1 — Pre-iRacing Launch Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gate every iRacing-data-dependent surface behind one `iracing-live` feature flag so the deployed app presents as a polished personal-telemetry tool until the iRacing service-account credentials arrive, at which point a single Admin toggle reveals the full product.

**Architecture:** Reuse the existing feature-flag stack (`FeatureFlag` entity, `/api/feature-flags`, `FeatureFlagProvider` + `useFeatureFlag`, Admin flag CRUD). Add one seeded flag (`iracing-live`, disabled) via an EF migration, plus a flag-based `RequireFlag` route wrapper that renders a new `ComingSoon` page, flag-filtered navigation, and conditional degradation of the Dashboard/Profile pages. No new mechanism is invented.

**Tech Stack:** .NET 10 / EF Core 10 / Npgsql (backend); React 19 + TypeScript + React Router + Vite + Vitest + Tailwind (frontend).

## Global Constraints

- **Accent color is cyan, never green.** Use `text-primary-container` / `bg-primary-container` / `border-primary-container` / `primary-fixed-dim`. Never the literal hexes `#00FF88` / `#00e479`, nor green RGBA glows. Accent glows use `rgba(0,224,255,…)` or `var(--color-primary-container)`.
- **Fluid design utilities only** — use the `text-*-fluid`, `card-r`, `card-p`, `card-hp`, `kpi-p`, `page-wrap`, `grid-kpi`, `btn-fluid*` classes from `src/web/src/index.css`. Do not introduce Tailwind responsive breakpoints for sizing.
- **Standard card pattern** — reuse the `cardStyle` box-shadow constant and `scanTexture` header background (defined inline per page; copy the exact constants from `DashboardPage.tsx`).
- **Frontend coverage gate: 85%** across statements/branches/functions/lines (`src/web/vite.config.ts`). Every new source file needs tests; degraded pages need both on/off-flag tests.
- **Backend coverage gate: 85%** line AND branch (controllers and migrations excluded — migrations are not executed by `dotnet test`, which builds the SQLite schema from the model).
- **Formatting/lint required by CI:** from `src/web/`, `npx prettier --write .` then `npx prettier --check .` must pass, and `npm run lint` must pass.
- **Flag key is exactly `iracing-live`** (one master flag). Seeded `IsEnabled=false`, `MinimumRole=Admin`.
- **Feature-flag context method is `isEnabled`**, consumed via the hook `useFeatureFlag(key: string): boolean` from `src/web/src/context/FeatureFlagContext.tsx`. (The docs' `hasFlag` name is stale — fixed in Task 8.)
- **NuGet versions** are centrally managed in `Directory.Packages.props`; never add `Version=` to a `.csproj`.
- **EF migration commands** target the Data project with the Api startup project:
  `dotnet ef migrations add <Name> --project src/ApexRacers.Data --startup-project src/ApexRacers.Api`.

**The gated set (single source of truth for this plan).** These nav paths and routes require `iracing-live`:

```
/series          /series/:id/schedule   /series/:id/standings
/series/:id/weeks/:n   /series/:id/weeks/:n/strategy
/series/:id/weeks/:n/cars/:carId/percentile
/races/:subsessionId   /analytics   /progression   /recommendations
/live   /races   /leaderboards   /compare   /cars   /cars/:carId
/tracks   /tracks/:trackId
```

Nav-item `to` values that are gated (subset of the above that appear in `navItems.ts`):
`/series`, `/analytics`, `/progression`, `/recommendations`, `/live`, `/races`, `/leaderboards`, `/compare`, `/cars`, `/tracks`.

Routes/nav that stay **ungated**: `/`, `/login`, `/forgot-password`, `/reset-password`, `/terms`, `/privacy`, `/dashboard`, `/my-laps`, `/telemetry`, `/profile`, `/support`, `/settings`, `/admin`.

---

### Task 1: Seed the `iracing-live` feature flag (EF migration)

**Files:**
- Create: `src/ApexRacers.Data/Migrations/<timestamp>_SeedIracingLiveFlag.cs`
- Modify: `src/ApexRacers.Data/Migrations/AppDbContextModelSnapshot.cs` (regenerated automatically by `migrations add` — leave as generated)

**Interfaces:**
- Consumes: nothing.
- Produces: a `FeatureFlags` row with `Key = "iracing-live"`. Later tasks (frontend) reference this key string but do not import anything from this task.

- [ ] **Step 1: Generate an empty migration**

There is no model change, so this produces a migration with empty `Up`/`Down` to fill in.

Run:
```bash
dotnet ef migrations add SeedIracingLiveFlag --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
```
Expected: a new `*_SeedIracingLiveFlag.cs` file is created with empty `Up(...)`/`Down(...)` bodies, and the snapshot is regenerated (no diff because there is no model change).

- [ ] **Step 2: Fill in `Up`/`Down` with deterministic seed data**

Edit the generated `*_SeedIracingLiveFlag.cs` so its body reads exactly (keep the auto-generated namespace, `#nullable disable`, and class name):

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedIracingLiveFlag : Migration
    {
        // Fixed timestamp — migrations must be deterministic (no DateTime.UtcNow).
        private static readonly DateTime SeededAt = new(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "iracing",
                table: "FeatureFlags",
                columns: new[] { "Key", "Name", "Description", "IsEnabled", "MinimumRole", "CreatedAt", "UpdatedAt" },
                values: new object[]
                {
                    "iracing-live",
                    "Live iRacing data",
                    "Reveals every iRacing-data-backed surface (series, analytics, leaderboards, catalog, live race data). Off until the iRacing service-account credentials are configured.",
                    false,
                    "Admin",
                    SeededAt,
                    SeededAt,
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "iracing",
                table: "FeatureFlags",
                keyColumn: "Key",
                keyValue: "iracing-live");
        }
    }
}
```

Notes: `Id` is omitted so Postgres' identity-by-default column generates it. `Key`/`Name`/`MinimumRole` are non-null `text`; `Description` is nullable but seeded with copy. `IsEnabled=false` and `MinimumRole=Admin` mean nobody resolves the flag until an admin flips it.

- [ ] **Step 3: Verify the solution builds and the migration is registered**

Run:
```bash
dotnet build
dotnet ef migrations list --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
```
Expected: build succeeds; the list ends with `SeedIracingLiveFlag` (no `(Pending)` ambiguity issues).

- [ ] **Step 4: Apply against the local Docker database and confirm the row**

Run (Docker Postgres must be up: `docker compose up -d`):
```bash
dotnet ef database update --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
echo "SELECT \"Key\", \"IsEnabled\", \"MinimumRole\" FROM iracing.\"FeatureFlags\" WHERE \"Key\"='iracing-live';" | docker compose exec -T postgres psql -U apexracers -d apexracers
```
Expected: one row — `iracing-live | f | Admin`.

- [ ] **Step 5: Commit**

```bash
git add src/ApexRacers.Data/Migrations/
git commit -m "feat(flags): seed disabled iracing-live feature flag via migration"
```

---

### Task 2: Add gated-path predicate and `visibleNav` helper to `navItems.ts`

**Files:**
- Modify: `src/web/src/components/navItems.ts`
- Test: `src/web/src/components/__tests__/navItems.test.ts` (create)

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `export type NavItem = { readonly to: string; readonly label: string; readonly icon: string; readonly exact?: boolean }`
  - `export const GATED_NAV_PATHS: ReadonlySet<string>`
  - `export function visibleNav(items: readonly NavItem[], iracingLive: boolean): readonly NavItem[]`

- [ ] **Step 1: Write the failing test**

Create `src/web/src/components/__tests__/navItems.test.ts`:

```ts
import { describe, it, expect } from 'vitest';
import { GUEST_NAV, AUTH_NAV, visibleNav } from '../navItems';

describe('visibleNav', () => {
  it('returns all items unchanged when iracing-live is on', () => {
    expect(visibleNav(AUTH_NAV, true)).toEqual(AUTH_NAV);
    expect(visibleNav(GUEST_NAV, true)).toEqual(GUEST_NAV);
  });

  it('drops gated items from the auth nav when off, keeping the always-on tools', () => {
    const result = visibleNav(AUTH_NAV, false);
    const paths = result.map(i => i.to);
    expect(paths).toEqual([
      '/dashboard',
      '/my-laps',
      '/telemetry',
      '/settings',
      '/profile',
      '/support',
    ]);
  });

  it('drops /series from the guest nav when off, leaving only Home', () => {
    const result = visibleNav(GUEST_NAV, false);
    expect(result.map(i => i.to)).toEqual(['/']);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/components/__tests__/navItems.test.ts`
Expected: FAIL — `visibleNav` is not exported.

- [ ] **Step 3: Implement the helper**

Edit `src/web/src/components/navItems.ts`. Add an exported `NavItem` type (replace the local `type NavItem`), then append the gated set and helper. Full file:

```ts
export type NavItem = {
  readonly to: string;
  readonly label: string;
  readonly icon: string;
  readonly exact?: boolean;
};

export const GUEST_NAV: readonly NavItem[] = [
  { to: '/', label: 'Home', icon: 'home', exact: true },
  { to: '/series', label: 'Browse Series', icon: 'sports_motorsports' },
];

export const AUTH_NAV: readonly NavItem[] = [
  { to: '/dashboard', label: 'Dashboard', icon: 'dashboard', exact: true },
  { to: '/series', label: 'Browse Series', icon: 'sports_motorsports' },
  { to: '/analytics', label: 'Analytics', icon: 'analytics' },
  { to: '/progression', label: 'Progression', icon: 'trending_up' },
  { to: '/recommendations', label: 'Recommendations', icon: 'recommend' },
  { to: '/live', label: 'Race Now', icon: 'live_tv' },
  { to: '/races', label: 'Race History', icon: 'history' },
  { to: '/leaderboards', label: 'Leaderboards', icon: 'leaderboard' },
  { to: '/compare', label: 'Compare', icon: 'group' },
  { to: '/cars', label: 'Cars', icon: 'directions_car' },
  { to: '/tracks', label: 'Tracks', icon: 'route' },
  { to: '/my-laps', label: 'My Laps', icon: 'timer' },
  { to: '/telemetry', label: 'Telemetry', icon: 'sensors' },
  { to: '/settings', label: 'Settings', icon: 'settings' },
  { to: '/profile', label: 'Profile', icon: 'account_circle' },
  { to: '/support', label: 'Support', icon: 'help' },
];

// Nav paths gated behind the `iracing-live` flag (see the plan's "gated set").
export const GATED_NAV_PATHS: ReadonlySet<string> = new Set([
  '/series',
  '/analytics',
  '/progression',
  '/recommendations',
  '/live',
  '/races',
  '/leaderboards',
  '/compare',
  '/cars',
  '/tracks',
]);

// When iracing-live is off, hide the gated entries; otherwise show everything.
export function visibleNav(
  items: readonly NavItem[],
  iracingLive: boolean
): readonly NavItem[] {
  return iracingLive ? items : items.filter(i => !GATED_NAV_PATHS.has(i.to));
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npx vitest run src/components/__tests__/navItems.test.ts`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/components/navItems.ts src/web/src/components/__tests__/navItems.test.ts
git commit -m "feat(nav): add gated-path predicate and visibleNav helper"
```

---

### Task 3: Filter navigation by the flag in Sidebar, TopNav, and MobileNav

**Files:**
- Modify: `src/web/src/components/Sidebar.tsx`
- Modify: `src/web/src/components/TopNav.tsx`
- Test: `src/web/src/components/__tests__/Sidebar.test.tsx`
- Test: `src/web/src/components/__tests__/TopNav.test.tsx`

**Interfaces:**
- Consumes: `visibleNav`, `GUEST_NAV`, `AUTH_NAV` from `navItems.ts`; `useFeatureFlag` from `context/FeatureFlagContext`.
- Produces: nothing for later tasks.

- [ ] **Step 1: Update Sidebar to filter nav**

In `src/web/src/components/Sidebar.tsx`:

Change the import line:
```tsx
import { GUEST_NAV, AUTH_NAV } from './navItems';
```
to:
```tsx
import { GUEST_NAV, AUTH_NAV, visibleNav } from './navItems';
import { useFeatureFlag } from '../context/FeatureFlagContext';
```

Change the `navItems` derivation inside `Sidebar()`:
```tsx
  const { user } = useAuth();
  const navItems = user ? AUTH_NAV : GUEST_NAV;
```
to:
```tsx
  const { user } = useAuth();
  const iracingLive = useFeatureFlag('iracing-live');
  const navItems = visibleNav(user ? AUTH_NAV : GUEST_NAV, iracingLive);
```

- [ ] **Step 2: Update TopNav (and its inner MobileNav) to filter nav**

In `src/web/src/components/TopNav.tsx`:

Change the import line:
```tsx
import { GUEST_NAV, AUTH_NAV } from './navItems';
```
to:
```tsx
import { GUEST_NAV, AUTH_NAV, visibleNav } from './navItems';
import { useFeatureFlag } from '../context/FeatureFlagContext';
```

In `MobileNav()`, change:
```tsx
  const { user } = useAuth();
  const navItems = user ? AUTH_NAV : GUEST_NAV;
```
to:
```tsx
  const { user } = useAuth();
  const iracingLive = useFeatureFlag('iracing-live');
  const navItems = visibleNav(user ? AUTH_NAV : GUEST_NAV, iracingLive);
```

In `TopNav()`, change:
```tsx
  const { user } = useAuth();
  const navItems = user ? AUTH_NAV : GUEST_NAV;
```
to:
```tsx
  const { user } = useAuth();
  const iracingLive = useFeatureFlag('iracing-live');
  const navItems = visibleNav(user ? AUTH_NAV : GUEST_NAV, iracingLive);
```

- [ ] **Step 3: Add the flag mock + off-case test to Sidebar.test.tsx**

The component now calls `useFeatureFlag`; without a mock the context default returns `false`, breaking the existing tests. Add a mock defaulting to **on** and a new off-case test.

After the existing `vi.mock('../../context/AuthContext', ...)` block, add:
```tsx
let mockFlag = true;
vi.mock('../../context/FeatureFlagContext', () => ({
  useFeatureFlag: () => mockFlag,
}));
```

In the `beforeEach`, add `mockFlag = true;`:
```tsx
  beforeEach(() => {
    localStorage.clear();
    mockUser = null;
    mockFlag = true;
  });
```

Add this test inside the `describe('Sidebar', ...)` block:
```tsx
  it('hides gated nav items but keeps the always-on tools when iracing-live is off', () => {
    mockUser = LOGGED_IN;
    mockFlag = false;
    renderSidebar();
    // Always-on tools remain
    expect(screen.getByRole('link', { name: /my laps/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /telemetry/i })).toBeInTheDocument();
    // Gated items are gone
    expect(screen.queryByRole('link', { name: /browse series/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /analytics/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /leaderboards/i })).not.toBeInTheDocument();
  });
```

- [ ] **Step 4: Add the flag mock + off-case test to TopNav.test.tsx**

After the existing `vi.mock('../../context/AuthContext', ...)` block, add:
```tsx
let mockFlag = true;
vi.mock('../../context/FeatureFlagContext', () => ({
  useFeatureFlag: () => mockFlag,
}));
```

The existing `beforeEach` calls `vi.resetAllMocks()`; add `mockFlag = true;` after it:
```tsx
  beforeEach(() => {
    vi.resetAllMocks();
    mockUser = null;
    mockLogout.mockResolvedValue(undefined);
    mockFlag = true;
  });
```

Add this test inside `describe('TopNav', ...)`:
```tsx
  it('hides gated inline nav links when iracing-live is off', () => {
    mockUser = { ...baseUser };
    mockFlag = false;
    renderTopNav();
    // /analytics is gated → gone from the inline (slice(1)) links
    expect(screen.queryByRole('link', { name: /analytics/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /browse series/i })).not.toBeInTheDocument();
  });
```

- [ ] **Step 5: Run the affected tests**

Run: `cd src/web && npx vitest run src/components/__tests__/Sidebar.test.tsx src/components/__tests__/TopNav.test.tsx`
Expected: PASS (all existing + the two new tests).

- [ ] **Step 6: Commit**

```bash
git add src/web/src/components/Sidebar.tsx src/web/src/components/TopNav.tsx \
        src/web/src/components/__tests__/Sidebar.test.tsx src/web/src/components/__tests__/TopNav.test.tsx
git commit -m "feat(nav): filter sidebar and top nav by iracing-live flag"
```

---

### Task 4: Add the `ComingSoon` page

**Files:**
- Create: `src/web/src/pages/ComingSoonPage.tsx`
- Test: `src/web/src/pages/__tests__/ComingSoonPage.test.tsx`

**Interfaces:**
- Consumes: nothing.
- Produces: `export default function ComingSoonPage(): JSX.Element` — used by `RequireFlag` in Task 5.

- [ ] **Step 1: Write the failing test**

Create `src/web/src/pages/__tests__/ComingSoonPage.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, it, expect } from 'vitest';
import ComingSoonPage from '../ComingSoonPage';

function renderPage() {
  return render(
    <MemoryRouter>
      <ComingSoonPage />
    </MemoryRouter>
  );
}

describe('ComingSoonPage', () => {
  it('shows the coming-soon headline', () => {
    renderPage();
    expect(screen.getByText(/live iracing analytics arriving soon/i)).toBeInTheDocument();
  });

  it('links back to the always-on tools', () => {
    renderPage();
    expect(screen.getByRole('link', { name: /telemetry/i })).toHaveAttribute('href', '/telemetry');
    expect(screen.getByRole('link', { name: /my laps/i })).toHaveAttribute('href', '/my-laps');
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/pages/__tests__/ComingSoonPage.test.tsx`
Expected: FAIL — cannot find `../ComingSoonPage`.

- [ ] **Step 3: Implement the page**

Create `src/web/src/pages/ComingSoonPage.tsx`:

```tsx
import { Link } from 'react-router-dom';

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

export default function ComingSoonPage() {
  return (
    <main className="page-wrap">
      <div
        className="card-r border border-white/10 bg-surface overflow-hidden max-w-2xl mx-auto"
        style={cardStyle}
      >
        <div className="card-hp border-b border-white/10" style={scanTexture}>
          <p className="text-eyebrow text-primary-container">Coming soon</p>
        </div>
        <div className="card-p flex flex-col items-center gap-4 text-center py-12">
          <span
            className="material-symbols-outlined text-5xl text-primary-container"
            aria-hidden="true"
            style={{ filter: 'drop-shadow(0 0 18px rgba(0,224,255,0.35))' }}
          >
            speed
          </span>
          <h1 className="text-page-title text-on-surface">Live iRacing analytics arriving soon</h1>
          <p className="text-body-fluid text-on-surface-variant max-w-md">
            Series, leaderboards, standings, the car &amp; track catalog, and live race data are on
            the way. In the meantime your personal telemetry tools are ready to use right now.
          </p>
          <div className="flex flex-wrap items-center justify-center gap-fluid mt-2">
            <Link
              to="/telemetry"
              className="inline-flex items-center gap-2 btn-fluid border-transparent bg-primary-container text-on-primary-fixed font-semibold transition-all"
              style={{ boxShadow: '0 0 26px -8px var(--color-primary-container)' }}
            >
              <span className="material-symbols-outlined text-[17px]" aria-hidden="true">
                upload_file
              </span>
              Upload telemetry
            </Link>
            <Link
              to="/my-laps"
              className="inline-flex items-center gap-2 btn-fluid border border-line-2 bg-surface-container text-on-surface font-semibold transition-all hover:bg-surface-container-high"
            >
              <span className="material-symbols-outlined text-[17px]" aria-hidden="true">
                timer
              </span>
              My Laps
            </Link>
          </div>
        </div>
      </div>
    </main>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npx vitest run src/pages/__tests__/ComingSoonPage.test.tsx`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/pages/ComingSoonPage.tsx src/web/src/pages/__tests__/ComingSoonPage.test.tsx
git commit -m "feat(ui): add ComingSoon page for gated iRacing surfaces"
```

---

### Task 5: Add `RequireFlag` and wrap the gated routes

**Files:**
- Modify: `src/web/src/App.tsx`
- Test: `src/web/src/__tests__/guards.test.tsx`

**Interfaces:**
- Consumes: `useFeatureFlag` from `context/FeatureFlagContext`; `ComingSoonPage` from `pages/ComingSoonPage`.
- Produces: `export function RequireFlag(): JSX.Element` — a layout route that renders `<Outlet/>` when `iracing-live` is on, else `<ComingSoonPage/>`.

- [ ] **Step 1: Write the failing test**

In `src/web/src/__tests__/guards.test.tsx`, the file already mocks `../context/AuthContext`. Add a flag mock and a `RequireFlag` describe block.

After the existing `vi.mock('../context/AuthContext', ...)`, add:
```tsx
let mockFlag = true;
vi.mock('../context/FeatureFlagContext', () => ({
  useFeatureFlag: () => mockFlag,
}));
```

Add `RequireFlag` to the import from `../App`:
```tsx
import { RequireAuth, AdminGuard, RequireFlag } from '../App';
```

Append this describe block at the end of the file:
```tsx
describe('RequireFlag', () => {
  beforeEach(() => {
    mockUser = null;
    mockLoading = false;
    mockFlag = true;
  });

  it('renders the gated outlet when iracing-live is on', () => {
    mockFlag = true;
    renderGuard(<RequireFlag />, '/secret');
    expect(screen.getByText('secret content')).toBeInTheDocument();
  });

  it('renders ComingSoon (not the outlet) when iracing-live is off', () => {
    mockFlag = false;
    renderGuard(<RequireFlag />, '/secret');
    expect(screen.queryByText('secret content')).not.toBeInTheDocument();
    expect(screen.getByText(/live iracing analytics arriving soon/i)).toBeInTheDocument();
  });

  it('renders ComingSoon for a guest (no redirect to login) when off', () => {
    mockUser = null;
    mockFlag = false;
    renderGuard(<RequireFlag />, '/secret');
    expect(screen.queryByText('login page')).not.toBeInTheDocument();
    expect(screen.getByText(/live iracing analytics arriving soon/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/__tests__/guards.test.tsx`
Expected: FAIL — `RequireFlag` is not exported from `../App`.

- [ ] **Step 3: Add `RequireFlag` to App.tsx**

In `src/web/src/App.tsx`, add the imports near the other context/page imports:
```tsx
import { useFeatureFlag } from './context/FeatureFlagContext';
import ComingSoonPage from './pages/ComingSoonPage';
```

Add the guard next to `AdminGuard`:
```tsx
// Gate for routes behind the iracing-live flag. Auth-independent: renders the
// ComingSoon page for everyone (guest or signed-in) when the flag is off, so deep
// links degrade gracefully instead of 404/redirect.
export function RequireFlag() {
  const enabled = useFeatureFlag('iracing-live');
  return enabled ? <Outlet /> : <ComingSoonPage />;
}
```

- [ ] **Step 4: Wrap the gated routes**

In `AppRoutes()`, restructure the `<Route element={<AppShell />}>` subtree so the gated routes sit under `RequireFlag`. Replace the existing AppShell block body with:

```tsx
      <Route element={<AppShell />}>
        {/* Public but iRacing-data-dependent → gated behind iracing-live */}
        <Route element={<RequireFlag />}>
          <Route path="/series" element={<SeriesPage />} />
          <Route path="/series/:seriesId/schedule" element={<SchedulePage />} />
          <Route path="/series/:seriesId/standings" element={<StandingsPage />} />
          <Route path="/series/:seriesId/weeks/:weekNumber" element={<WeekDetailPage />} />
          <Route path="/series/:seriesId/weeks/:weekNumber/strategy" element={<StrategyPage />} />
          <Route
            path="/series/:seriesId/weeks/:weekNumber/cars/:carId/percentile"
            element={<PercentileCarPage />}
          />
          <Route path="/races/:subsessionId" element={<RaceDetailPage />} />
          <Route path="/cars" element={<CarsPage />} />
          <Route path="/cars/:carId" element={<CarDetailPage />} />
          <Route path="/tracks" element={<TracksPage />} />
          <Route path="/tracks/:trackId" element={<TrackDetailPage />} />
        </Route>

        {/* Everything below requires an authenticated user */}
        <Route element={<RequireAuth />}>
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route path="/my-laps" element={<MyLapsPage />} />
          <Route path="/telemetry" element={<TelemetryPage />} />
          <Route path="/profile" element={<ProfilePage />} />
          <Route path="/support" element={<SupportPage />} />
          <Route path="/settings" element={<SettingsPage key={user?.userId} />} />

          {/* Authed + iRacing-data-dependent → gated behind iracing-live */}
          <Route element={<RequireFlag />}>
            <Route path="/analytics" element={<AnalyticsPage />} />
            <Route path="/progression" element={<ProgressionPage />} />
            <Route path="/recommendations" element={<RecommendationsPage />} />
            <Route path="/races" element={<RacesPage />} />
            <Route path="/leaderboards" element={<LeaderboardsPage />} />
            <Route path="/compare" element={<ComparePage />} />
            <Route path="/live" element={<LivePage />} />
          </Route>

          <Route element={<AdminGuard />}>
            <Route path="/admin" element={<AdminPage />} />
          </Route>
        </Route>
      </Route>
```

(`/dashboard`, `/my-laps`, `/telemetry`, `/profile`, `/support`, `/settings` move out of the implicit grouping into the explicit ungated list above; behavior is unchanged for them.)

- [ ] **Step 5: Run the guard tests to verify they pass**

Run: `cd src/web && npx vitest run src/__tests__/guards.test.tsx`
Expected: PASS (existing RequireAuth/AdminGuard tests + 3 new RequireFlag tests).

- [ ] **Step 6: Commit**

```bash
git add src/web/src/App.tsx src/web/src/__tests__/guards.test.tsx
git commit -m "feat(routing): gate iRacing routes behind RequireFlag"
```

---

### Task 6: Degrade DashboardPage when the flag is off

**Files:**
- Modify: `src/web/src/pages/DashboardPage.tsx`
- Test: `src/web/src/pages/__tests__/DashboardPage.test.tsx`

**Interfaces:**
- Consumes: `useFeatureFlag` from `context/FeatureFlagContext`.
- Produces: nothing for later tasks.

- [ ] **Step 1: Add the flag mock (default on) to the existing test file**

In `src/web/src/pages/__tests__/DashboardPage.test.tsx`, after the `vi.mock('../../services/api', ...)` block add:
```tsx
let mockFlag = true;
vi.mock('../../context/FeatureFlagContext', () => ({
  useFeatureFlag: () => mockFlag,
}));
```
In the `beforeEach`, add `mockFlag = true;` as the first line so every existing test runs with the flag on (current behavior preserved).

- [ ] **Step 2: Write the failing off-case test**

Add inside `describe('DashboardPage', ...)`:
```tsx
  it('hides iRacing widgets and skips their fetches when iracing-live is off', async () => {
    mockFlag = false;
    vi.mocked(api.getMyLaps).mockResolvedValue([baseLap]);
    renderPage();
    // Local content stays
    await waitFor(() => expect(screen.getByText('Laps recorded')).toBeInTheDocument());
    expect(screen.getByText('Cars tracked')).toBeInTheDocument();
    expect(screen.getByText('Personal bests')).toBeInTheDocument();
    // iRacing widgets are gone
    expect(screen.queryByText('This week')).not.toBeInTheDocument();
    expect(screen.queryByText('Best percentile')).not.toBeInTheDocument();
    expect(screen.queryByText('iRating')).not.toBeInTheDocument();
    // iRacing fetches never fire
    expect(api.getSeries).not.toHaveBeenCalled();
    expect(api.getProfileStats).not.toHaveBeenCalled();
    expect(api.getMyAnalytics).not.toHaveBeenCalled();
  });
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/pages/__tests__/DashboardPage.test.tsx -t "iracing-live is off"`
Expected: FAIL — widgets still render and `getSeries` is called.

- [ ] **Step 4: Implement the degradation in DashboardPage.tsx**

In `src/web/src/pages/DashboardPage.tsx`:

Add the import:
```tsx
import { useFeatureFlag } from '../context/FeatureFlagContext';
```

Inside `DashboardPage()`, after `const displayName = ...`, add:
```tsx
  const iracingLive = useFeatureFlag('iracing-live');
```

Split the single `useEffect` so the iRacing fetches are guarded. Replace the existing `useEffect(() => { ... }, [])` block with:
```tsx
  useEffect(() => {
    api
      .getMyLaps()
      .then(setLaps)
      .catch(() => {})
      .finally(() => setLapsLoading(false));
  }, []);

  useEffect(() => {
    if (!iracingLive) return;
    api
      .getSeries()
      .then(setSeries)
      .catch(() => {})
      .finally(() => setSeriesLoading(false));

    api
      .getProfileStats()
      .then(setProfile)
      .catch(() => {})
      .finally(() => setProfileLoading(false));

    api
      .getMyAnalytics()
      .then(setAnalytics)
      .catch(() => {})
      .finally(() => setAnalyticsLoading(false));
  }, [iracingLive]);
```

Wrap the four gated KPI tiles — **Active series**, **Best percentile**, **iRating**, **Safety Rating**, **Avg finish** — each in `{iracingLive && ( ... )}`. (Keep **Laps recorded** and **Cars tracked** unconditional.) For each gated tile, wrap its top-level `<div className="bg-surface border border-line-2 card-r kpi-p ...">` element:
```tsx
        {iracingLive && (
          <div className="bg-surface border border-line-2 card-r kpi-p relative overflow-hidden" style={cardStyle}>
            {/* …existing tile body… */}
          </div>
        )}
```

Wrap the **This week** card (the `<div className="card-r border border-line-2 bg-surface overflow-hidden">` containing `<h3>This week</h3>`) in `{iracingLive && ( ... )}`.

Replace the two-column body wrapper so it collapses to one column when off. Change:
```tsx
      <div className="grid grid-cols-1 lg:grid-cols-[1.55fr_1fr] gap-fluid">
```
to:
```tsx
      <div className={`grid grid-cols-1 ${iracingLive ? 'lg:grid-cols-[1.55fr_1fr]' : ''} gap-fluid`}>
```

Wrap the **right column** (the `<div>` whose child is the `Active series` card — the last child of that grid) in `{iracingLive && ( ... )}`.

- [ ] **Step 5: Run the full Dashboard test file**

Run: `cd src/web && npx vitest run src/pages/__tests__/DashboardPage.test.tsx`
Expected: PASS — all existing tests (flag on) + the new off-case test.

- [ ] **Step 6: Commit**

```bash
git add src/web/src/pages/DashboardPage.tsx src/web/src/pages/__tests__/DashboardPage.test.tsx
git commit -m "feat(dashboard): degrade gracefully when iracing-live is off"
```

---

### Task 7: Degrade ProfilePage when the flag is off

**Files:**
- Modify: `src/web/src/pages/ProfilePage.tsx`
- Test: `src/web/src/pages/__tests__/ProfilePage.test.tsx`

**Interfaces:**
- Consumes: `useFeatureFlag` from `context/FeatureFlagContext`.
- Produces: nothing for later tasks.

- [ ] **Step 1: Add the flag mock (default on) to the existing test file**

In `src/web/src/pages/__tests__/ProfilePage.test.tsx`, after the `vi.mock('../../services/api', ...)` block add:
```tsx
let mockFlag = true;
vi.mock('../../context/FeatureFlagContext', () => ({
  useFeatureFlag: () => mockFlag,
}));
```
In the `beforeEach`, add `mockFlag = true;` (after `vi.resetAllMocks()`), so existing tests run with the flag on.

- [ ] **Step 2: Write the failing off-case test**

Add inside `describe('ProfilePage', ...)`:
```tsx
  it('hides iRacing sections and skips their fetches when iracing-live is off', async () => {
    mockFlag = false;
    mockGetMyLaps.mockResolvedValue(sampleLaps);
    mockGetProfileStats.mockResolvedValue(sampleProfile);
    renderPage();
    // Local content stays
    await waitFor(() => expect(screen.getByText('Personal Best by Car')).toBeInTheDocument());
    expect(screen.getByText('Porsche 911 GT3 R')).toBeInTheDocument();
    // iRacing sections are gone
    expect(screen.queryByText('Licenses')).not.toBeInTheDocument();
    expect(screen.queryByText('Trophy Case')).not.toBeInTheDocument();
    expect(screen.queryByText('Active Series')).not.toBeInTheDocument();
    // iRacing fetches never fire; local laps still load
    expect(mockGetSeries).not.toHaveBeenCalled();
    expect(mockGetProfileStats).not.toHaveBeenCalled();
    expect(mockGetAchievements).not.toHaveBeenCalled();
    expect(mockGetMyLaps).toHaveBeenCalled();
  });
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/pages/__tests__/ProfilePage.test.tsx -t "iracing-live is off"`
Expected: FAIL — sections still render and `getSeries`/`getProfileStats` are called.

- [ ] **Step 4: Implement the degradation in ProfilePage.tsx**

In `src/web/src/pages/ProfilePage.tsx`:

Add the import:
```tsx
import { useFeatureFlag } from '../context/FeatureFlagContext';
```

Inside `ProfilePage()`, after `const displayName = ...`, add:
```tsx
  const iracingLive = useFeatureFlag('iracing-live');
```

Split the first `useEffect` so `getSeries` is flag-guarded while `getMyLaps` stays unconditional. Replace:
```tsx
  useEffect(() => {
    api
      .getMyLaps()
      .then(setLaps)
      .catch(() => {})
      .finally(() => setLapsLoading(false));

    api
      .getSeries()
      .then(setSeries)
      .catch(() => {})
      .finally(() => setSeriesLoading(false));
  }, []);
```
with:
```tsx
  useEffect(() => {
    api
      .getMyLaps()
      .then(setLaps)
      .catch(() => {})
      .finally(() => setLapsLoading(false));
  }, []);

  useEffect(() => {
    if (!iracingLive) return;
    api
      .getSeries()
      .then(setSeries)
      .catch(() => {})
      .finally(() => setSeriesLoading(false));
  }, [iracingLive]);
```

Guard the profile-stats effect — change its early-return condition:
```tsx
  useEffect(() => {
    if (!linked) return;
```
to:
```tsx
  useEffect(() => {
    if (!linked || !iracingLive) return;
```
and add `iracingLive` to that effect's dependency array: `}, [linked, iracingLive]);`

Guard the achievements effect the same way — change `if (!linked) return;` to `if (!linked || !iracingLive) return;` and its deps to `[linked, iracingLive]`.

Wrap the three gated JSX blocks in `{iracingLive && ( ... )}`:
- The `<DriverStats state={...} />` line.
- The `<TrophyCase state={...} />` line.
- The entire `{/* Active series */}` `<section> ... </section>`.

For example:
```tsx
      {/* Driver stats — career, licenses, favorites */}
      {iracingLive && <DriverStats state={linked ? statsState : { status: 'not-linked' }} />}

      {/* Trophy case — earned awards/achievements */}
      {iracingLive && <TrophyCase state={linked ? achState : { status: 'hidden' }} />}

      {/* Active series */}
      {iracingLive && (
        <section>
          {/* …existing Active Series section body… */}
        </section>
      )}
```

- [ ] **Step 5: Run the full Profile test file**

Run: `cd src/web && npx vitest run src/pages/__tests__/ProfilePage.test.tsx`
Expected: PASS — all existing tests (flag on) + the new off-case test.

- [ ] **Step 6: Commit**

```bash
git add src/web/src/pages/ProfilePage.tsx src/web/src/pages/__tests__/ProfilePage.test.tsx
git commit -m "feat(profile): degrade gracefully when iracing-live is off"
```

---

### Task 8: Full verification + documentation

**Files:**
- Modify: `CLAUDE.md` (routing table, nav list, feature-flags section, `isEnabled` vs `hasFlag` fix)
- Modify: `private/ROADMAP.md` (tick M1)
- Modify: `private/PRD.md` (new section/rows + version bump)

**Interfaces:** none.

- [ ] **Step 1: Run the full frontend coverage gate**

Run: `cd src/web && npx vitest run --coverage`
Expected: PASS with all four metrics ≥ 85%. If any new file dropped a metric, add the missing-branch test before continuing.

- [ ] **Step 2: Run prettier and lint**

Run:
```bash
cd src/web && npx prettier --write . && npx prettier --check . && npm run lint
```
Expected: prettier reports all files formatted; lint exits clean.

- [ ] **Step 3: Run the backend build and tests**

Run:
```bash
dotnet build
dotnet test
```
Expected: build + all tests pass. (No new backend tests — the migration is data-only and excluded from coverage.)

- [ ] **Step 4: Update CLAUDE.md**

- In the frontend routing table, note the gated routes render `ComingSoonPage` when `iracing-live` is off, and add `/` … no new route, but add a row for the `ComingSoon` behavior under the App routes intro (one line: "Gated routes are wrapped in `RequireFlag` and render `ComingSoonPage` when `iracing-live` is off").
- In the `FeatureFlagContext` bullet, correct `hasFlag(key)` to `useFeatureFlag(key)` / `isEnabled`.
- In the Sidebar/TopNav bullets, note nav items are filtered by `iracing-live` via `visibleNav`.
- Add an `AdminGuard`-style note for `RequireFlag`.

- [ ] **Step 5: Update ROADMAP.md and PRD.md**

- `private/ROADMAP.md`: move **M1** from "▶ NEW, do now" to completed/condensed; note the `iracing-live` flag is seeded disabled and the gating shipped; record the deferred guest-reveal as an M2 task.
- `private/PRD.md`: add the `iracing-live` flag + ComingSoon surface to the relevant section and bump the version.

- [ ] **Step 6: Commit**

```bash
git add CLAUDE.md private/ROADMAP.md private/PRD.md
git commit -m "docs: record M1 iracing-live gating (routing, nav, flags)"
```

> Note: `private/` is gitignored, so the ROADMAP/PRD edits will not be staged by `git add` — make the edits for local accuracy; only `CLAUDE.md` will actually commit. That is expected.

---

## Self-Review

**Spec coverage:**
- Flag seeded disabled via EF `InsertData` → Task 1. ✅
- `RequireFlag` wrapper (auth-independent, renders ComingSoon) → Task 5. ✅
- `ComingSoon` page → Task 4. ✅
- Nav filtering (Sidebar/TopNav/MobileNav) → Tasks 2–3. ✅
- Route wrapping (public + authed gated sets) → Task 5. ✅
- Dashboard degradation (3 local widgets kept, rest gated, fetches skipped, reflow) → Task 6. ✅
- Profile degradation (DriverStats/TrophyCase/Active Series gated, fetches skipped) → Task 7. ✅
- No backend endpoint changes; 503 defense-in-depth retained → unchanged (noted, no task needed). ✅
- Guest-reveal deferred to M2 → recorded in Task 8 docs; no code. ✅
- HomePage copy skipped (YAGNI) → no task. ✅
- Both coverage gates + prettier + lint → Task 8. ✅
- Docs (`CLAUDE.md`, ROADMAP, PRD) → Task 8. ✅

**Placeholder scan:** No "TBD"/"handle edge cases"/"similar to" placeholders — every step shows the actual code or command.

**Type consistency:** `visibleNav(items, iracingLive)` and `GATED_NAV_PATHS` defined in Task 2 are consumed with matching signatures in Task 3. `RequireFlag` exported in Task 5 and imported by name in the guards test. `useFeatureFlag('iracing-live')` used consistently. The flag mock pattern (`let mockFlag = true; vi.mock('…/FeatureFlagContext', () => ({ useFeatureFlag: () => mockFlag }))` + `mockFlag = true` in `beforeEach`) is identical across Tasks 3/5/6/7.
