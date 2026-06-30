# Design: Playwright Accessibility Checks (axe-core)

**Date:** 2026-06-30
**Status:** Approved (design); pending implementation plan
**Scope:** ROADMAP backlog — "Accessibility (axe-core) (Task 3)" of the Accessibility & visual
regression workstream. **Task 4 (visual regression) is explicitly out of scope.**

---

## Goal

Automatically catch WCAG 2.1 A/AA accessibility violations on every Playwright E2E run by integrating
`@axe-core/playwright` into the existing `web/e2e` suite. The suite asserts **zero** accessibility
violations on a focused set of pages that render real content in CI, fixing the violations found and
documenting any narrowly-scoped, justified exceptions.

## Non-goals

- **Visual regression** (screenshot baselines / `toHaveScreenshot()`) — that is Task 4, deferred.
- **Making the E2E workflow a required CI check** — it stays non-blocking, consistent with the current
  `.github/workflows/e2e.yml` stance ("promote to required once the suite proves stable"). Promotion is
  a future, separate decision.
- **A full manual WCAG audit** (keyboard-trap walkthroughs, screen-reader transcripts, etc.) — automated
  axe coverage only. axe catches a meaningful subset of WCAG, not all of it.

## Context (current state)

- `web/playwright.config.ts`: single Chromium project, `baseURL` `http://localhost:8080`,
  `reuseExistingServer: !CI`, `retries: 2` in CI.
- `web/e2e/smoke.spec.ts`: one smoke test (landing → register → `/dashboard` → reload persists).
- `web/e2e/helpers/users.ts`: `registerNewUser(page)` registers a fresh account through the UI and lands
  authenticated on `/dashboard`; `uniqueEmail()` avoids collisions on the shared dev DB.
- `.github/workflows/e2e.yml`: builds the SPA into the API `wwwroot`, runs the API against a real
  Postgres service, runs `npx playwright test`, uploads the HTML report. **Non-blocking.**
- Routing (`web/src/App.tsx`): iRacing-gated routes are wrapped in `RequireFlag` and render
  `ComingSoonPage` when both `iracing-live` and `iracing-demo` are off — which is the case in CI (no
  creds, flags seeded-disabled).

## Page coverage (the focused real-content set)

Pages that render meaningful content in CI. Flag-gated routes all render the **same** `ComingSoonPage`,
so one representative (`/series`) covers them.

**Public (no auth):**
- `/` (landing / `HomePage`)
- `/login` (`LoginPage`)
- `/forgot-password` (`ForgotPasswordPage`)
- `/terms` (`TermsOfServicePage`)
- `/privacy` (`PrivacyPolicyPage`)

**Authenticated (after one `registerNewUser`):**
- `/dashboard` (`DashboardPage` — degrades gracefully without iRacing data)
- `/my-laps` (`MyLapsPage`)
- `/telemetry` (`TelemetryPage`)
- `/profile` (`ProfilePage`)
- `/support` (`SupportPage`)
- `/settings` (`SettingsPage`)
- `/series` (renders `ComingSoonPage` inside the real `AppShell` chrome — representative gated page)

> Excluded on purpose: `/reset-password` and `/verify-email` require a token query param to render their
> primary state; `/admin` requires an admin role a freshly-registered Standard user lacks; all other
> flag-gated routes duplicate `ComingSoonPage`.

## Architecture

**Approach (chosen): data-driven single spec + shared helper.** Matches the existing helper-driven e2e
style; trivial to extend with a new page.

### Dependency

- Add `@axe-core/playwright` as a `web` devDependency (pairs with the installed `@playwright/test`).

### Helper — `web/e2e/helpers/a11y.ts`

Exports `auditA11y(page, opts?)`:

- Runs `new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()`.
- Applies `opts.disableRules?: string[]` via `.disableRules(...)` and `opts.exclude?: string[]` via
  `.exclude(selector)` — **this is the allowlist mechanism.**
- Asserts `results.violations` is empty using Playwright's `expect` with a custom failure message — e.g.
  `expect(results.violations, formatViolations(results.violations)).toEqual([])`. The formatted message
  lists, per violation: `id`, `impact`, `help`/`helpUrl`, and each failing node's `target` selector, so a
  developer can act without re-running locally. `formatViolations` is a small pure function in the same
  helper module.

Signature (illustrative):

```ts
type A11yOptions = { disableRules?: string[]; exclude?: string[] };
export async function auditA11y(page: Page, opts?: A11yOptions): Promise<void>;
```

Every `disableRules` / `exclude` usage at a call site carries a comment:
`// KNOWN-A11Y(<rule-id>): <reason> — follow-up: <where-tracked>`.

### Spec — `web/e2e/a11y.spec.ts`

- `test('public pages have no accessibility violations')` — loops the public routes, `goto` each, wait on
  a stable landmark/heading already asserted elsewhere, then `auditA11y(page)`.
- `test('authenticated pages have no accessibility violations')` — one `registerNewUser(page)`, then loops
  the authenticated routes (incl. `/series`), navigating and waiting on a stable element before each audit.

Auditing authenticated pages in a single test (one registration, sequential navigations) avoids many
registrations against the shared dev DB and keeps wall-clock low. A per-page wait-for-stable-element step
prevents auditing a mid-render frame.

## Violation handling — two-phase

1. **Build** the dependency + helper + spec and wire them into the existing run (no workflow change).
2. **Run → triage:** execute the suite against the app and collect what axe flags (color-contrast on the
   cyan/dark theme is the most likely culprit; missing form labels / landmark structure are possible).
   - **Fix** the real issues in the app. App fixes keep their existing Vitest tests green.
   - For anything genuinely large to fix now, add a **narrowly-scoped** `disableRules`/`exclude` allowlist
     entry with a documented reason **and** a tracked follow-up bullet in `private/ROADMAP.md` backlog.
   - **End state:** suite green, every exception justified in code and tracked.

The exact set of app fixes is **discovered by running axe** — it cannot be enumerated up front. The
implementation plan must treat phase 2 as a triage-and-branch step, not a fixed edit list.

## CI integration

No change to `.github/workflows/e2e.yml`. The new spec runs under the existing `npx playwright test` step.
The workflow remains **non-blocking**. Promotion to a required check is out of scope.

## Testing & quality gates

- The Playwright run **is** the test for this work; the helper stays thin enough not to warrant its own
  unit test.
- Confirm `web/e2e/**` remains **outside Vitest's coverage scope** (`vite.config.ts`) so the 85% frontend
  unit-coverage gate is unaffected by the new e2e files.
- New TS files must pass `npx prettier --check .` and `npm run lint` (CI gates both).
- Any phase-2 app fixes must keep existing Vitest suites green and not drop coverage below 85%.

## Documentation (on completion)

Per project convention:
- Remove "Accessibility (axe-core) (Task 3)" from the `private/ROADMAP.md` backlog (leave Task 4).
- Prepend a completion summary to `private/archive.md` (newest first).
- Add a `CHANGELOG.md` `[Unreleased]` bullet under `Added` (accessibility test coverage) — and any app
  fixes under `Fixed`.
- Update `react-frontend` agent docs if e2e/a11y conventions are worth recording; `docs-updater` owns the
  full matrix.

## Risks / open questions

- **Unknown remediation size (accepted):** the count/severity of existing violations is unknown until the
  first run. Mitigated by the agreed allowlist policy — fix what's reasonable, document-and-defer the rest,
  so the harness lands regardless of remediation size.
- **Flaky timing:** auditing before a page settles can produce spurious violations; mitigated by waiting on
  a stable element per page before `auditA11y`.
- **axe coverage limits:** automated checks catch a subset of WCAG; this does not certify full AA
  conformance. Acceptable for the stated goal (regression-catching automation).
