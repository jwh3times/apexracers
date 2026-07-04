# ApexRacers — Roadmap Implementation Plan (committed snapshot)

> **Provenance note.** The canonical roadmap is `private/ROADMAP.md` (gitignored, local-only,
> **absent from this clone**). This document is a committed engineering snapshot of the
> outstanding-work picture, **reconstructed from committed sources** and verified against the
> code on this branch. It does not replace `private/ROADMAP.md`; the maintainer should reconcile
> both (and `private/archive.md`) locally.

- **Date:** 2026-07-03
- **Branch:** `claude/roadmap-implementation-plans-o9740c` (from `main` @ `2a748cb`)
- **Sources mined:**
  - `CHANGELOG.md` — `[Unreleased]` + the 0.1.0 release note ("every iRacing-data-backed feature
    ships behind the seeded-disabled `iracing-live` flag … non-functional in production until
    iRacing service-account OAuth credentials are available" — the standing blocker).
  - `CLAUDE.md` — the canonical iRacing-blocker note (`iracing-live` / `iracing-demo`,
    `ApexRacers.Seeder --demo`, "do not enable in prod before then", the referenced-but-absent
    `private/deployTODO.md` §14), the documented Plan-1 limitation, and the documented demo caveats.
  - `docs/superpowers/specs/*.md` and `docs/superpowers/plans/*.md` — every
    deferred / out-of-scope / follow-up list (all eight specs and seven plans swept).
  - `.github/workflows/e2e.yml` — documented non-blocking E2E workflow.
  - `// TODO:` grep across `src/` and `web/src` (exactly one hit:
    `src/ApexRacers.Api/Services/AuthService.cs:299`).
  - README.md (no TODOs; ports/ACS notes only).
- **No GitHub issues exist** for this repo; the lists above are the only committed roadmap signal.

---

## 1. Verified-status table

Every candidate item was checked against the code on this branch before planning. Several items
that committed docs still describe as outstanding have in fact **already shipped** — that drift is
itself a work item (P1).

| # | Item | Verified state | Evidence |
|---|------|----------------|----------|
| A | Plan-1 limitation: Dashboard/Profile iRacing panels gate on `iracing-live` only | **Already fixed in code** — both pages gate on `liveFlag \|\| demoFlag` (commit `7c8dc22` "feat(demo): show Dashboard/Profile/notifications panels under iracing-demo"). `CLAUDE.md` still documents the limitation → **docs drift** | `web/src/features/driver/DashboardPage.tsx:31-34`, `web/src/features/profile/ProfilePage.tsx:351-354`, tests `web/src/features/profile/ProfilePage.test.tsx:423`; stale text in `CLAUDE.md` ("Routing" bullet: "…iRacing panels still gate on `iracing-live` **only**") |
| B | Demo-surface completion: WR overlay, race-detail lap trace, `/compare` curated search | **Shipped** — `WorldRecordService` guard removed (seeded `wr:` rows honored without creds); `DemoCacheSeeder` includes `SeedWorldRecordsAsync` / `SeedLapDataAsync` / `SeedDriverSearchAsync` | `src/ApexRacers.Api/Services/WorldRecordService.cs:17-31`, `src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs:110-154`, spec `docs/superpowers/specs/2026-06-23-demo-data-preview-completion-design.md` |
| C | Old-address "your email was changed" security notice (account-emails deferral) | **Shipped** | `src/ApexRacers.Api/Services/Email/AccountEmailTemplates.cs:41`; CHANGELOG 0.1.0 "a security notice sent to the old address" |
| D | Demo caveat: `/analytics` populates lazily (only after a Recommendations/percentile visit) | **Outstanding** — `UserAnalyticsService` reads pre-computed `CarPercentileResult` rows keyed by the real user's GUID; nothing seeds/warms them | `src/ApexRacers.Api/Services/UserAnalyticsService.cs:12-35`, caveat recorded in `docs/superpowers/specs/2026-06-23-demo-data-preview-design.md` ("Accepted as a minor follow-up") and `CLAUDE.md` demo-caveats note |
| E | Demo caveat: race-guide board shows static "in-progress" sessions | **Outstanding** — seeded rows use a fixed 2020→2099 window so they always pass the now-filter but display as perpetually in-progress | `src/ApexRacers.Seeder/Demo/DemoRaceGuideData.cs` (`Past`/`FarFuture`), `src/ApexRacers.Api/Services/RaceGuideService.cs` |
| F | Demo caveat: `/compare` search — arbitrary (unseeded) terms 503 | **Outstanding by design** (curated-only was a locked decision); the UX around the 503 is the open item | `src/ApexRacers.Api/Services/RivalService.cs:65` (`driversearch:{term}` cache; miss w/o creds → `IRacingNotConfiguredException` → 503), curated terms in `src/ApexRacers.Seeder/Demo/DemoDriverSearchData.cs` |
| G | Guest/anonymous feature-flag read (deferred to "M2/GA" in the M1 spec) | **Outstanding** — `/api/feature-flags` is `[Authorize]`; `FeatureFlagProvider` never fetches for guests, so an unauthenticated visitor always resolves `iracing-live = false` and sees `ComingSoonPage` even after the flag is enabled | `src/ApexRacers.Api/Controllers/FeatureFlagsController.cs:11`, `web/src/context/FeatureFlagProvider.tsx` (`owner == null` → no fetch), deferral recorded in `docs/superpowers/specs/2026-06-21-m1-pre-iracing-launch-mode-design.md` ("Deferred" §) |
| H | Production `iracing-demo` rollout | **Outstanding** — `purge_demo_data.sql` and the idempotent `--demo` seeder exist, but there is no automated pre-enable verification, and the runbook lives only in the absent `private/deployTODO.md` §14 | `src/ApexRacers.Data/Seeds/purge_demo_data.sql`, `src/ApexRacers.Seeder/Demo/*`, `CLAUDE.md` blocker note ("do **not** enable it in prod before then — cached pages would 503") |
| I | iRacing OAuth account linking (Authorization Code callback) | **Stub** — `AuthService.HandleCallbackAsync` throws `NotImplementedException`; the only `// TODO:` in the codebase | `src/ApexRacers.Api/Services/AuthService.cs:299-305` |
| J | `iracing-live` go-live readiness (creds arrive) | **Outstanding** — ingestion worker requires four `IRACING_*` env vars and throws without them; demo teardown script exists; no committed go-live runbook | `src/ApexRacers.Ingestion/Program.cs:29-36`, `src/ApexRacers.Data/Seeds/purge_demo_data.sql` header ("M2 'real creds on' runbook" ordering) |
| K | E2E suite expansion (follow-ups 1–4 from the e2e spec) | **Outstanding** — suite is exactly `smoke.spec.ts` (25 lines) + `a11y.spec.ts` (77 lines); no logout/reset/email-change, telemetry, catalog, or ComingSoon-gating specs | `web/e2e/` listing; follow-ups §"Follow-ups" in `docs/superpowers/specs/2026-06-27-playwright-e2e-design.md:146-154` |
| L | Promote E2E workflow to a required check (follow-up 7) | **Outstanding** — `e2e.yml` explicitly "intentionally NOT a required status check" | `.github/workflows/e2e.yml:8-9`; also `docs/superpowers/specs/2026-06-30-playwright-accessibility-axe-core-design.md:123` |
| M | Visual regression (`toHaveScreenshot()`, "Task 4") | **Outstanding** — explicitly deferred in both Playwright specs; nothing in repo | `docs/superpowers/specs/2026-06-30-playwright-accessibility-axe-core-design.md:6,19`; e2e spec follow-up 6 |
| N | `/healthz` endpoint ("optional future nicety") | **Outstanding** — no health endpoint in `Program.cs`; e2e `webServer` polls `GET /` | `docs/superpowers/specs/2026-06-27-playwright-e2e-design.md:133-134`, `src/ApexRacers.Api/Program.cs` (no health mapping) |
| O | Catalog percentile overlay | **Deferred (backlog)** — catalog spec deferred `CarPercentileResult` overlay as series/week-scoped and awkward on a catalog page | `docs/superpowers/specs/2026-06-18-catalog-explorer-design.md:17-18` |
| P | A11y allowlist follow-ups | **None** — `web/e2e/a11y.spec.ts` contains no `KNOWN-A11Y` / `disableRules` / `exclude` entries; zero-violation gate holds with no debt | `web/e2e/a11y.spec.ts` |

---

## 2. Sequencing & dependencies

**The standing blocker** is iRacing service-account OAuth credentials (CHANGELOG 0.1.0 note;
`CLAUDE.md` canonical blocker note). Work splits cleanly:

**Not blocked on credentials (do now, in this order):**

1. **P1 — Docs drift fix** (item A + B/C doc fallout). Trivial, removes a false "known limitation"
   from every future session's context.
2. **P2 — Demo caveat improvements** (items D, E, F). Small, self-contained, improves the Alpha
   preview that is the *only* usable iRacing surface until creds arrive.
3. **P3 — Prod `iracing-demo` rollout prep** (item H). Engineering half now; operator half gated
   on maintainer action, not creds. Depends on P2 only softly (nicer preview), not technically.
4. **P4 — E2E suite expansion** (items K, N). Independent; do before P5.
5. **P5 — Promote E2E to required check** (item L). Depends on P4 landing and the suite proving
   stable over a run of PRs; final step is an operator branch-protection change.
6. **P6 — Guest/anonymous flag read** (item G). Independent; must land **before** GA (the moment
   `iracing-live` flips for Standard users, guests must see public pages, not ComingSoon).
7. **P8 — Visual regression** (item M) and **P9 — catalog percentile overlay** (item O): backlog,
   anytime.

**Blocked (fully or partially) on credentials:**

- **P7 — iRacing OAuth account linking** (item I): the code can be written now behind an
  interface, but the wire contract (iRacing token/profile endpoints) is an **unverifiable external
  fact** in this repo state (see Ground Rules; `private/iracing-api-response-objects/` is absent
  here) — final mapping + end-to-end verification are creds-gated.
- **P10 — `iracing-live` go-live runbook** (item J): document + rehearse now; execute only with
  creds. **Critical dependency discovered during verification:** `MemberContext` resolves real
  users via `ApplicationUser.IRacingCustomerId`, which is only ever set by the OAuth callback —
  which is a stub (item I). So even with live creds, every `[Authorize]` iRacing-linked endpoint
  would 409 for every real user until P7 ships. **P7 is on the go-live critical path**, not a
  nice-to-have. (Public endpoints — week stats, schedule, standings, race guide, catalog — work
  without linking.)

---

## 3. Per-item plans

### P1 — Documentation drift fix: retire the stale "Plan-1 limitation" note

**Objective & rationale.** `CLAUDE.md` (routing section) still states Dashboard/Profile iRacing
panels "gate on `iracing-live` **only** (a known Plan-1 limitation)". Verification shows this
shipped: both pages compute `showIracing = liveFlag || demoFlag`
(`web/src/features/driver/DashboardPage.tsx:31-34`, `web/src/features/profile/ProfilePage.tsx:351-354`;
commit `7c8dc22`, covered by `ProfilePage.test.tsx:423` "shows iRacing sections when iracing-demo
is on and iracing-live is off"). A false standing limitation in the primary context file misleads
every future session and reviewer.

**Current state.** Code correct; `CLAUDE.md` stale. `NotificationsBell` was included in the same
commit (per its message), so the notifications caveat, if documented locally, is also stale.

**Design/approach.** Pure docs edit — no code. Replace the limitation sentence with a statement
that Dashboard/Profile panels honor `iracing-live` OR `iracing-demo` like `RequireFlag`/`visibleNav`.

**Tasks.**
1. Edit `CLAUDE.md` routing bullet (and any other occurrence of the Plan-1 limitation phrasing).
2. Maintainer (locally): remove the corresponding item from `private/ROADMAP.md` if still listed;
   prepend the `7c8dc22` fix to `private/archive.md` if not already recorded.

**Testing.** None (docs only). Prettier does not cover root markdown, but keep formatting consistent.

**Docs updates.** This *is* the docs update. Add a `CHANGELOG.md` `[Unreleased]` bullet only if the
maintainer considers doc-only corrections changelog-worthy (convention here: no).

**Risks / open questions.** None.

**Size: S** (minutes).

---

### P2 — Demo caveat improvements (analytics lazy-population, race-guide static board, compare 503 UX)

**Objective & rationale.** Three documented demo-mode caveats (`CLAUDE.md` demo-seeding note;
`docs/superpowers/specs/2026-06-23-demo-data-preview-completion-design.md` §5) degrade the Alpha
preview. None are page-breakers, but the preview is the product's only live iRacing surface until
credentials arrive, so polish here has outsized value.

A shared constraint governs all three: **`MemberContext` is deliberately the only demo-aware
branch in the API** (`src/ApexRacers.Api/Services/MemberContext.cs` XML-doc: "This is the only
demo-aware branch in the API"). Every design below preserves that invariant — no second
demo-aware backend branch.

#### P2a — `/analytics` lazy population

**Current state.** `UserAnalyticsService.GetAnalyticsAsync`
(`src/ApexRacers.Api/Services/UserAnalyticsService.cs`) reads `CarPercentileResult` rows keyed by
the caller's *user GUID*. The demo seeder cannot pre-seed rows for unknown future users, so
`/analytics` is empty until the user visits Recommendations or a percentile page (which
compute-and-upsert). Recorded as an accepted follow-up in
`docs/superpowers/specs/2026-06-23-demo-data-preview-design.md`.

**Design.** Frontend-only, demo-agnostic **empty-state CTA** on `AnalyticsPage`
(`web/src/features/*/AnalyticsPage.tsx` — locate under `web/src/features/`): when
`getAnalytics()` returns `[]`, render a card (design tokens; `cardStyle` pattern per the
`react-frontend` agent) explaining that analytics builds from computed percentiles, with:
- a primary "Compute my percentiles" button that calls the existing recommendations endpoint via
  `api.ts` (`request<T>` — reuse the existing `api.getRecommendations()`; computing
  recommendations upserts `CarPercentileResult` rows via `CarRecommendationService` →
  `PercentileCalculationService`) and then refetches analytics;
- a secondary link to `/recommendations`.

This benefits **live** users identically (the lazy-population behavior is inherent to the
compute-on-demand design, not demo-specific), which is why it beats the alternatives.

**Rejected alternatives.**
- Backend "warm on read" (compute percentiles inside `GetAnalyticsAsync` when empty): heavy
  compute inside a GET, duplicates `CarRecommendationService` orchestration, and couples two
  services.
- A demo-aware `UserAnalyticsService` branch: violates the single-demo-branch invariant; already
  explicitly rejected in the 2026-06-23 spec.

**Verify before building:** confirm which endpoint(s) upsert `CarPercentileResult`
(`PercentileCalculationService` / `CarRecommendationService`) and whether one recommendations call
populates enough rows for a non-empty analytics view against a `--demo`-seeded local stack
(`docker compose up` + Seeder — obtainable ground truth per Ground Rules).

**Tasks.**
1. Add the empty-state branch + CTA to `AnalyticsPage` (new small component only if reused).
2. Wire the CTA: `api.getRecommendations()` → refetch analytics; loading + error states.
3. Vitest: empty → CTA renders; click → recommendations called → analytics refetched; non-empty →
   no CTA; error path.

**Testing.** Vitest ≥85% (statements/branches/functions/lines, `vite.config.ts`);
`npx prettier --check .` from `web/`; `npm run lint`. Optional Playwright assertion added under P4
(demo-seeded stack): visit `/analytics` fresh → CTA → click → table appears.

**Docs.** `CHANGELOG.md` `[Unreleased]` → `Fixed` (or `Changed`); update the demo-caveats list in
`CLAUDE.md` (caveat softened: analytics self-serves); maintainer updates `private/ROADMAP.md` /
`private/archive.md` and `private/deployTODO.md` §14 tester notes locally.

**Size: S.**

#### P2b — Race-guide static "in-progress" board

**Current state.** `DemoRaceGuideData.Build` seeds `RaceGuideCacheRow`s with `Start=2020-01-01`,
`End=2099-01-01` so the sentinel rows always pass `RaceGuideService`'s now-window filter
(`src/ApexRacers.Api/Services/RaceGuideService.cs`), rendering a perpetual, visibly-stale board.

**Design.** Frontend display guard on the Live page (`/live`): when an entry's start time is
implausibly old (e.g. `> 24 h` in the past), render the status label as "In progress" and suppress
the absolute start timestamp/elapsed counter. Demo-agnostic (a live session more than a day old
would be equally bogus data), no backend change, no new demo branch.

**Rejected alternatives.**
- Demo-aware `RaceGuideService` (rewrite times at read): violates the single-demo-branch rule.
- Rolling re-seed of the cache row (cron/hosted service to refresh `Start` values): new prod
  infrastructure for a preview nicety; the sentinel row is static JSON by design.
- Accept as-is: cheap, but the fix is a few lines — worth doing.

**Tasks.**
1. Add the guard + label logic in the Live page component (pure helper in `web/src/utils/` if it
   needs unit-testing in isolation — do not inline duplicated time logic per the utilities rule).
2. Vitest: old-start entry renders "In progress" without timestamp; recent entry unchanged.

**Testing.** As P2a (Vitest 85%, prettier, lint).

**Docs.** `CHANGELOG.md` `[Unreleased]`; trim the caveat in `CLAUDE.md`; maintainer syncs private docs.

**Size: S.**

#### P2c — `/compare` curated-term search UX

**Current state.** `RivalService` driver search hits cache key `driversearch:{term}`
(`src/ApexRacers.Api/Services/RivalService.cs:65`); in demo mode only the curated terms in
`src/ApexRacers.Seeder/Demo/DemoDriverSearchData.cs` are seeded — any other term misses, the live
fetch throws `IRacingNotConfiguredException`, and `ExceptionHandlingMiddleware` returns **503**.
The frontend surfaces this as an error. Curated-only search is a **locked decision**
(completion-design §2: "arbitrary unseeded terms stay a documented caveat") — the outstanding work
is the *experience around* the 503, not removing it.

**Design.** Frontend-only:
1. In the Compare page search handler, catch the API error and, when it is the 503
   `IRacingNotConfiguredException` shape (RFC-7807 `status === 503` via the typed error mapping in
   `web/src/services/api.ts`), render a friendly inline empty-state: "Driver search isn't available
   right now — pick a driver from the suggestions below." (Suggestions always work — they come from
   shared `SubsessionResult` rows.)
2. When `useFeatureFlag('iracing-demo')` is on (frontend flag check — presentation only, not a
   backend branch), extend the copy: "Demo mode: search covers sample drivers only — try
   'demo', 'rival', or 'driver'." Keep the term list in one exported constant so tests pin it to
   `DemoDriverSearchData`'s curated set (note the coupling in a comment).

**Rejected alternatives.**
- Backend fallback returning `[]` on not-configured for search only: hides real outage signal in
  live mode and special-cases one endpoint's error contract.
- Demo-aware backend search returning a filtered curated list for arbitrary terms: second demo
  branch; rejected.

**Tasks.**
1. Add error-shape detection + empty-state to the Compare search UI; demo-flag copy variant.
2. Vitest: 503 → friendly state (both flag variants); other errors keep generic handling;
   successful search unchanged.

**Testing.** As P2a. Prettier + lint.

**Docs.** `CHANGELOG.md` `[Unreleased]`; `CLAUDE.md` caveat text updated ("arbitrary terms show a
guided empty-state" rather than "503 — use the suggestions list"); maintainer syncs private docs.

**Size: S.** (P2 total: **S/M**.)

---

### P3 — Production `iracing-demo` rollout (engineering prep + operator runbook)

**Objective & rationale.** Let Alpha testers preview the full product in production on synthetic
data — the only way the iRacing surface is demonstrable before credentials arrive. `CLAUDE.md` is
explicit: `iracing-demo` is fully functional **only** after `ApexRacers.Seeder --demo` has run
against that DB — "do **not** enable it in prod before then (cached pages would 503)". The runbook
lives in `private/deployTODO.md` §14 (absent here), so the committed repo has **no guard** against
enabling the flag on an unseeded DB. The engineering work is to make that mistake mechanically
detectable.

**Current state.**
- Seeder: `src/ApexRacers.Seeder/Program.cs` + `SeedData.cs` (catalog + synthetic laps, negative
  subsession IDs, custIds 100001–100200) and `src/ApexRacers.Seeder/Demo/DemoCacheSeeder.cs` with
  nine seed steps (`SeedMembersAsync` … `SeedDriverSearchAsync`, orchestrated by `SeedAllAsync`),
  all rows carrying the far-future `ExpiresAt` sentinel (`DemoCache.Sentinel`).
- Teardown: `src/ApexRacers.Data/Seeds/purge_demo_data.sql` (surgical, prod-safe, ordering
  documented in its header).
- Flag: `iracing-demo`, Alpha-gated (`MemberContext.IsDemoActiveForUserAsync` checks
  `MinimumRole` via `AdminService.RoleHierarchy`); enabled via Admin UI (`AdminService` flag CRUD),
  no redeploy.
- Constraint: the base Seeder "needs `private/iracing-api-response-objects/` populated first"
  (`CLAUDE.md` Commands §) — an operator-side prerequisite that cannot be satisfied from a public
  clone.

**Design/approach.**

*Engineering (committed, not operator-gated):*

1. **Seeder `--verify-demo` mode.** New verification pass (e.g. `DemoSeedVerifier` in
   `src/ApexRacers.Seeder/Demo/`) invoked as `dotnet run --project src/ApexRacers.Seeder -- --verify-demo`
   (and automatically at the end of `--demo`). It asserts, against the connected DB, everything the
   runtime demo surface depends on, and exits non-zero with a per-check report otherwise:
   - one `ExternalDataCache` row per expected key family/count: member/profile/progression keys for
     `DemoData.DriverCustId`, leaderboards per category, standings, `race-guide`, `wr:{car}:{track}`
     per distinct schedule combo, `laps:{subsessionId}:100001` per synthetic subsession,
     `driversearch:{term}` per curated term — derive expectations from the same builders the seeder
     uses (`DemoCacheSeeder`, `DemoDriverSearchData`, …), not hardcoded numbers;
   - every demo cache row carries the sentinel `ExpiresAt >= 9000-01-01`;
   - synthetic subsessions (`Id < 0`) + results exist; `SeasonCarBop` rows and non-null
     `Week.WeatherSummaryJson` exist for active seasons;
   - the `iracing-demo` flag row exists (it may legitimately be disabled — report state, don't fail).
   Keep the checks pure/testable (a `DemoSeedExpectations` helper computing expected key sets from
   DB state; verifier compares sets and reports diffs).
2. **Committed runbook stub** in this document (below) so the ordering is not private-only. The
   authoritative operator detail (connection strings, Key Vault, Azure identities) stays in
   `private/deployTODO.md` §14.

*Operator runbook (gated on maintainer, NOT on iRacing creds):*

1. Ensure prod DB is migrated (API self-migrates on boot — see e2e design §"Why these were safe").
2. From a machine with `private/iracing-api-response-objects/` and the prod
   `DATABASE_CONNECTION_STRING`: run the Seeder (catalog + synthetic laps), then `--demo`.
3. Run `--verify-demo`; proceed only on exit 0.
4. In Admin → Feature Flags, enable `iracing-demo` (stays Alpha-gated).
5. Smoke as an Alpha user: `/dashboard`, `/progression`, `/races`, one race detail with pace trace,
   `/live`, `/compare` (suggestions + a curated term), a percentile page (WR overlay), `/analytics`
   after the P2a CTA. Confirm `DemoBanner` shows.
6. Rollback: disable the flag; optionally run `purge_demo_data.sql`.

**Rejected alternatives.**
- Runtime guard in `AdminService` refusing to enable `iracing-demo` when seed rows are absent:
  attractive, but couples the API to seeder internals and gives false confidence (partial seeds
  pass a shallow check). The offline verifier can be exhaustive; keep the API honest. *(Could be
  revisited as a lightweight "warning" in the admin UI later.)*
- Running the seeder from CI against prod: prod credentials in CI for a one-time operator action —
  rejected on least-privilege grounds.

**Tasks.**
1. `src/ApexRacers.Seeder/Program.cs`: parse `--verify-demo`; run verifier after `--demo` too.
2. New `src/ApexRacers.Seeder/Demo/DemoSeedVerifier.cs` (+ pure `DemoSeedExpectations`).
3. xUnit tests in `src/ApexRacers.Tests/`: expectations computed correctly from a seeded
   `DbContextFactory.Create()` (in-memory SQLite) context; verifier passes on a fully-seeded
   fixture, fails with named checks when a key family is deleted; sentinel check. **Watch the known
   SQLite limitation** — `DateTimeOffset` range comparisons (the sentinel filter) may need
   `CreateInMemory()` like `ExternalDataCacheCleanupService` tests (see `CLAUDE.md` Test-DB note).
4. README: one paragraph documenting `--verify-demo` next to the existing seeder docs.

**Testing.** Backend 85% line **and** branch (CI gates both in `.github/workflows/deploy.yml`
`Test` job); run locally via `dotnet-coverage collect "dotnet test" -f xml` + `reportgenerator`.
Manual: full local rehearsal — `docker compose up`, Seeder, `--demo`, `--verify-demo`, flip flag,
walk the smoke list.

**Docs.** `CHANGELOG.md` `[Unreleased]` → `Added` (`--verify-demo`); `CLAUDE.md` Commands § (new
seeder form) and blocker note (mention the verifier as the pre-enable gate); README seeder section;
maintainer updates `private/deployTODO.md` §14 to insert the verify step and syncs
`private/ROADMAP.md`/`archive.md`.

**Risks / open questions.**
- The exact cache-key inventory must be derived by reading `DemoCacheSeeder` + each service's key
  format at implementation time (obtainable in-repo; do not hardcode from this plan).
- Prod DB size/perf of `--demo` (~425 lap-data rows + members/standings/etc.) — trivial, but run
  off-peak.
- Operator prerequisite: `private/iracing-api-response-objects/` must exist on the operator's
  machine (gitignored; unverifiable here — flagged per Ground Rules).

**Size: M.**

---

### P4 — E2E suite expansion (spec follow-ups 1–4, + `/healthz`)

**Objective & rationale.** The Playwright harness landed as a deliberate thin slice; the design
spec (`docs/superpowers/specs/2026-06-27-playwright-e2e-design.md:146-154`) enumerates follow-ups
to be tracked in ROADMAP: (1) broader auth flows, (2) telemetry upload, (3) public catalog pages,
(4) ComingSoonPage gating. Expansion is also the prerequisite for P5 (promotion to required) —
a required check guarding only one smoke test is weak.

**Current state.** `web/e2e/` = `smoke.spec.ts` (register → dashboard → session persists),
`a11y.spec.ts` (axe-core, 5 public + 7 authed pages, zero-violation), helpers
`web/e2e/helpers/users.ts` (`uniqueEmail`, `TEST_PASSWORD`, `registerNewUser`) and `helpers/a11y.ts`.
Conventions locked in the spec: role/label selectors, self-provisioned data (unique email per
run), `e2e/` excluded from Vitest coverage. CI (`.github/workflows/e2e.yml`): Postgres service →
SPA build into `wwwroot` → Playwright `webServer` launches the API → Chromium only, retries 2,
trace on-first-retry.

**Design/approach.** Four new spec files, reusing `registerNewUser`:

1. **`web/e2e/auth.spec.ts`** — logout (menu → landing → protected route redirects to `/login`);
   password reset via the Development token echo (the reset token is returned in the response body
   in Development — README:134; drive `/forgot-password` → capture token from the response via
   `page.waitForResponse` → `/reset-password?token=…` → login with new password); email change
   request → confirm via the same Development-echo pattern **if** the confirm token is echoed in
   Development — *verify in `AuthController`/`AuthService` first; if it is not echoed, scope email
   change out and note it* (Ground Rules: don't build on the guess).
2. **`web/e2e/telemetry.spec.ts`** — generate a minimal `.ibt` fixture with the existing
   `FakeIbtBuilder` approach from the backend tests (spec follow-up 2 names it): add a tiny
   committed fixture under `web/e2e/fixtures/` produced by a one-off script, or port the builder to
   a Node helper — **decision: commit a small binary fixture generated once from `FakeIbtBuilder`**
   (deterministic, no cross-stack builder port). Upload on `/telemetry`, assert laps appear on
   `/my-laps`.
3. **`web/e2e/catalog.spec.ts`** — public catalog + gated-route behavior. Requires flag-on +
   seeded data in CI (see below): `/cars`, `/tracks`, one detail page each.
4. **`web/e2e/gating.spec.ts`** — ComingSoonPage: with both flags off (the CI default — flags are
   seeded disabled), `/series` and `/live` render ComingSoon for guest and authed users; nav hides
   gated items (asserts the `RequireFlag`/`visibleNav` contract in `web/src/App.tsx:80-97`).
5. **`/healthz`** (spec's "optional future nicety", enables a cleaner `webServer.url`): map
   `app.MapHealthChecks("/healthz")` (or a minimal `MapGet` returning 200) in
   `src/ApexRacers.Api/Program.cs` **after** migrations; point `webServer.url` at it.

**CI data strategy for catalog/demo-dependent specs (key decision).** Catalog pages 404/empty on
a bare DB and gated routes render ComingSoon while flags are off. Options:
- *(a)* Run the Seeder (+ `--demo`) in `e2e.yml` before Playwright and enable `iracing-demo` for
  the test user (register → promote to Alpha? Admin bootstrap is nontrivial in CI).
- *(b)* Split: gating spec needs **no** seed (asserts the off state — the true CI default);
  catalog spec runs **only when seed data exists**, i.e. gate it with a Playwright `test.skip`
  on a probe (e.g. `GET /api/cars` empty) and run it locally against the compose stack.
- **Recommendation: (b) now, (a) later.** (a) requires solving admin bootstrap + role promotion in
  CI and depends on `private/iracing-api-response-objects/` for the seeder — which is **absent in
  CI** (gitignored). That makes (a) partially infeasible today; flag it: if full catalog coverage
  in CI is wanted, the seeder's fixture dependency must first be made CI-safe (e.g. a committed
  minimal catalog fixture) — record as an open question for the maintainer.

**Rejected alternatives.** Mock-API e2e (violates the locked "full real stack" decision);
per-suite DB truncation (spec convention is self-provisioned unique data).

**Tasks.**
1. `Program.cs`: `/healthz`; xUnit not needed (excluded controllers; endpoint is I/O glue) but the
   e2e config change exercises it. Update `web/playwright.config.ts` `webServer.url`.
2. Add the four spec files + fixture; extend `helpers/` (e.g. `logout(page)`).
3. Keep `e2e/` out of Vitest include (already the case — verify `vite.config.ts`).
4. `e2e.yml`: no topology change for (b); bump artifact name/retention if reports grow.

**Testing.** The suite *is* the test. Local: `docker compose up` + `npm run test:e2e` (full),
CI: green non-blocking run. Prettier whole-tree (`npx prettier --check .` from `web/` covers
`e2e/`). Vitest/coverage unaffected (e2e excluded). A11y: run `a11y.spec.ts` unchanged — new pages
visited by new specs are not auto-audited; **add any newly-reachable page states to the a11y page
set** if they are distinct surfaces (keeps the zero-violation gate meaningful).

**Docs.** `CHANGELOG.md` `[Unreleased]` → `Added`; `CLAUDE.md` Testing § (spec list) and the
`react-frontend` agent file (e2e conventions section); README if the e2e how-to changes; maintainer
moves e2e follow-ups 1–4 to done in `private/ROADMAP.md`.

**Risks / open questions.**
- Development token echo for **email change** confirm is unverified (flagged above) — check before
  designing that leg.
- `.ibt` fixture realism: `FakeIbtBuilder` output shape is in-repo ground truth (backend tests) —
  use it, don't hand-craft bytes.
- Flake risk grows with suite size — keep retries 2 + trace on-first-retry; this is the P5
  stability gate.

**Size: M.**

---

### P5 — Promote the E2E workflow to a required check (spec follow-up 7)

**Objective & rationale.** `e2e.yml` is intentionally non-blocking ("Promote to required once the
suite proves stable" — `.github/workflows/e2e.yml:8-9`; e2e spec follow-up 7; a11y spec:
"Promotion to a required check is out of scope"). With P4 landed and the a11y gate inside the same
suite, a green E2E run is the closest thing to a deploy smoke test this repo has — it should gate
merges.

**Current state.** Workflow: `pull_request` → `main` + `workflow_dispatch`, job name
`Playwright E2E`, concurrency-cancelled per-ref, retries 2, report artifact. The blocking gates
today are the `Format` and `Test` jobs in `.github/workflows/deploy.yml` (branch-protection
required checks are a GitHub setting, not readable from the repo).

**Design/approach.**
1. **Stability gate first:** define the promotion criterion — e.g. 10 consecutive PR runs (or 2
   weeks) with zero non-code-caused failures after P4 merges. Track informally; don't build tooling.
2. **Workflow hardening before promotion:**
   - Add `push: branches: [main]` trigger? **No** — required checks apply to PRs; keep PR-only
     (deploy.yml owns push). *(Decision: no change.)*
   - Pin the job name (`Playwright E2E`) — required-check matching is by name; add a comment
     warning that renaming breaks branch protection (mirrors the GuardianTracker convention).
   - Consider `timeout-minutes` on the job so a hung webServer can't hold a PR hostage.
   - Update the header comment (no longer "non-blocking").
3. **Operator step (maintainer-gated):** add `Playwright E2E` to the required status checks in the
   repo's branch-protection rules/ruleset for `main`. Not creds-gated.

**Rejected alternatives.** Merging e2e into `deploy.yml` as a `needs:` job (couples deploy latency
to Playwright and loses independent re-run); making only the smoke test required via a split
workflow (two workflows to maintain for marginal benefit).

**Tasks.**
1. Edit `.github/workflows/e2e.yml`: comment update, `timeout-minutes`, name-pinning comment.
2. Maintainer: flip branch protection; watch the first few gated PRs.
3. Rollback plan: remove the required check (setting change only) if flake bites.

**Testing.** CI itself; a deliberately-failing draft PR to confirm the gate blocks.

**Docs.** `CLAUDE.md` Testing § ("non-blocking" → "required"); CHANGELOG `[Unreleased]` →
`Changed`; maintainer syncs `private/ROADMAP.md`.

**Risks.** Flake tax on every PR — mitigated by the stability gate and retries; the rollback is a
one-click setting.

**Size: S** (plus the calendar-time stability window).

---

### P6 — Guest/anonymous feature-flag read path (GA readiness)

**Objective & rationale.** Deferred in the M1 design
(`docs/superpowers/specs/2026-06-21-m1-pre-iracing-launch-mode-design.md`, "Deferred" §): guests
must be able to see the public iRacing pages once `iracing-live` is truly on. Today
`/api/feature-flags` is `[Authorize]` (`src/ApexRacers.Api/Controllers/FeatureFlagsController.cs:11`)
and `FeatureFlagProvider` only fetches when a user is present (`web/src/context/FeatureFlagProvider.tsx`,
`owner == null` → no fetch), so an anonymous visitor always resolves every flag `false` and
`RequireFlag` renders `ComingSoonPage` on `/series`, `/cars`, `/tracks`, `/races/:id`, etc. —
even at GA. This must land **before** the `iracing-live` flip (P10 predecessor), and it is not
creds-blocked, so build it now.

**Current state (additional).** Flag eligibility is hierarchical: `AdminService` resolves a user's
single role level against `FeatureFlag.MinimumRole` (`MemberContext.IsDemoActiveForUserAsync`
shows the pattern). Guests have no role — they must see only flags that are enabled **and** open
to the lowest tier.

**Design/approach.**
- **Backend:** make the flag read work anonymously without weakening the authed contract.
  - `AdminService`: add `GetPublicFlagsAsync(CancellationToken)` returning enabled flags with
    `MinimumRole == "Standard"` (level 0 — the bottom of `AdminService.RoleHierarchy`). Pure query;
    reuses the existing flag DTO shape returned by `GetFlagsForUserAsync` so `api.ts` types don't
    fork.
  - `FeatureFlagsController`: replace `[Authorize]` with `[AllowAnonymous]` on the single `GET`;
    inside, if the JWT `sub` claim parses → existing `GetFlagsForUserAsync(userId)`; else →
    `GetPublicFlagsAsync()`. One route, no client URL change. (Controllers bind inputs only —
    the branch is a two-line dispatch, logic stays in the service.)
  - Security note: this endpoint then leaks *names of enabled Standard-tier flags* to the world.
    That is the point (they gate public UI), and `iracing-demo` (Alpha) is never exposed. Flag for
    `code-reviewer`/`penetration-tester` review anyway.
- **Frontend:** `FeatureFlagProvider` — fetch flags when `owner == null` too (guest fetch), keyed
  as owner `'guest'` so the stale-map guard keeps working across login/logout transitions; the
  `isEnabled` owner-match logic already handles identity changes. `request<T>` sends no auth
  header when there's no token — verify the 401-retry path doesn't loop for anonymous calls
  (read `web/src/services/api.ts` before wiring).

**Rejected alternatives.**
- Separate `/api/feature-flags/public` endpoint: two client codepaths and a second DTO for the
  same concept; the single-route branch is simpler and keeps `api.ts` unchanged except the call
  condition.
- Embedding public flags in `index.html` at build time: flags are runtime-mutable by design
  ("no redeploy required to flip it" — CHANGELOG 0.1.0).
- Treating guests as Standard via a synthetic principal: confusing in auth middleware and easy to
  get wrong; explicit anonymous branch is clearer.

**Tasks.**
1. `AdminService.GetPublicFlagsAsync` + xUnit (enabled Standard flag returned; disabled or
   higher-tier flags excluded — branch coverage on both filters).
2. `FeatureFlagsController` dispatch (`[AllowAnonymous]`); controllers are excluded from backend
   coverage, keep it logic-free.
3. `FeatureFlagProvider` guest fetch + Vitest (guest fetches and resolves public flags; login
   refetches under the user owner; logout returns to guest set; fetch failure → all-off).
4. Guards test refresh (`web/src/__tests__/guards.test.tsx`) if it stubs the provider.
5. e2e (bolts onto P4's `gating.spec.ts`): flags off → guest sees ComingSoon (unchanged); *(local
   only, seeded)* flag on → guest sees `/series`.

**Testing.** Backend 85% line+branch; Vitest 85%; prettier both trees; lint; a11y unaffected
(ComingSoon already audited).

**Docs.** `CHANGELOG.md` `[Unreleased]` → `Added`; `CLAUDE.md` (controller table row for
`FeatureFlagsController` gains "**public**", blocker-note nuance, `FeatureFlagProvider` description);
`react-frontend` + `dotnet-api` agent files if they document the flag flow; maintainer syncs
`private/ROADMAP.md` (M2 item) and `private/PRD.md`.

**Risks / open questions.**
- Cacheability: anonymous responses may be CDN/proxy-cached — confirm no `Cache-Control: private`
  assumptions break the flip-without-redeploy property (low risk; App Service default).
- None external — all facts verifiable in-repo.

**Size: M.**

---

### P7 — iRacing OAuth account linking (complete `HandleCallbackAsync`)

**Objective & rationale.** The only `// TODO:` in the codebase
(`src/ApexRacers.Api/Services/AuthService.cs:299-305`): `HandleCallbackAsync(code, state)` throws
`NotImplementedException`. Per §2, this is **on the go-live critical path**: `MemberContext`
resolves real users through `ApplicationUser.IRacingCustomerId`, which only this callback sets —
without it, every personalized iRacing endpoint 409s (`IRACING_NOT_LINKED`) for every real user
even after `iracing-live` flips.

**Current state.** `AuthController` exposes the callback route (controller table: "iRacing OAuth
callback"); the TODO enumerates the intended steps: validate `state` (CSRF), exchange the code via
the Authorization Code flow, fetch the driver profile (`customerId`, `displayName`), set
`ApplicationUser.IRacingCustomerId`, re-issue the JWT with updated claims. The ingestion worker
uses `UsePasswordLimitedOAuth()` with service-account creds — a *different* grant than the
user-facing Authorization Code flow.

**Design/approach.**

- **State/CSRF (decision):** use **stateless HMAC-signed state** — `base64url(payload).sig` where
  payload = `{ userId, issuedAt, nonce }` signed with a key derived from `JWT_SIGNING_KEY` (or a
  dedicated secret), validated for signature + max age (10 min) + `userId` match on callback. Pure
  `OAuthStateProtector` helper → directly unit-testable.
  *Rejected:* a nonce store (the TODO's literal suggestion) — a new table/cache write per login
  attempt for something a MAC gives us stateless; *(note the deviation from the TODO comment in
  the PR description)*. Also rejected: reusing `ExternalDataCache` as the nonce store (it is an
  iRacing-response cache, not a general KV; would pollute its purge semantics).
- **Token exchange + profile fetch:** isolate all iRacing-OAuth I/O behind a focused service
  (e.g. `IRacingLinkService`) injected into `AuthService`, using `HttpClient` via
  `IHttpClientFactory` with **config-driven** endpoint URLs (`IRACING_OAUTH_TOKEN_URL`, etc.) and
  the app's own iRacing client id/secret. Map only the two fields we need (`cust_id`,
  `display_name`) into a small internal record — never persist or cache the raw token response.
  Check first whether `Aydsko.iRacingData` exposes Authorization-Code helpers before hand-rolling
  (the SDK is the wire contract per Ground Rules; it currently ships `UsePasswordLimitedOAuth()` —
  whether it supports the user Authorization Code grant must be read from the installed package,
  not assumed).
- **Persistence + re-issue:** set `IRacingCustomerId` (+ optionally refresh `DisplayName`), save,
  then reuse the existing JWT/refresh issuance path so the new claims flow. Storing the user's
  iRacing access/refresh tokens is **out of scope** (nothing in the API calls iRacing as the user;
  all live calls are service-account via `CachedIRacingClient`) — record this decision.
- **Error contract:** invalid/expired state → `InvalidOperationException` (→ 400 via
  `ExceptionStatusMapper`); iRacing exchange failure → surfaced as 400 with a safe message;
  unconfigured client creds → `IRacingNotConfiguredException` (→ 503, consistent with the rest of
  the surface).
- **Frontend:** verify the existing callback/link UX in `web/src` (settings/profile "link iRacing"
  affordance) and complete whatever redirect handling is missing — audit at implementation time.

**⚠ Ground-Rules flag (hard).** The iRacing OAuth token endpoint, parameter names, and the
profile-response shape are **external facts not verifiable in this clone**:
`private/iracing-api-response-objects/` is gitignored/absent, there are no creds, and SDK support
for this grant is unconfirmed. **Do not finalize the wire mapping from this plan.** Implement the
protector, service seam, persistence, claims re-issue, and tests against a faked
`IRacingLinkService` now; land the concrete HTTP mapping only once the maintainer supplies the
captured shapes or credentials. Designing a runtime "discovery" of the shape is itself a banned
assumption — stop and ask.

**Tasks.**
1. `OAuthStateProtector` (pure) + xUnit (round-trip, tamper, expiry, wrong-user).
2. `IRacingLinkService` interface + config-driven implementation skeleton (creds absent → throws
   `IRacingNotConfiguredException`); DI registration.
3. `AuthService.HandleCallbackAsync` real implementation against the interface + xUnit via fakes
   (happy path sets `IRacingCustomerId` and re-issues tokens; bad state 400-path; unconfigured
   503-path; already-linked overwrite semantics — **decide + test**: default *overwrite allowed
   for the same signed-in user*).
4. State **issuance**: wherever the authorize-redirect URL is built (add `GET /api/auth/iracing/authorize`
   or equivalent if missing — audit `AuthController` first), include the signed state.
5. Frontend audit + wiring (settings "Link iRacing account" → authorize URL → callback → refreshed
   session shows `iRacingCustomerId`); Vitest for the new UI states.
6. **Creds-gated final step:** real-wire mapping + a manual end-to-end link against iRacing,
   then a live smoke of one personalized endpoint.

**Testing.** Backend 85% line+branch (protector and service logic are pure/fake-friendly —
controllers excluded); Vitest 85%; prettier/lint; e2e cannot cover the external leg — add a gating
e2e only for the unlinked 409 UX if not already covered.

**Docs.** `CHANGELOG.md` `[Unreleased]` → `Added`; `CLAUDE.md` (AuthService bullet, controller
table, env vars); README (new `IRACING_OAUTH_*` config); `dotnet-api` agent file (new service);
maintainer: `private/ROADMAP.md`, `private/deployTODO.md` (Key Vault entries), and capture the
OAuth response shapes into `private/iracing-api-response-objects/` when obtained.

**Risks / open questions.**
- SDK support for the Authorization Code grant — unknown (flagged).
- iRacing app registration (redirect URI allow-listing) — operator-side, creds-gated.
- Claims re-issue: confirm the JWT actually carries a cust-id claim today or whether clients
  re-fetch the profile — read `AuthService` token issuance before assuming.

**Size: L** (of which the creds-gated tail is small but blocking).

---

### P8 — `iracing-live` go-live readiness (runbook + rehearsal)

**Objective & rationale.** When credentials arrive, flipping `iracing-live` must be a rehearsed,
ordered procedure — the pieces exist (`purge_demo_data.sql` header even prescribes the ordering)
but nothing committed assembles them, and two engineering gaps (P6 guests, P7 linking) silently
gate the outcome.

**Current state.** Ingestion worker (`src/ApexRacers.Ingestion/`) hard-requires
`IRACING_USERNAME/PASSWORD/CLIENT_ID/CLIENT_SECRET` (`Program.cs:29-36`), runs on an
`INGESTION_INTERVAL_MINUTES` loop (default 60), deployed as Azure Container App
`apexracers-ingestion` (deploy.yml builds/pushes it on every merge — the *deploy* exists; the
*runtime* fails fast without secrets). `CachedIRacingClient` throws `IRacingNotConfiguredException`
without API-side creds. Demo teardown: `purge_demo_data.sql` (demo flag off → purge → only then
live on; the `CarPercentileResults` truncate is safe **only** before real ingestion exists — its
header says so explicitly).

**Design/approach (ordered go-live runbook — engineering-authored now, operator-executed later):**

1. **Preconditions (engineering, not creds-gated):** P6 (guests) and P7 (linking, minus final wire
   mapping) merged; P3 verifier available for post-purge sanity.
2. Store the four `IRACING-*` secrets in Key Vault for **both** apps
   (`HyphenToUnderscoreSecretManager` maps hyphen→underscore in both `Program.cs` files).
3. Start/restart the ingestion Container App; watch one full cycle (catalog refresh via
   `CatalogIngest`, seasons/weeks, subsessions with **positive** IDs).
4. **Demo teardown ordering (from the purge script header — do not reorder):**
   a. Admin UI: `iracing-demo` → disabled.
   b. Run `purge_demo_data.sql` (removes negative-ID subsessions, truncates
      `CarPercentileResults`, deletes sentinel cache rows, clears synthetic BoP/weather).
   c. **Only then** enable `iracing-live`.
   The truncate-safety invariant means: **if ingestion has already run before teardown, stop** —
   percentile rows would no longer be purely demo-derived; in that case purge must be revisited
   (open question flagged below).
5. Cache warm-up: **none required** — `ExternalDataCache` is lazy by design (TTL-only, per-request
   fill). Optional nicety: a smoke pass over the public pages to pre-fill 24 h keys
   (leaderboards/standings/WR). *Rejected:* a warm-up job/service — unnecessary machinery for
   lazy caches.
6. Smoke checklist: public surface as guest (needs P6); register/link a real account (needs P7);
   one personalized page per cache family; confirm `DemoBanner` is gone and no sentinel rows
   remain (`SELECT count(*) … WHERE "ExpiresAt" >= '9000-01-01'` → 0).
7. Rollback: disable `iracing-live` (surface returns to ComingSoon); ingestion may keep running
   harmlessly; re-enabling demo afterwards requires re-seeding (`--demo` is idempotent).

**Engineering tasks (committable now).**
1. Commit this runbook (it lives in this document; maintainer mirrors the operator detail into
   `private/deployTODO.md`).
2. Rehearse locally: compose stack + seeded demo → execute steps 4a–4c against local Postgres →
   run P3 `--verify-demo` (should now FAIL, proving teardown) → flip `iracing-live` locally with
   seeded catalog data and walk the public pages.
3. Optional: a `Seeds/verify_no_demo.sql` (or a `--verify-teardown` mode on the P3 verifier)
   asserting zero sentinel rows / zero negative-ID subsessions — S, recommended.

**Testing.** The local rehearsal is the test; no new coverage-gated code unless
`--verify-teardown` is built (then: xUnit as in P3).

**Docs.** `CHANGELOG.md` when teardown tooling lands; `CLAUDE.md` blocker note gains a pointer to
this runbook; maintainer: `private/deployTODO.md` (authoritative operator copy),
`private/ROADMAP.md` M2 milestone.

**Risks / open questions.**
- **Ordering hazard (flagged):** if real ingestion runs before demo teardown, the
  `CarPercentileResults` truncate is no longer safe — the runbook forbids it, but nothing enforces
  it. `--verify-teardown` should assert "no positive-ID subsessions yet" before allowing the purge
  in the demo→live path, or the purge script gains a guard `DO $$ … $$` block. Decide at
  implementation.
- First live ingestion behavior at scale (rate limits, chunk downloads) — external facts;
  observe, don't assume.

**Size: M** (engineering); execution creds-gated.

---

### P9 — Backlog (tracked, not scheduled)

- **Visual regression — Playwright `toHaveScreenshot()` ("Task 4").** Deferred by both Playwright
  specs; the a11y spec pins the approach: bolt onto the existing harness, **pin the CI browser
  image to `mcr.microsoft.com/playwright`** for stable rendering (e2e spec follow-up 6). Scope:
  screenshot baselines for the stable public pages (landing, login, terms, privacy, ComingSoon)
  first; authed pages after the demo/live data question is settled (dynamic data → masking or
  seeded determinism). Requires baseline-update workflow docs. Size: **M**.
- **Catalog percentile overlay** (`docs/superpowers/specs/2026-06-18-catalog-explorer-design.md`):
  deferred as series/week-scoped and awkward on a catalog page; revisit only with a concrete UX
  (e.g. "best percentile this season" chip on car detail). Respect persist-vs-cache rules
  (`CarPercentileResult` is persisted user-owned data — #3). Size: **S/M**.
- **Other transactional emails** (welcome, race alerts) — account-emails spec §8 notes the
  foundation supports them; product call first. Size: **S each**.

---

## 4. Cross-cutting execution rules (apply to every item)

- **Architecture:** controller-binds/service-owns-logic; DTO `record`s in `Dtos/ResponseDtos.cs`;
  `api.ts` types kept in sync; all frontend fetches through `request<T>`; design tokens/fluid
  `clamp()` classes, cyan accent only; persist-vs-cache per the `CLAUDE.md` decision framework;
  **never cache raw Aydsko SDK types**.
- **Gates:** backend 85% line **and** branch (xUnit; SQLite in-memory provider caveats);
  frontend Vitest 85%; `npx prettier --check .` from `web/` (whole tree); `npm run lint`;
  Playwright e2e + axe zero-violation gate.
- **Docs matrix per merge:** `CHANGELOG.md` `[Unreleased]` (correct category); `CLAUDE.md`;
  README; affected `.claude/agents/*` files; **and — maintainer, locally —** remove the shipped
  item from `private/ROADMAP.md`, prepend to `private/archive.md`, update `private/PRD.md` /
  `private/deployTODO.md` as relevant. This committed plan should be updated or superseded as
  items land.
- **Ground Rules:** anything marked ⚠ above (iRacing OAuth wire shapes, SDK grant support,
  seeder fixture availability in CI, email-change token echo) is an unverified external/internal
  fact — verify against obtainable ground truth or ask the maintainer **before** implementing
  against a guess.
