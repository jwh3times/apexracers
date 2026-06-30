# Playwright Accessibility (axe-core) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate `@axe-core/playwright` into the `web/e2e` suite so every Playwright run asserts zero WCAG 2.1 A/AA accessibility violations across a focused set of pages that render real content in CI.

**Architecture:** A thin shared helper (`web/e2e/helpers/a11y.ts`) wraps `AxeBuilder`, runs the WCAG 2.1 A/AA tagset, and asserts an empty `violations` array with a readable, actionable failure message. A single data-driven spec (`web/e2e/a11y.spec.ts`) loops public pages (no auth) and authenticated pages (one registration via the existing `registerNewUser` helper). Existing violations are fixed in the app; anything too large to fix now is allowlisted with a documented, narrowly-scoped exception and a tracked follow-up.

**Tech Stack:** TypeScript, Playwright (`@playwright/test`), `@axe-core/playwright` (wraps `axe-core`), the existing ASP.NET API + Postgres serving the built SPA on `http://localhost:8080`.

## Global Constraints

- **WCAG tagset (exact, verbatim):** `['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']`.
- **Scope is Task 3 (accessibility) only.** Do **not** add visual regression / `toHaveScreenshot()` (that is the deferred Task 4).
- **No CI workflow change.** The new spec runs under the existing `npx playwright test` step in `.github/workflows/e2e.yml`, which stays **non-blocking**. Do not promote it to a required check.
- **Allowlist comment format (exact):** every `disableRules`/`exclude` use carries `// KNOWN-A11Y(<rule-id>): <reason> — follow-up: <where-tracked>` at the call site, plus a matching backlog bullet in `private/ROADMAP.md`.
- **Coverage gate untouched:** `web/e2e/**` is outside Vitest's `include`/`coverage.include` (both `src/**` in `web/vite.config.ts`), so the 85% frontend gate must remain unaffected. Any **app** fix in Task 3 must keep existing Vitest suites green at ≥85%.
- **Formatting/lint:** all new/changed `web` files must pass `npx prettier --check .` and `npm run lint` (CI gates both).
- **Local prerequisite for running e2e:** the app must be reachable at `http://localhost:8080`. Start it with `docker compose up -d` from the repo root first (Playwright's `reuseExistingServer: !CI` then attaches instead of doing a ~240s `dotnet run` boot). Ensure the `iracing-live` and `iracing-demo` feature flags are **off** (their seeded default) so `/series` renders `ComingSoonPage`, matching CI.

---

### Task 1: a11y audit helper (`auditA11y` + `formatViolations`)

Add the dependency and the shared helper, TDD'd through the pure `formatViolations` function.

**Files:**
- Modify: `web/package.json` + `web/package-lock.json` (add `@axe-core/playwright` devDependency)
- Create: `web/e2e/helpers/a11y.ts`
- Create (test): `web/e2e/a11y.spec.ts` (this task adds only the `formatViolations` unit tests; Task 2 adds the page audits to the same file)

**Interfaces:**
- Produces:
  - `auditA11y(page: Page, opts?: A11yOptions): Promise<void>` — runs axe for WCAG 2.1 A/AA and asserts zero violations.
  - `formatViolations(violations: Violation[]): string` — pure; renders violations into a readable message.
  - `type A11yOptions = { disableRules?: string[]; exclude?: string[] }`.
  - `type Violation` — a single axe violation, derived from the `AxeBuilder` result type (no extra dependency).

- [ ] **Step 1: Install the dependency**

Run (from `web/`):
```bash
npm install --save-dev @axe-core/playwright
```
Expected: `package.json` gains `"@axe-core/playwright"` under `devDependencies` and `package-lock.json` updates. (Types come transitively via `axe-core`; we derive the violation type from the builder, so no separate `axe-core` install is needed.)

- [ ] **Step 2: Write the failing unit test for `formatViolations`**

Create `web/e2e/a11y.spec.ts`:
```ts
import { test, expect } from '@playwright/test';
import { formatViolations, type Violation } from './helpers/a11y';

// Minimal fixture shaped like an axe violation — only the fields formatViolations reads.
const sampleViolation = {
  id: 'color-contrast',
  impact: 'serious',
  help: 'Elements must meet minimum color contrast ratio thresholds',
  helpUrl: 'https://dequeuniversity.com/rules/axe/4.10/color-contrast',
  nodes: [{ target: ['.kpi-value'] }],
};

test.describe('a11y helper: formatViolations', () => {
  test('summarizes rule id, impact, help URL, and node targets', () => {
    const message = formatViolations([sampleViolation] as unknown as Violation[]);
    expect(message).toContain('color-contrast');
    expect(message).toContain('serious');
    expect(message).toContain('dequeuniversity.com');
    expect(message).toContain('.kpi-value');
  });

  test('reports a clean message when there are no violations', () => {
    expect(formatViolations([])).toBe('No accessibility violations.');
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run (from `web/`, with the app up at `:8080`):
```bash
npx playwright test a11y.spec.ts -g "formatViolations"
```
Expected: FAIL — `Cannot find module './helpers/a11y'` (the helper does not exist yet).

- [ ] **Step 4: Implement the helper**

Create `web/e2e/helpers/a11y.ts`:
```ts
import AxeBuilder from '@axe-core/playwright';
import { expect, type Page } from '@playwright/test';

/** WCAG 2.1 Level A & AA rule tags — the conformance target for this suite. */
const WCAG_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

/** A single axe violation, derived from the AxeBuilder result so we need no extra dependency. */
export type Violation = Awaited<ReturnType<AxeBuilder['analyze']>>['violations'][number];

export type A11yOptions = {
  /** axe rule ids to skip (allowlist). Document every entry with a KNOWN-A11Y comment. */
  disableRules?: string[];
  /** CSS selectors to exclude from the scan (allowlist). Document every entry with a KNOWN-A11Y comment. */
  exclude?: string[];
};

/**
 * Renders axe violations into a readable, actionable failure message: one block
 * per violation with rule id, impact, help URL, and the failing node selectors.
 * Pure — exported for unit testing.
 */
export function formatViolations(violations: Violation[]): string {
  if (violations.length === 0) return 'No accessibility violations.';
  const blocks = violations.map((v) => {
    const nodes = v.nodes.map((n) => `      ${JSON.stringify(n.target)}`).join('\n');
    return [`  [${v.impact ?? 'unknown'}] ${v.id} — ${v.help}`, `    ${v.helpUrl}`, nodes].join('\n');
  });
  return `${violations.length} accessibility violation(s):\n${blocks.join('\n\n')}`;
}

/**
 * Runs axe-core against the current page state for WCAG 2.1 A/AA and asserts zero
 * violations. On failure the assertion message lists every violation. Pass
 * `disableRules`/`exclude` to allowlist a known issue — always with a
 * `// KNOWN-A11Y(<rule>): <reason> — follow-up: <ref>` comment at the call site.
 */
export async function auditA11y(page: Page, opts: A11yOptions = {}): Promise<void> {
  let builder = new AxeBuilder({ page }).withTags(WCAG_TAGS);
  if (opts.disableRules?.length) builder = builder.disableRules(opts.disableRules);
  for (const selector of opts.exclude ?? []) builder = builder.exclude(selector);
  const { violations } = await builder.analyze();
  expect(violations, formatViolations(violations)).toEqual([]);
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run (from `web/`):
```bash
npx playwright test a11y.spec.ts -g "formatViolations"
```
Expected: PASS (2 tests).

- [ ] **Step 6: Lint + format the new files**

Run (from `web/`):
```bash
npx prettier --write e2e/helpers/a11y.ts e2e/a11y.spec.ts
npm run lint
```
Expected: prettier reports the files formatted; `npm run lint` passes with no errors.

- [ ] **Step 7: Commit**

```bash
git add web/package.json web/package-lock.json web/e2e/helpers/a11y.ts web/e2e/a11y.spec.ts
git commit -m "test: add axe-core a11y audit helper for Playwright e2e"
```

---

### Task 2: Page audit specs (public + authenticated)

Add the data-driven page audits to the spec created in Task 1.

**Files:**
- Modify: `web/e2e/a11y.spec.ts` (append two `test.describe` blocks)

**Interfaces:**
- Consumes: `auditA11y` from `./helpers/a11y`; `registerNewUser` from `./helpers/users`.

- [ ] **Step 1: Add the public + authenticated page audits**

Append to `web/e2e/a11y.spec.ts` (after the existing `formatViolations` describe block); add `auditA11y` and `registerNewUser` to the imports at the top:
```ts
import { auditA11y } from './helpers/a11y';
import { registerNewUser } from './helpers/users';

/** Pages that render real content with no auth in CI. */
const PUBLIC_PAGES = ['/', '/login', '/forgot-password', '/terms', '/privacy'];

/**
 * Authenticated pages that render real content without iRacing creds.
 * `/series` is flag-gated, so it renders ComingSoonPage inside the real AppShell
 * chrome — our single representative for every gated route.
 */
const AUTHED_PAGES = [
  '/dashboard',
  '/my-laps',
  '/telemetry',
  '/profile',
  '/support',
  '/settings',
  '/series',
];

/** Wait for the SPA route to render before auditing, so axe never sees a pre-mount frame. */
async function gotoAndSettle(page: import('@playwright/test').Page, path: string) {
  await page.goto(path);
  await page.locator('main, h1').first().waitFor({ state: 'visible' });
}

test.describe('accessibility: public pages (WCAG 2.1 A/AA)', () => {
  for (const path of PUBLIC_PAGES) {
    test(`${path} has no violations`, async ({ page }) => {
      await gotoAndSettle(page, path);
      await auditA11y(page);
    });
  }
});

test.describe('accessibility: authenticated pages (WCAG 2.1 A/AA)', () => {
  test('authed pages have no violations', async ({ page }) => {
    await registerNewUser(page);
    for (const path of AUTHED_PAGES) {
      await gotoAndSettle(page, path);
      await auditA11y(page);
    }
  });
});
```

- [ ] **Step 2: Run the full spec to discover the current state**

Run (from `web/`, app up at `:8080` with iRacing flags off):
```bash
npx playwright test a11y.spec.ts
```
Expected: the `formatViolations` tests PASS. The page-audit tests either PASS (app already clean) or FAIL listing real violations — **either outcome is the expected discovery result**; the failures (if any) are the input to Task 3. Note exactly which pages/rules fail from the printed `formatViolations` output.

- [ ] **Step 3: Format + lint**

Run (from `web/`):
```bash
npx prettier --write e2e/a11y.spec.ts
npm run lint
```
Expected: clean.

- [ ] **Step 4: Commit (the spec, regardless of current pass/fail)**

```bash
git add web/e2e/a11y.spec.ts
git commit -m "test: add WCAG 2.1 A/AA axe audits for public and authed pages"
```
(Committing here captures the audit harness; Task 3 lands the fixes/allowlist that turn it green.)

---

### Task 3: Triage — fix violations, allowlist the rest, go green

Drive the page-audit suite to green. The exact fixes are **discovered by running axe** (Task 2, Step 2) and cannot be enumerated up front — this task is a procedure, not a fixed edit list.

**Files:**
- Modify: app source under `web/src/**` (the specific files depend on what axe flags — e.g. a page missing a `<main>` landmark, an icon-only control missing an accessible name, a low-contrast token, `web/index.html` missing `lang`)
- Modify (only if allowlisting): `web/e2e/a11y.spec.ts` (wrap the relevant `auditA11y` call with `disableRules`/`exclude`) and `private/ROADMAP.md` (tracked follow-up)
- Modify (if markup changes): the affected component's existing `*.test.tsx` under `web/src/**`

**Interfaces:**
- Consumes: the `A11yOptions` (`disableRules`/`exclude`) surface from Task 1.

- [ ] **Step 1: Triage each violation from Task 2's output**

For every violation reported (rule `id`, `impact`, `helpUrl`, node `target` are all in the failure message), decide **fix** vs **allowlist**:
- **Fix in app** when the change is local and low-risk. Common axe rules and their typical fixes:
  - `color-contrast` → swap the offending color to a design token that meets ≥4.5:1 (normal text) / ≥3:1 (large text); never reintroduce the old greens (use `text/bg/border-primary-container` per project rules).
  - `button-name` / `link-name` → add visible text or `aria-label` to the icon-only control.
  - `label` / `form-field-multiple-labels` → associate `<label htmlFor>` with the input `id`.
  - `landmark-one-main` / `region` → ensure the page/AppShell renders a single `<main>` wrapping content.
  - `html-has-lang` → add `lang="en"` to `<html>` in `web/index.html`.
  - `image-alt` → add `alt` text (empty `alt=""` for decorative images).
- **Allowlist** only when a proper fix is genuinely large/risky right now. Wrap the specific call:
  ```ts
  // KNOWN-A11Y(color-contrast): legacy chart legend tokens; fix tracked separately.
  // follow-up: private/ROADMAP.md "A11y backlog — chart legend contrast"
  await auditA11y(page, { disableRules: ['color-contrast'] });
  ```
  Keep the scope as narrow as possible (prefer `exclude: ['<selector>']` over a blanket `disableRules` when only one region is at fault), and add a matching bullet under a new "A11y backlog" subsection in `private/ROADMAP.md`.

- [ ] **Step 2: Apply each app fix and keep its unit test green**

After editing a component's markup, update its existing `*.test.tsx` if a query/label changed, then run (from `web/`):
```bash
npx vitest run
```
Expected: PASS, coverage still ≥85% (unaffected by the e2e files).

- [ ] **Step 3: Re-run the a11y suite until green**

Run (from `web/`):
```bash
npx playwright test a11y.spec.ts
```
Expected: iterate until all tests PASS. (The authed test stops at the first failing page; fix it, re-run, continue. To see all authed-page violations at once during triage, you may temporarily comment out earlier `AUTHED_PAGES` entries — restore the full list before committing.)

- [ ] **Step 4: Format + lint everything touched**

Run (from `web/`):
```bash
npx prettier --write .
npm run lint
```
Expected: clean.

- [ ] **Step 5: Commit (fixes and allowlist together, or in logical chunks)**

```bash
git add -A
git commit -m "fix(a11y): resolve WCAG 2.1 A/AA violations on audited pages"
```
If you added any allowlist entries, use a second commit:
```bash
git add web/e2e/a11y.spec.ts private/ROADMAP.md
git commit -m "test(a11y): allowlist known issues with tracked follow-ups"
```

---

### Task 4: Final verification + documentation

Run the full gate locally and update the project docs per convention.

**Files:**
- Modify: `private/ROADMAP.md` (remove the shipped Task 3; keep Task 4 visual regression)
- Modify: `private/archive.md` (prepend completion summary, newest first)
- Modify: `CHANGELOG.md` (add `[Unreleased]` bullets)

- [ ] **Step 1: Run the full local gate**

Run (from `web/`, app up at `:8080`):
```bash
npx prettier --check .
npm run lint
npx vitest run --coverage
npx playwright test
```
Expected: prettier clean; lint clean; Vitest PASS at ≥85% all metrics; **all** Playwright tests (smoke + a11y) PASS.

- [ ] **Step 2: Update `private/ROADMAP.md`**

In the "Accessibility & visual regression (Playwright)" backlog block: remove the "Accessibility (axe-core) (Task 3)" item and update the "Current state" line to note Tasks 1–3 shipped (Task 4 visual regression remains). Add the "A11y backlog" subsection only if Task 3 created any allowlisted follow-ups.

- [ ] **Step 3: Prepend a summary to `private/archive.md`**

Add a newest-first entry, e.g.:
```markdown
## <date> — Accessibility audits (axe-core) in the E2E suite

Integrated `@axe-core/playwright` into `web/e2e`: a shared `auditA11y` helper runs the
WCAG 2.1 A/AA tagset and asserts zero violations; `a11y.spec.ts` audits the focused
real-content page set (5 public + 6 authed + 1 representative ComingSoonPage). Fixed the
violations found; any deferral is a documented, narrowly-scoped allowlist entry with a
tracked ROADMAP follow-up. Suite stays non-blocking in CI. (ROADMAP backlog Task 3.)
```

- [ ] **Step 4: Add `CHANGELOG.md` `[Unreleased]` bullets**

Under `### Added`: a bullet for the axe-core accessibility coverage. Under `### Fixed`: a bullet for any user-facing a11y fixes made in Task 3 (only if app markup/styles changed).

- [ ] **Step 5: Format, then commit the docs**

Run (from repo root) `npx prettier --write CHANGELOG.md` if needed, then:
```bash
git add private/ROADMAP.md private/archive.md CHANGELOG.md
git commit -m "docs: record axe-core a11y suite (roadmap, archive, changelog)"
```
(Note: `private/**` is gitignored, so only `CHANGELOG.md` actually enters the commit — that is expected; the `private/` edits are local working docs.)

---

## Self-Review

**Spec coverage check (each spec section → task):**
- Dependency + helper (`auditA11y`/`formatViolations`, allowlist via `disableRules`/`exclude`) → Task 1. ✓
- Page coverage (5 public + 6 authed + `/series` representative) → Task 2 (`PUBLIC_PAGES`/`AUTHED_PAGES`). ✓
- Settle-before-audit to avoid mid-render flakes → Task 2 (`gotoAndSettle`). ✓
- Two-phase build-then-triage; fix + documented allowlist + tracked follow-up → Tasks 2 (build) & 3 (triage). ✓
- No CI workflow change / stays non-blocking → Global Constraints; no task edits `.github/workflows/e2e.yml`. ✓
- Vitest coverage scope unaffected → Global Constraints + Task 4 Step 1 verification. ✓
- prettier + lint gates → Steps in every task. ✓
- Docs updates (ROADMAP/archive/CHANGELOG) → Task 4. ✓

**Placeholder scan:** No "TBD"/"add appropriate…". Task 3 is intentionally procedural because its inputs are discovered at runtime — it ships concrete rule→fix mappings and exact allowlist syntax, not hand-waving. ✓

**Type consistency:** `formatViolations(violations: Violation[])`, `auditA11y(page, opts?: A11yOptions)`, and `type Violation` are defined in Task 1 and consumed unchanged in Tasks 2–3. The Task 1 test imports `Violation` from the helper; the helper exports it. `gotoAndSettle` is local to Task 2's file. ✓
