# Playwright E2E — Thin Vertical Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a Playwright end-to-end harness for the ApexRacers web app with one smoke test (landing → register → dashboard, session persists) running against the full real stack, plus a non-blocking per-PR CI workflow.

**Architecture:** Single-origin topology (Approach A) — the .NET API serves the built React SPA from `wwwroot` and exposes `/api`, exactly as production does. Locally, Playwright attaches to `docker compose up` on `:8080`; in CI, Playwright's `webServer` launches the API (which self-migrates on boot) against a Postgres service container. No proxy, no CORS, no iRacing creds, no catalog seeding.

**Tech Stack:** Playwright (`@playwright/test`), Vite 8 / Vitest 4 (existing), React 19, .NET 10 API, PostgreSQL 18, GitHub Actions.

## Global Constraints

- **Frontend root is `web/`** (not `src/web/`). All Playwright files live under `web/`.
- **Node `>=26`**, **.NET `10.x`**, **Postgres `18-alpine`** (match existing `deploy.yml` / `docker-compose.yml`).
- **Prettier gates CI:** `npx prettier --check .` runs over the whole `web/` tree — every new file must be Prettier-formatted (2-space indent, single quotes, trailing commas, semicolons).
- **Package versions:** add deps with `npm install -D` from `web/`; never hand-edit version pins.
- **Identity password policy:** length ≥ 8, must contain a digit, an uppercase, and a lowercase letter; non-alphanumeric NOT required. Test password `ApexRacer123` satisfies this.
- **API base URL is same-origin `/api`** — the SPA calls relative paths, so serving it from the API origin needs no proxy.
- **Commit messages** end with: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- **Branch:** all work lands on `feat/playwright-e2e` (already created; the design spec is committed there).

---

## File Structure

| File | Responsibility | Action |
| --- | --- | --- |
| `web/playwright.config.ts` | Playwright config: testDir, baseURL, single Chromium project, retries/trace, `webServer` (reuse local / launch API in CI) | Create |
| `web/e2e/helpers/users.ts` | `uniqueEmail()`, `TEST_PASSWORD`, `registerNewUser(page)` | Create |
| `web/e2e/smoke.spec.ts` | The one smoke test | Create |
| `web/package.json` | Add `@playwright/test` devDep + `test:e2e` / `test:e2e:ui` scripts | Modify |
| `web/vite.config.ts` | Scope Vitest `test.include` to `src/**` so it ignores `e2e/` | Modify |
| `web/eslint.config.js` | Add a config block giving `e2e/**` + `playwright.config.ts` node globals and disabling the react-refresh rule | Modify |
| `.gitignore` | Ignore `web/playwright-report/`, `web/test-results/`, `web/blob-report/` | Modify |
| `.github/workflows/e2e.yml` | New non-blocking E2E workflow (Postgres service + SPA build + Playwright) | Create |

---

## Task 1: Playwright harness + smoke test (local green)

Deliverable: `npm run test:e2e` passes locally against `docker compose up`, the existing `npm test` (Vitest) still passes and ignores `e2e/`, and `npm run lint` + `npx prettier --check .` pass.

**Files:**
- Create: `web/playwright.config.ts`
- Create: `web/e2e/helpers/users.ts`
- Create: `web/e2e/smoke.spec.ts`
- Modify: `web/package.json`
- Modify: `web/vite.config.ts`
- Modify: `web/eslint.config.js`
- Modify: `.gitignore`

**Interfaces:**
- Produces: `uniqueEmail(prefix?: string): string`, `TEST_PASSWORD: string`, `registerNewUser(page: Page): Promise<string>` (returns the email used; leaves the page authenticated on `/dashboard`) — exported from `web/e2e/helpers/users.ts`.
- Consumes: the running app at `baseURL` (`http://localhost:8080`), served by `docker compose up` locally.

**Prerequisite for local verification:** Docker Desktop running, and from the repo root `docker compose up -d` (Postgres + API on `:8080`). Confirm `http://localhost:8080/` returns the SPA before running the suite.

- [ ] **Step 1: Install the Playwright test package**

Run (from `web/`):
```bash
npm install -D @playwright/test
```
Expected: `@playwright/test` appears under `devDependencies` in `web/package.json` and `package-lock.json` updates. (Browser binaries are installed in a later step / by CI.)

- [ ] **Step 2: Install the Chromium browser binary locally**

Run (from `web/`):
```bash
npx playwright install chromium
```
Expected: Chromium downloads succeed ("chromium … downloaded").

- [ ] **Step 3: Add E2E scripts to `web/package.json`**

In the `"scripts"` block, add these two entries (place them after `"test:watch"`):
```json
    "test:e2e": "playwright test",
    "test:e2e:ui": "playwright test --ui",
```
Expected: `npm run test:e2e` is now a defined script (it will fail until config + a test exist — that's fine).

- [ ] **Step 4: Scope Vitest to `src/` so it ignores `e2e/`**

In `web/vite.config.ts`, add an `include` line to the `test` block (Vitest's default include would otherwise pick up `e2e/*.spec.ts` and crash on the Playwright imports). The `test` block becomes:
```ts
    test: {
      environment: 'jsdom',
      globals: true,
      setupFiles: ['./src/test/setup.ts'],
      include: ['src/**/*.{test,spec}.{ts,tsx}'],
      coverage: {
        provider: 'v8',
        include: ['src/**/*.{ts,tsx}'],
        exclude: ['src/test/**', 'src/main.tsx'],
        thresholds: {
          lines: 85,
          functions: 85,
          branches: 85,
          statements: 85,
        },
      },
    },
```

- [ ] **Step 5: Verify Vitest still passes and does not touch `e2e/`**

Run (from `web/`):
```bash
npm test
```
Expected: PASS, same suite count as before this task. (No `e2e/` files exist yet, but this locks the include scope in before they do.)

- [ ] **Step 6: Create the Playwright config**

Create `web/playwright.config.ts`:
```ts
import { defineConfig, devices } from '@playwright/test';

const PORT = 8080;
const baseURL = `http://localhost:${PORT}`;

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: process.env.CI ? [['html', { open: 'never' }], ['list']] : 'list',
  use: {
    baseURL,
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    // Locally, reuseExistingServer attaches to `docker compose up` on :8080 and this
    // command is never run. In CI nothing is on :8080, so Playwright launches the API,
    // which serves the SPA from wwwroot and self-migrates against the Postgres service.
    command:
      'dotnet run --configuration Release --no-launch-profile --project src/ApexRacers.Api',
    cwd: '..',
    url: baseURL,
    timeout: 240_000,
    reuseExistingServer: true,
    stdout: 'pipe',
    stderr: 'pipe',
  },
});
```

- [ ] **Step 7: Create the test helpers**

Create `web/e2e/helpers/users.ts`:
```ts
import { expect, type Page } from '@playwright/test';

/** A unique, valid email per call so tests never collide on the shared dev DB. */
export function uniqueEmail(prefix = 'apex-e2e'): string {
  const stamp = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`;
  return `${prefix}-${stamp}@example.com`;
}

/** Meets the API Identity policy: length >= 8, with a digit, an uppercase, and a lowercase. */
export const TEST_PASSWORD = 'ApexRacer123';

/**
 * Registers a brand-new account through the UI and lands authenticated on the dashboard.
 * Self-navigates to /login, so callers may call it from any page. Returns the email used.
 */
export async function registerNewUser(page: Page): Promise<string> {
  const email = uniqueEmail();

  await page.goto('/login');
  await page.getByRole('tab', { name: 'Create Account' }).click();
  await page.getByLabel('Email Address').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(TEST_PASSWORD);
  await page.getByLabel('Confirm Password').fill(TEST_PASSWORD);
  await page.getByRole('button', { name: 'Create Account' }).click();

  await expect(page).toHaveURL(/\/dashboard$/);
  await expect(page.getByRole('heading', { level: 1, name: /welcome back/i })).toBeVisible();

  return email;
}
```

- [ ] **Step 8: Create the smoke test**

Create `web/e2e/smoke.spec.ts`:
```ts
import { test, expect } from '@playwright/test';
import { registerNewUser } from './helpers/users';

test.describe('smoke: register -> dashboard', () => {
  test('landing renders, new user can register, session persists across reload', async ({
    page,
  }) => {
    // 1. Landing page renders (hero h1; level:1 avoids colliding with the section h2).
    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1, name: /win races/i })).toBeVisible();

    // 2. The "Sign in" CTA routes to the auth page (exact match: avoid substring collisions).
    await page.getByRole('link', { name: 'Sign in', exact: true }).click();
    await expect(page).toHaveURL(/\/login$/);

    // 3. Register a fresh user -> lands authenticated on the dashboard.
    //    (registerNewUser self-navigates to /login, which is harmless here.)
    await registerNewUser(page);

    // 4. Reload: the session rehydrates from IndexedDB; no bounce to /login.
    await page.reload();
    await expect(page).toHaveURL(/\/dashboard$/);
    await expect(page.getByRole('heading', { level: 1, name: /welcome back/i })).toBeVisible();
  });
});
```

- [ ] **Step 9: Add a lint config block for the E2E files**

In `web/eslint.config.js`, add a new config object to the exported array, AFTER the existing `{ files: ['**/*.{ts,tsx}'], … }` block (so it overrides for these paths). The file becomes:
```js
import js from '@eslint/js';
import globals from 'globals';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import tseslint from 'typescript-eslint';
import { defineConfig, globalIgnores } from 'eslint/config';
import eslintConfigPrettier from 'eslint-config-prettier';

export default defineConfig([
  globalIgnores(['dist', 'coverage', 'playwright-report', 'test-results', 'blob-report']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
      eslintConfigPrettier,
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  {
    // E2E + Playwright config run under Node, not the browser/React refresh model.
    files: ['e2e/**/*.ts', 'playwright.config.ts'],
    languageOptions: {
      globals: globals.node,
    },
    rules: {
      'react-refresh/only-export-components': 'off',
    },
  },
]);
```

- [ ] **Step 10: Ignore Playwright output artifacts**

In `.gitignore`, under the `# Node / Vite` section (after `web/dist/`), add:
```
web/playwright-report/
web/test-results/
web/blob-report/
```

- [ ] **Step 11: Format the new/changed files**

Run (from `web/`):
```bash
npx prettier --write "playwright.config.ts" "e2e/**/*.ts" "vite.config.ts" "eslint.config.js" "package.json"
```
Then verify the whole tree:
```bash
npx prettier --check .
```
Expected: "All matched files use Prettier code style!"

- [ ] **Step 12: Run lint**

Run (from `web/`):
```bash
npm run lint
```
Expected: PASS, no errors. (If `process`/node globals are flagged in `e2e/` or `playwright.config.ts`, the Step-9 block is missing or misordered — fix it.)

- [ ] **Step 13: Run the smoke test (red/green against the real stack)**

Ensure the stack is up (from repo root): `docker compose up -d`, and confirm `http://localhost:8080/` serves the SPA.
Run (from `web/`):
```bash
npm run test:e2e
```
Expected: `1 passed`. If a selector mismatches (e.g. tab/label text changed), the trace and `npm run test:e2e:ui` show the failing step — fix the selector in `users.ts`/`smoke.spec.ts`, not the app.

- [ ] **Step 14: Confirm Vitest is still green and ignores `e2e/`**

Run (from `web/`):
```bash
npm test
```
Expected: PASS, unchanged suite count; no attempt to run `e2e/smoke.spec.ts`.

- [ ] **Step 15: Commit**

Run (from repo root):
```bash
git add web/playwright.config.ts web/e2e/ web/package.json web/package-lock.json web/vite.config.ts web/eslint.config.js .gitignore
git commit -m "$(cat <<'EOF'
test(e2e): add Playwright harness + register->dashboard smoke test

Single-origin E2E against the real stack: landing renders, a new user
registers through the UI and lands on the dashboard, and the session
persists across a reload. Scopes Vitest to src/ so it ignores e2e/, and
gives the e2e/playwright files a Node-globals lint block.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Non-blocking E2E CI workflow

Deliverable: `.github/workflows/e2e.yml` runs on PRs and `workflow_dispatch`, brings up Postgres + builds the SPA into the API's `wwwroot` + runs Playwright (which launches the self-migrating API), and uploads the HTML report. It is NOT added to required status checks.

**Files:**
- Create: `.github/workflows/e2e.yml`

**Interfaces:**
- Consumes: `web/package.json` scripts and `web/playwright.config.ts` from Task 1 (the `webServer` command launches `dotnet run … src/ApexRacers.Api` with `cwd: '..'`, inheriting the job's env vars).
- Produces: a `playwright-report` workflow artifact.

- [ ] **Step 1: Create the workflow file**

Create `.github/workflows/e2e.yml`:
```yaml
name: E2E

on:
  pull_request:
    branches: [main]
  workflow_dispatch:

# Non-blocking for now: runs on PRs for signal but is intentionally NOT a required
# status check. Promote to required once the suite proves stable.
permissions:
  contents: read

jobs:
  e2e:
    name: Playwright E2E
    runs-on: ubuntu-latest

    services:
      postgres:
        image: postgres:18-alpine
        env:
          POSTGRES_DB: apexracers
          POSTGRES_USER: apexracers
          POSTGRES_PASSWORD: postgres
        ports:
          - 5432:5432
        options: >-
          --health-cmd "pg_isready -U apexracers"
          --health-interval 5s
          --health-timeout 5s
          --health-retries 10

    env:
      ASPNETCORE_ENVIRONMENT: Development
      ASPNETCORE_URLS: http://localhost:8080
      DATABASE_CONNECTION_STRING: 'Host=localhost;Port=5432;Database=apexracers;Username=apexracers;Password=postgres'
      JWT_SIGNING_KEY: ci-e2e-signing-key-not-a-secret-0123456789abcdef

    steps:
      - uses: actions/checkout@v7

      - name: Setup Node
        uses: actions/setup-node@v6
        with:
          node-version: '26'
          cache: 'npm'
          cache-dependency-path: web/package-lock.json

      - name: Setup .NET
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: '10.x'

      - name: Install frontend dependencies
        run: npm ci
        working-directory: web

      - name: Build SPA into API wwwroot
        run: |
          npm run build
          rm -rf ../src/ApexRacers.Api/wwwroot
          mkdir -p ../src/ApexRacers.Api/wwwroot
          cp -r dist/. ../src/ApexRacers.Api/wwwroot/
        working-directory: web

      - name: Install Playwright browser
        run: npx playwright install --with-deps chromium
        working-directory: web

      - name: Run E2E tests
        run: npx playwright test
        working-directory: web

      - name: Upload Playwright report
        if: ${{ !cancelled() }}
        uses: actions/upload-artifact@v4
        with:
          name: playwright-report
          path: web/playwright-report/
          retention-days: 7
```

- [ ] **Step 2: Validate the workflow YAML locally**

Run (from repo root, if `actionlint` is available; otherwise skip):
```bash
actionlint .github/workflows/e2e.yml
```
Expected: no output (valid). If `actionlint` is not installed, visually confirm: 2-space indent, the `services.postgres.options` health flags, and that `env` keys match the connection string.

- [ ] **Step 3: Format check**

Run (from `web/`):
```bash
npx prettier --check .
```
Expected: PASS. (The workflow file is outside `web/`; if a root Prettier config also covers `.github/`, run `npx prettier --check .github/workflows/e2e.yml` from repo root and format if needed.)

- [ ] **Step 4: Commit**

Run (from repo root):
```bash
git add .github/workflows/e2e.yml
git commit -m "$(cat <<'EOF'
ci(e2e): add non-blocking Playwright E2E workflow

Runs on PRs and workflow_dispatch against a Postgres service: builds the
SPA into the API wwwroot, then Playwright launches the self-migrating API
and runs the smoke suite. Uploads the HTML report. Not a required check yet.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 5: Push and verify the workflow runs green on a PR**

Run (from repo root):
```bash
git push -u origin feat/playwright-e2e
```
Then open a PR to `main` and confirm the **E2E** workflow runs and passes (it appears as a check but is not required). If it fails, download the `playwright-report` artifact to inspect the trace.

---

## Notes for the implementer

- **Don't fix selector failures by editing the app.** The smoke test asserts existing UI (`Create Account` tab/button, `Email Address` / `Password` / `Confirm Password` labels, the `/dashboard` "Welcome back" heading, the landing "win races" heading, the "Sign in" link). If one mismatches, the app changed — update the test selector.
- **`getByLabel('Password', { exact: true })`** is deliberate: a non-exact match would also match "Confirm Password".
- **`getByRole('button', { name: 'Create Account' })`** disambiguates the submit button from the same-named tab (`role="tab"`).
- **Local prerequisite:** the suite needs `:8080` serving the app. `reuseExistingServer: true` means it attaches to `docker compose up`; without Docker it would try to `dotnet run` locally and need DB env vars — so run `docker compose up` first.
- **After this lands:** update `private/ROADMAP.md` (move the e2e item's first workstream to "in progress / partially done"), prepend to `private/archive.md`, and add a `CHANGELOG.md` `[Unreleased]` bullet — per the `docs-updater` matrix. The remaining workstreams (broader auth, telemetry, catalog, ComingSoon gating, axe-core a11y, visual regression, and promoting the workflow to required) stay in the backlog.
