# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

No unreleased changes.

## [0.4.46] - 2026-08-09

### Changed

- The field-percentile rank now has one owner, `ApexRacers.Core.FieldPercentile`. The formula was written five times under **two incompatible conventions**: two sites excluded the ranked driver by customer id, three counted against a bare list of lap times with no exclusion at all. Those two conventions produce the same number today *only because `>` is strict* — a driver's own lap is never slower than itself, so leaving it in the count happens to be harmless. Changing any one site to `>=`, to count a tie as beaten, would have made the unfiltered sites start counting the driver as slower than themselves while the filtered ones stayed correct, with nothing in the type system or the tests connecting them.
- The module takes the **other** drivers' laps rather than the whole field, which removes a parameter and a branch: once the ranked driver is excluded, the "is the driver in the field?" distinction disappears, because the old `driverInField ? total - 1 : total` denominator is just "how many other drivers are there" in both arms. The exclusion is now the first thing the signature asks for instead of something each caller has to remember — and it is not cosmetic, since a driver's own lap can be *slower* than the value being ranked when a personal lap supersedes their race result, in which case leaving it in the field counts as one more driver beaten.
- `FieldPercentile.MedianOfSorted` replaces three copies of the same even/odd midpoint code. The sorted precondition is stated in the name rather than enforced, because all three callers already sort for other reasons.
- **No behaviour changes.** The refactor is equivalent at all eight call sites; the 493 pre-existing tests passed against it before any new test was added. The arithmetic is now covered directly rather than only through full database fixtures — the percentile service tests previously documented it in comments precisely because it could not be asserted any other way.

## [0.4.45] - 2026-08-09

### Changed

- The two iRacing unit converters are now one module, `ApexRacers.Core.IRacingUnits`. The same Fahrenheit/Celsius and mph/m·s⁻¹→km/h conversions were written twice — once beside the subsession read path, once beside the schedule read path — each carrying the identical doc-comment ("0 = Fahrenheit, otherwise Celsius. Pure + testable") and each with its own tests. Both sets passed, because each was written against the copy it sat beside: the arithmetic was tested, but the load-bearing knowledge (the unit encoding, the constants) was stored twice. **No behaviour changes** — the two implementations differed in whether the already-metric branch rounded, but `Math.Round` on the integer input that branch received is a no-op, so the divergence was latent rather than live and no wrong temperature was ever served.
- `Subsession.WeatherJson` and `Week.WeatherSummaryJson` now hold owned `WeatherSnapshot`/`WeatherForecastSnapshot` records instead of raw Aydsko SDK types. The project's rule — cache mapped DTOs, never raw SDK types, because their wire shape drifts and they carry `[Obsolete]` fields — was enforced on the `ExternalDataCache` path but not on the persisted one, which is strictly worse: cache rows expire within 24 hours, but these persist forever and a subsession is ingested exactly once. An SDK upgrade renaming a `[JsonPropertyName]` would make every historical row deserialize into a default-valued object — a *successful* parse with zeros, not an exception — so the readers' `catch (JsonException)` would never fire and every historical race would silently report 0 °C. `ResultsWeather` already ships alias pairs (`TempValue`/`TemperatureValue`, `RelHumidity`/`RelativeHumidity`), which is that drift mid-flight.
- The snapshots pin their JSON property names to the SDK's existing snake_case wire names rather than inventing PascalCase ones. **Every row already written stays readable — no migration or backfill is required** — while ownership of the format moves from the SDK to this codebase. A wire-shape change now surfaces as a compile error in the new pure `WeatherIngest` mapper (the seam where the SDK stops, mirroring `CatalogIngest`) instead of as silent zeros years later. Neither read path imports an Aydsko namespace any more, and the demo seeder — previously a fourth author of the SDK shape — builds the snapshot too.

### Fixed

- Added the round-trip coverage this mapping never had: SDK wire type → owned snapshot → JSON → read path, for both weather blocks. The mapping previously lived inline in the ingestion worker, which is excluded from coverage as an I/O shell, so the code most likely to break on an SDK upgrade was the code nothing verified. Two further tests read byte-for-byte legacy payloads to hold the persisted contract; verified by mutation, where renaming a single `JsonPropertyName` fails both the format assertion and the legacy read.

## [0.4.44] - 2026-08-09

### Changed

- The iRacing cache key now has one owner, `IRacingCacheKeys`, returning a `CacheSpec(Key, Ttl)` for each of the 15 key families. The key is the interface between the API read paths and the demo seeder, and it was previously a bare interpolated string authored independently in three trees — the services, `DemoCacheSeeder`, and `DemoSeedVerifier` — with test literals as a fourth transcription, kept in step by a doc-comment asking editors to "keep the two in lockstep". That comment named only two of the three authors, and not the one that actually matters: the service that reads. The arrangement had already cost a bug, where the verifier derived expected `laps:` keys with a different filter than the seeder wrote them with and demanded a key that was never seeded. Key and TTL travel together because they are one decision — passing them as separate parameters let a caller take a key from one family and a TTL from another. The per-key TTL guidance that lived in prose is now code.
- `IRacingCacheKeys.DriverSearch` owns the driver-search normalization (trim, lowercase, two-character minimum) and returns null for a term too short to search, so a caller cannot cache a search it should have refused. That rule previously lived as code in `RivalService` and as a doc-comment in the demo seed data asking the author to hand-lowercase every dictionary key, with nothing enforcing that the two matched.
- `CachedIRacingClient` takes `IDataClient?` directly instead of resolving it from an `IServiceProvider`. Nullability already expressed "credentials absent", which was the only reason for service location, so the seam now sits on `IDataClient` — where NSubstitute already operates. **Twelve test files deleted an identical private stub** that returned the same object for any requested type. `IsConfigured`, which had no production callers, is gone. `Program.cs` constructs the client by hand because the SDK client is registered only when all four iRacing credentials are present, and container auto-wiring would fail to resolve the nullable parameter rather than pass null.

### Fixed

- A cold cache could return a 500 under concurrent load. Two callers that both read-missed a key with no row yet both reached the insert, and `CacheKey` is unique, so the loser took a `DbUpdateException` that `ExceptionStatusMapper` has no case for. The most exposed instance was the public `/live` board, whose single global `race-guide` key is uncached on a cold or freshly-purged cache. `GetOrFetchAsync` now handles the conflict for the insert case only — an expiry updates an existing row and cannot collide — and returns the value the losing caller fetched. Reproduced by a test before fixing: with the guard removed it fails with `UNIQUE constraint failed: ExternalDataCaches.CacheKey`.
- Added the round-trip test the seeder suite was missing: seed the demo surface, then read it back through the **real** services constructed with no iRacing client, so a cache miss throws instead of silently falling through to a live fetch. Every other seeder test asserts a hardcoded key literal — a transcription of the format rather than a check against the code that consumes it — so a typo in a service's key would have left them all green while the demo page returned 503. Verified by mutation: pointing one service at a different key fails it. A fourth case asserts an unseeded key throws, so the others cannot pass vacuously against an empty cache.

## [0.4.43] - 2026-08-09

### Changed

- The signed-in session is now one module, `web/src/services/session.ts`, owning the token pair, the claims decoded from it, its persistence and the silent refresh behind `restore`/`adopt`/`clear`/`refresh`/`subscribe`. It previously lived in two places — `api.ts` held `_token`/`_refreshToken`/`tryRefresh` while `AuthProvider` held `user.token`/`refreshTokenRef` and the IndexedDB writes — kept in step by five imperative setters called from ten sites plus two global callback slots. Neither half was a pass-through, so neither could simply be deleted: they were one concept wearing two hats, and the bug surface was the handshake between them, which nothing tested. `AuthProvider` drops 197 → 134 lines and is now a thin React binding; `api.ts` drops 1031 → 975.
- Storage sits behind a `KeyValueStore` seam with two real adapters: IndexedDB in the app, in-memory in tests. The refresh transport is injected too, so `session.ts` has no dependency on `api.ts` — and, more importantly, the refresh call cannot be routed through the intercepting HTTP client, which would call back into `refresh()` on a 401 and recurse.

### Fixed

- Registering a second `AuthProvider` silently disabled the first. `onTokenRefreshed`/`onSessionExpired` were single callback slots, so the last registration won globally and there was no way to deregister — meaning a React StrictMode double-invoke, a multi-provider test, or an unmounted provider all left the app listening on the wrong instance. `subscribe()` now keeps a list and returns an unsubscribe, which `AuthProvider` calls on unmount.
- A request issued during app boot could get a 401 with no retry. The refresh token was installed inside a `.then()` on an asynchronous IndexedDB read, so anything that raced that read found no refresh token and skipped the silent-refresh retry entirely. `restore()` is awaitable, so callers can order themselves against it.
- `login`, `updateSession` and `logout` each wrote a different subset of the session (five things, three, and two respectively), so a caller had to know which — the shape that let the refresh token fall out of step with the access token. `adopt` and `clear` are now the only writers.
- Silent-refresh mechanics — dedup of concurrent attempts, listener notification, refresh-token rotation, and clearing the session when a refresh token is spent — are covered by 29 direct tests instead of being reachable only through an `api` method. `AuthContext.test.tsx` now drives the real session against a mocked storage adapter and asserts the provider follows, rather than reaching in to invoke a captured callback slot.

## [0.4.42] - 2026-08-09

### Changed

- Extracted the HTTP core of the web client into a new `web/src/services/http.ts`, exposed as `createHttpClient({ fetch, getAccessToken, refresh })`. Auth headers, the single silent-refresh retry on 401, RFC-7807 error mapping, the typed 409 not-linked contract and 204 handling all move behind one `request<T>` method, with `fetch` and `refresh` injected rather than reached for. `ApiError`, `IRacingNotLinkedError`, `humanMessageFor` and `throwForResponse` move with it; `api.ts` re-exports the error classes, so all ~90 existing `from '…/services/api'` imports and the entire public `api` surface are unchanged. The full 616-test suite passed against the extraction before any test was touched — this is a refactor, not a rewrite. No runtime behaviour changes.
- Token state and the refresh mechanics deliberately stayed in `api.ts`: the client asks only for the current token and whether a refresh succeeded, so session ownership is untouched. Consolidating it with `AuthProvider` is a separate change.
- Why it was worth doing: `request<T>` was private, so its 401-retry branch could only be reached *through* one of the 50-odd `api` methods. That produced ten near-identical per-verb copies of the same test (GET/POST/PUT/DELETE/postForm/postJson, each in a succeeds/throws pair) — 628 test lines exercising five lines of implementation, with six of them still named for `postJson`/`putJson`/`del` helpers that stopped existing when the client collapsed to a single `request`. Those copies are gone; `http.test.ts` covers the branch verb-independently in 28 direct tests that touch no globals and run in ~18 ms. `api.test.ts` drops 1869 → 1476 lines, `http.ts` lands at 100% statements/functions/lines, and overall branch coverage is unmoved (86.90% → 86.88%).
- Test doubles for `services/api` now go through a shared `web/src/test/apiMock.ts`, adopted by 30 of 32 files. A bare `vi.mock` factory replaces the whole module, so a page's `err instanceof IRacingNotLinkedError` check compares against whatever class the factory invented — the test then passes because it supplied both halves, proving nothing about the real contract. That is exactly how the percentile page's dead 404 branch survived two dedicated tests in 0.4.41. `mockApiModule` keeps every real export and stubs only the `api` methods, so **no hand-rolled `ApiError`/`IRacingNotLinkedError` remains anywhere in the suite** (previously 5 files) and every `instanceof` check is exercised against the real class. It also stubs *all* `api` methods rather than an enumerated subset, so adding a call to a page no longer requires editing its test's mock factory. `AuthContext.test.tsx` and `ThemeContext.test.tsx` keep bespoke factories on purpose — the first mocks module-level token setters, the second needs a capturing implementation of `updateTheme`.

## [0.4.41] - 2026-08-09

### Changed

- The `react-frontend` agent's testing rules now warn against hand-rolling a stand-in `ApiError`/`IRacingNotLinkedError` inside a `vi.mock` factory. Replacing the whole `services/api` module resolves a page's `instanceof` check against the mock's class rather than the real one, so such a test can pass for the wrong reason — which is precisely how the 404 bug below survived two dedicated tests. The rule documents the `importOriginal` spread that keeps the real error exports.

### Fixed

- The percentile page's "no race lap found" state never rendered. `PercentileCarPage` decided a 404 by string-matching `'→ 404'` against the thrown error's message, but that status-line text is only produced when the response body is **not** a JSON object. `PercentileController` returns a bare `NotFound()`, which `[ApiController]` fills in as automatic ProblemDetails, so `humanMessageFor` stops at its `title` and the message is `"Not Found"` — the substring is never present. Every genuine 404 therefore fell through to the generic error branch and showed the bare words "Not Found" instead of the dedicated empty state. It now branches on `err instanceof ApiError && err.status === 404`, using the status `ApiError` has always carried; `ComparePage` already handled its 503 this way. This is the same automatic-ProblemDetails behaviour that produced the raw-JSON login error fixed in 0.4.39 — the second bug from that one root cause.
- The two tests covering that branch could not have caught it: they mocked the rejection as `new Error('GET ... → 404 Not Found')`, a hand-written string the client never actually throws. Because `vi.mock` replaced the whole `services/api` module, the page's `instanceof` check was resolving against the mock rather than the real class, so the test asserted the author's recollection of the contract instead of the contract. The mock now uses `importOriginal` to keep the **real** `ApiError`/`IRacingNotLinkedError` exports and rejects with `new ApiError(404, 'Not Found')` — what the client genuinely produces. Verified by mutation: both tests fail against the old string-matching branch and pass against the fix.
- Twelve data-fetching effects across eight pages could update state after unmount, because they had no cancellation guard — navigating away from a page with a request in flight left the response to resolve into an unmounted component. Fixed in `SeriesPage`, `WeekDetailPage`, `PercentileCarPage`, `MyLapsPage`, `AdminPage` (both effects), `AnalyticsPage` (all three), `DashboardPage` (both, the second covering three parallel fetches), and `ComparePage`. Twenty other effects already used the `let active = true` + cleanup pattern; these had simply been written without it, which is what a convention with no owning module tends to produce.
- `ComparePage`'s debounced driver search had a subtler form of the same defect: its cleanup called only `clearTimeout`, which stops a timer that has not fired yet but does nothing about a search already in flight. It now clears the timer **and** drops a late result.
- `AnalyticsPage` typed two rejection handlers as `.catch((e: Error) => …)`. A rejection is `unknown`, so that annotation was unsound — a non-`Error` rejection would have read `.message` off whatever was thrown. Both now narrow with `instanceof` and fall back to a message.

## [0.4.39] - 2026-08-08

### Added

- Configured the repo's Superpowers-style engineering skills (`triage`, `to-tickets`, `to-spec`, `domain-modeling`, `wayfinder`, and others) with repo-specific config under `docs/agents/`: `issue-tracker.md` (issues live in GitHub Issues at jwh3times/apexracers, via the `gh` CLI), `triage-labels.md` (maps the five canonical triage roles to this repo's label strings, currently identical to the canonical names), and `domain.md` (single-context layout — one `CONTEXT.md` + `docs/adr/` at the repo root, neither of which exist yet). `AGENTS.md` gained a new `## Agent skills` section pointing to these files, and `docs/README.md` documents `docs/agents/` in the doc-ownership map.

### Changed

- Migrated the frontend router from `react-router-dom` to `react-router` v8. This is a package swap plus an import rewrite across 62 files, not a version bump: React Router v8 **dropped** `react-router-dom`, which had existed only to re-export the DOM APIs during the v6→v7 upgrade, and no 8.x of it was ever published — so the advisory below could not be cleared by bumping the package the app actually depended on. Every export in use (`BrowserRouter`, `MemoryRouter`, `Routes`, `Route`, `Outlet`, `Navigate`, `Link`, `NavLink`, `useNavigate`, `useLocation`, `useParams`, `useSearchParams`) keeps its name, and the app uses no `RouterProvider`/`HydratedRouter`, so nothing needed the `react-router/dom` subpath. v8's floors were already met (React ≥ 19.2.7 against the app's 19.2.8; Node ≥ 22.22.0 against the repo-wide 26).
- `docker-compose.yml` now raises the API's per-IP rate limits for the local stack (`AUTH_RATE_LIMIT_PERMIT_PER_MINUTE` 1000, `GLOBAL_RATE_LIMIT_PERMIT_PER_MINUTE` 10000), mirroring what `.github/workflows/e2e.yml` already sets. The documented local E2E loop — `docker compose up` then `npm run test:e2e` — drives the stack in parallel from a single loopback IP, so at the production defaults (10/min and 300/min) the limiter began returning 429 partway through the run. That surfaced as unrelated-looking failures rather than as throttling: a registration would silently stay on `/login`, and four specs failed for what appeared to be routing reasons. Both remain overridable from `.env` to exercise the limiter itself.
- Removed the `shell-quote` npm `overrides` entry added in 0.4.25. `concurrently` 10.0.4 depends on the patched `shell-quote@1.9.0` directly, so the override no longer affects resolution and only obscured the real dependency graph.
- Flipped `scripts/sync-agent-configs.mjs`'s skill direction: `.agents/skills/<name>/**` is now the authored source and `.claude/skills/<name>/**` is generated from it — the opposite of every other row in the Agent Config Sync mapping. A third-party skill installer writes to `.agents/skills/`, so a symlink at `.claude/skills/<name>` pointing back into it was the tempting shortcut to keep both trees in sync — and the actual state of this repo before this change: every installed skill except `ship` was such a symlink. Two independent failure modes rule that out. First, `readdirSync(dir, { withFileTypes: true })` reports a symlink as `isSymbolicLink()`, not `isDirectory()`, so a directory-walking generator (this one included) sees zero skill sources through it and deletes every real file underneath as "orphaned" on the next run — reproduced while building this fix: regenerating against the live symlinks silently wrote the generated banner back into the authored `.agents/skills/*/SKILL.md` sources, because the "generated" output path resolved, via the symlink, to the same file. Second, on a Windows checkout where `git config core.symlinks` is `false`, `git add` walks through the symlink and stages the target's file contents under the link's path instead of recording a link, duplicating every file rather than linking it. The generator now mirrors the whole skill directory (not just `SKILL.md`) byte-for-byte for non-Markdown assets, and injects a `# GENERATED — DO NOT EDIT` YAML-comment banner as line 2 of each generated `SKILL.md`. Added a root `package.json` exposing `npm run sync:agents` (and `-- --check`) as a thin wrapper over the existing `node scripts/sync-agent-configs.mjs` invocation.

### Fixed

- Closed two backend tests that could not fail, found by auditing for the pattern behind the E2E assertion fixed in the previous release. `RivalServiceTests.RemoveAsync_NonExistent_IsNoOp` called `RemoveAsync` against an **empty** table with no assertion, so it proved only "did not throw" — never the no-op its name claims. Confirmed by mutation: it passed against a `RemoveAsync` rewritten to delete every row in the table. It now seeds a rival first and asserts the row survives, and fails against that same mutation.
- `AuthServiceTests.RevokeAsync_UnknownToken_DoesNotThrow` had the same shape with an honest name — it asserted nothing, so it would have passed against a `RevokeAsync` that revoked every token it could find. Renamed to `RevokeAsync_UnknownToken_IsIgnoredAndLeavesValidTokensUsable`; it now holds a live session across the call and asserts that session still refreshes.
- The audit found no other instances. Playwright has no remaining negative web-first assertions (those pass the instant they are evaluated, since the state has not changed yet); every `.rejects`/`.resolves` in the web suite is awaited; and all 18 Vitest negative DOM assertions with no preceding settle point are in mock-free files testing synchronous presentational components, where they are meaningful.
- A failed sign-in showed the user a raw JSON blob instead of a message. `AuthController.LoginAsync` returned a bare `Unauthorized()`, whose automatic ProblemDetails carries only `type`/`title`/`status`/`traceId` — no `detail`. The web client's `throwForResponse` looked for `detail`, found none, and fell back to printing the response body verbatim, so a wrong password rendered `{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.2","title":"Unauthorized","status":401,"traceId":"00-fcbe51ce…"}` on screen. Login now returns `Problem(detail: "Invalid email or password.")` — deliberately ambiguous about which half was wrong, so it can't be used to enumerate accounts.
- Hardened `throwForResponse` against the whole class: a JSON **object** body with no `detail`/`message` now falls back to `title` and then the status line, never to the raw body. Plain-text bodies (e.g. the 423 lockout string) are still shown as-is, and a JSON-encoded string body is unwrapped rather than displayed with its surrounding quotes. This matters beyond login — `AuthController` alone has 8 bare `Unauthorized()` results, with more in the Achievements, Compare, and ProfileStats controllers.
- Fixed a race in the `auth.spec.ts` password-reset E2E test. It clicked **Reset Password** and went straight into `login()`, which begins with a `page.goto()` — that navigation aborted the still-in-flight POST, so the password never changed and the subsequent sign-in failed for a reason that looked nothing like the cause (it surfaced as a Kestrel `Unexpected end of request content`). The test now waits for the on-screen success state first, which also asserts the success UI renders — something nothing previously checked. Verified by holding the request open for 3s: the old form fails every time, the new one passes.
- Replaced a vacuous assertion in the same test. `expect(page).not.toHaveURL(/\/dashboard$/)`, evaluated immediately after clicking sign-in, passed the instant it ran because the URL was still `/login` — it held even when the **correct** password was supplied, so "the old password no longer works" was never actually tested. It now waits for the login response, asserts a 401, and asserts the human-readable error is visible. That missing assertion is why the raw-JSON bug above went unnoticed.
- Client disconnects are no longer reported as server errors. A browser navigating away from a page with requests in flight aborts them, which unwinds as an exception — `ExceptionHandlingMiddleware` had no case for it, so each one was logged at Error as an unhandled exception and answered with a 500 that no client remained to receive. In a local E2E run, **all 25** unhandled exceptions in the API log were disconnects: 14 Npgsql `OperationCanceledException: Query was cancelled` (wrapping `PostgresException 57014`), 10 `OperationCanceledException: The operation was canceled.`, and one Kestrel `BadHttpRequestException: Unexpected end of request content.` The same suite now records 15 of these as 499 with zero Error-level entries.
- The new pure `ClientDisconnectDetector` matches those exception types **only** when `HttpContext.RequestAborted` is signalled, so a server-side timeout or a genuinely malformed request keeps its existing 500/400 and its Error log — the cancellation token is what separates "the connection dropped" from "we failed". `ExceptionHandlingMiddleware` records a disconnect at Debug, sets **499** (nginx's "Client Closed Request"), and writes no body; `RequestLoggingMiddleware` logs 499 at Information rather than letting it fall into the `>= 400` Warning bucket. Observability-only — no response a client can still receive changes.

### Security

- Cleared the two remaining high-severity advisories failing the `npm audit (web)` job, returning it to zero vulnerabilities:
  - `react-router` updated to 8.3.0, resolving an RSC-mode CSRF bypass that allows action execution before the 400 response (GHSA-qwww-vcr4-c8h2; affects ≥ 7.12.0, < 8.3.0). The advisory only reaches applications using the unstable RSC APIs — ApexRacers is a client-side SPA (`BrowserRouter`, no framework mode, no server actions), so it was never exposed. The upgrade clears the audit gate rather than closing a reachable hole.
  - `brace-expansion` updated 5.0.7 → 5.0.8, resolving a DoS via unbounded expansion length causing an out-of-memory crash (GHSA-mh99-v99m-4gvg). Dev-only, reaching the tree through `minimatch`. The 0.4.25 bump to 5.0.7 did not clear it: the advisory's affected range was later widened to include that version.
- `brace-expansion` updated 5.0.8 → 5.0.9, resolving a further DoS via unbounded intermediate arrays that bypasses the CVE-2026-14257 mitigation the 5.0.8 bump above relied on (GHSA-rgw5-rvv9-x895; affected range 4.0.0 – 5.0.8, so the previous fix landed squarely inside it). Same dev-only path through `eslint` → `minimatch`. `npm audit fix` resolved it entirely within the existing `package.json` semver range — only `package-lock.json` changed.
- `nanoid` updated 3.3.16 → 3.3.18, resolving custom generators looping indefinitely when passed a size of zero (GHSA-2v37-7h3g-55p8; affected range < 3.3.17). Dev-only, reaching the tree through `postcss`. `npm audit fix` resolved it entirely within the existing `package.json` semver range — only `package-lock.json` changed.

## [0.4.25] - 2026-07-24

### Added

- Codex parity for the repo's agent tooling, generated from the existing Claude Code sources so the two tools cannot drift. `scripts/sync-agent-configs.mjs` renders `.claude/agents/*.md` to `.codex/agents/*.toml`, and mirrors `.claude/skills/*/SKILL.md` and `.claude/hooks/*` to the paths Codex discovers (`.agents/skills/`, `.codex/hooks/`). A `tools:` list without `Write`/`Edit` becomes `sandbox_mode = "read-only"`; `model:` is dropped, since Claude model names are not Codex model names. The generator self-validates: it round-trips each generated TOML through an independent parser to catch an escaping regression, and lints the mirrored prose for `claude`→`Codex` substitution artifacts and relative links that would break at the mirrored path depth.
- An **Agent Config Sync** CI check (`.github/workflows/agent-config-sync.yml`) that re-runs the generator with `--check` and fails a PR whose generated tree has drifted from its sources or that leaves an orphaned generated file behind.
- `.codex/config.toml`, the Codex counterpart to Claude Code's `.claude/settings.json` permissions: `sandbox_mode = "workspace-write"` + `approval_policy = "on-request"` with `network_access = true` so the .NET/npm dev loop (restore, install) works while Codex still prompts before acting outside the workspace.

### Changed

- Agent and skill prose is now tool-neutral, so the generator can copy it verbatim instead of find/replacing tool names — the previous hand-made Codex files had been produced by a blind `claude`→`Codex` substitution that emitted broken paths (`.Codex/agents/*.md`), broken doc links (`code.Codex.com`), and self-referential sentences ("the canonical guide is `AGENTS.md`; `AGENTS.md` is only a bare `@AGENTS.md` import").
- The shared agent session-start hook now gates on capability (`apt-get` present and .NET 10 absent) instead of the Claude-specific `CLAUDE_CODE_REMOTE` env var, so the one script correctly bootstraps the .NET 10 SDK under both Claude Code's web sandbox and Codex cloud, and no-ops on local machines. Codex exposes no cloud/remote indicator to key off, and the capability gate needs none.

### Security

- Cleared both high-severity advisories failing the `npm audit (web)` job, which is now back to zero vulnerabilities. Both dependencies are dev-only and never shipped to users:
  - `shell-quote` forced to the patched `^1.9.0` via an npm `overrides` entry, resolving a quadratic-complexity DoS in `parse()` (GHSA-395f-4hp3-45gv). The advisory reaches the tree through `concurrently`, which exact-pins `shell-quote@1.8.4` on its 10.x line, so no bump of `concurrently` could resolve it; the override keeps `concurrently` current instead of downgrading it to 9.2.4 as `npm audit fix --force` proposes.
  - `brace-expansion` updated 5.0.6 → 5.0.7, resolving a DoS via exponential-time expansion of consecutive non-expanding `{}` groups (GHSA-3jxr-9vmj-r5cp). It reaches the tree through `eslint` → `minimatch`.

### Fixed

- Pinned the app's base sans-serif font (`--font-sans` → Inter) so the default typeface no longer inherits Tailwind's Preflight default, whose value changed in `tailwindcss` 4.3.3 and shifted rendering on every element that sets no font family of its own (notably the public landing, terms, and privacy pages).
- Pinned the base monospace font (`--font-mono`) to the generic stack it already resolved to, so — like `--font-sans` — it is app-owned and immune to future changes in Tailwind's Preflight default. No visual change; purely defensive.
- Fixed the public Terms of Service and Privacy Policy pages, whose title and body text used typography utility classes (`font-display-sm`/`text-display-sm` and `font-body-md`/`text-body-md`) that have no matching design-system tokens and therefore emitted no CSS — the text rendered with no defined size or font family. Remapped the page titles to the shared `.text-page-title` style and the body copy to the existing `body-lg` token.

## [0.4.15] - 2026-07-16

### Added

- Public documentation taxonomy, feature overview, and high-level roadmap pages under `docs/`.
- Structured per-request logging middleware (method, path, status code, elapsed time, client IP; log level scales with status), complementing the Application Insights codeless auto-instrumentation already active on the API App Service.

### Fixed

- Per-IP rate limiting now partitions by the real client IP behind the App Service front end (via the `ASPNETCORE_FORWARDEDHEADERS_ENABLED` forwarded-headers app setting), instead of collapsing all clients into one shared bucket.

### Changed

- Public and agent docs now separate public setup/product guidance from maintainer-only planning, deployment, and security runbooks.
- Main-branch release automation now creates standard SemVer `<major>.<minor>.<build>` tags and GitHub Releases, auto-incrementing the build per major/minor line while preserving intentional `x.y.0` bumps.
- Contributor workflow: added a `/ship` skill (`.claude/skills/ship/`) that refreshes docs, rolls `[Unreleased]` into a CHANGELOG section dated for the version the merge will mint, runs the fast checks (Prettier, ESLint, `npm run build`, `dotnet build`), and opens or updates the PR. The mint version now comes from a single `scripts/next-version.sh` helper that the release workflow (`version.yml`) also calls, and a new **Changelog Version** CI check (`.github/workflows/changelog-version.yml`) fails a PR whose dated CHANGELOG section has drifted from that version. This replaces the per-turn docs-freshness Stop hook, which has been removed.

## [0.3.0] - 2026-07-04

### Added

- Baseline security response headers on every API and SPA response (content-type sniffing protection, frame denial, referrer and permissions policies, HSTS over HTTPS) via a unit-tested middleware.
- Global per-IP API rate limit (300 requests/minute, fixed window) as a safety net in front of every endpoint; the stricter per-IP auth limit is unchanged.
- Health endpoints: `/healthz` (liveness) and `/ready` (database readiness), both anonymous and exempt from rate limiting.
- CI dependency-vulnerability audit workflow (npm audit + `dotnet list package --vulnerable`), running on PRs and weekly; non-blocking for now.
- `/admin` accessibility audit in the E2E suite — the panel is provisioned by promoting an in-test-registered user to Admin, then audited with axe-core (zero WCAG 2.1 A/AA violations).
- Seeder `--ci` mode that seeds a fully synthetic catalog (no captured iRacing response objects required) and auto-applies pending migrations, enabling demo-data seeding in CI.
- Accessibility (axe-core WCAG 2.1 A/AA) audits across all 18 iRacing-gated routes, rendered against synthetic demo data in CI.
- `/analytics` first-visit empty state now offers a "Compute my percentiles" action that computes and populates percentile data inline, instead of requiring a prior visit to Recommendations.
- Typed `ApiError` (carrying the HTTP status) in the frontend API client, and a guided "search unavailable" hint on `/compare` that distinguishes a 503 (search backend unavailable) from "no drivers matched" — with demo mode naming the searchable sample drivers.
- Seeder `--verify-demo` / `--verify-teardown` gates — mechanical exit-code checks that the demo surface is fully seeded (a prod `iracing-demo` rollout precondition) or fully torn down (the M2 purge check); `--demo` now self-verifies at the end.
- E2E functional specs — logout/session-protection and password-reset (via the Development token echo) auth flows, and `.ibt` telemetry upload → My Laps — plus a feature-flag gating spec that restores the ComingSoonPage axe audit (asserting gated routes render synthetic demo content when the flag is on, and ComingSoon when off).
- Anonymous/guest feature-flag read: `GET /api/feature-flags` is now public and returns the enabled Standard-tier flag set to signed-out visitors (a GA prerequisite so flag-gated public pages render for guests once `iracing-live` is enabled); the frontend flag provider fetches under a `guest` owner.
- A `--color-gold` design token (Tailwind `text-gold`/`bg-gold`/`border-gold`/`shadow-gold`) replacing hardcoded `#FFD700` across the analytics/profile/settings UI.
- CI-only Playwright visual-regression suite for the stable public pages (`/`, `/login`, `/terms`, `/privacy`) with committed Linux/Chromium screenshot baselines, refreshable via an `e2e.yml` `workflow_dispatch` input.

### Fixed

- Extended light/dark WCAG 2.1 AA link-distinction — a persistent `underline` on inline accent links —
  across Profile, Progression, Analytics, Recommendations, Races, Percentile, and Compare (WCAG 1.4.1);
  extended full-strength muted-text contrast (`on-surface-variant`) to Admin, Series, and Percentile
  (WCAG 1.4.3); and added `/reset-password` and `/verify-email` to the axe audit set.
- Corrected the PR-template coverage checklist figure (80% → 85%) and two stale demo-gating code comments
  (Dashboard/Profile fetch guards reference the live-OR-demo flag check they actually use).
- Admin panel role and minimum-role dropdowns now have accessible names (WCAG 2.1 select-name); the /admin E2E axe audit enforces this.
- Accessibility on iRacing-gated pages — replaced hardcoded red iRating/SR deltas with the semantic error token (darkened for light-mode AA contrast) on Progression, Races, Race Detail, and Compare, and removed a low-contrast opacity on the Strategy weather line.
- `/live` race board no longer shows misleading absolute start times for perpetually-live (sentinel/stale) sessions — a session "live" for over 24 hours renders `—` instead of a bogus start time.

### Changed

- Pinned the local pgAdmin image to a specific version tag (was `latest`) so Dependabot can track it.
- The per-IP auth rate limit is now configurable via `AUTH_RATE_LIMIT_PERMIT_PER_MINUTE` (default 10, unchanged in production).
- The global per-IP rate limit is now configurable via `GLOBAL_RATE_LIMIT_PERMIT_PER_MINUTE` (default 300, unchanged in production).

### Removed

- Retired `docs/IMPLEMENTATION_PLAN.md` — a committed roadmap snapshot now reconciled into the
  maintainer's local planning docs.
- Deleted the stale GT3 SQL seed scripts (`seed_gt3_series.sql`, `remove_gt3_seed.sql`) — they targeted
  the pre-June-2026 `LapTimeEntries` schema and no longer run; the Seeder's `--ci` mode replaces them.
- Removed the dead, unused `.tier-badge-gold` / `.tier-badge-green` CSS utility rules (zero usages).

## [0.2.0] - 2026-06-30

### Added

- Axe-core (`@axe-core/playwright`) accessibility audits in the Playwright E2E suite — asserts zero
  WCAG 2.1 A/AA violations across the public + authenticated page set (5 public + 7 authed pages);
  runs in the existing non-blocking E2E workflow.

### Fixed

- Light-mode color contrast now meets WCAG 2.1 AA — darkened the cyan accent tokens
  (`primary-container`, `primary-fixed-dim`, `primary`, `secondary-fixed-dim`, and companion fill/ink
  tokens) in the `html.theme-light` / `prefers-color-scheme: light` overrides in `index.css`; dropped
  the low-opacity modifier on hero stat captions and footer text; and added a persistent underline to
  inline links on the Privacy, Dashboard, My Laps, and Profile pages. Dark-mode accent colors are unchanged.

## [0.1.0] - 2026-06-30

The first feature release since the initial production deployment. It lands the
full iRacing member/race/competition feature set, the launch-gating and demo-data
preview system, account-security and transactional-email improvements, and a
substantial testing and tooling uplift.

> **Note:** every iRacing-data-backed feature ships behind the seeded-disabled
> `iracing-live` feature flag and is non-functional in production until iRacing
> service-account OAuth credentials are available. See the project roadmap.

### Added

#### iRacing member, race & profile insights

- **Progression** — per-category iRating, Safety Rating, CPI, and Time Trial
  ratings, with iRating history.
- **Driver profile** — enriched profile with identity, per-category license
  badges (with safety rating), career stats, a this-year summary, and recap
  favorites.
- **Race history** — recent official races, with car names resolved from the
  local catalog.
- **Race detail** — the full classified field for a subsession (public), plus an
  authenticated per-lap pace trace.
- **Achievements** — an awards/trophy case on the profile.

#### Schedule, records & competition

- **Season schedule** — active-season schedule with track, weather, and
  Balance-of-Performance per week, plus a personal-best overlay.
- **World records & leaderboards** — fastest car+track lap overlays and a global
  top-200 leaderboard by license category.
- **Standings** — championship, time-trial, and qualifying standings per car
  class (qualifying lap times parsed directly from result chunk files).
- **Race Now guide** — a board of official sessions starting in the next few
  hours.

#### Head-to-head, catalog & strategy

- **Compare & rivals** — driver-vs-driver head-to-head, plus following rivals
  (add/remove/search/suggestions).
- **Catalog explorer** — a browsable car and track catalog with detail pages and
  a "your best laps" overlay.
- **Strategy briefing** — per-week track/pit, weather risk, and per-car BoP +
  shift analysis (public; personalizes when signed in).

#### Accounts & security

- **Password management** — authenticated password change, plus public,
  enumeration-safe forgot/reset password with email-delivered links.
- **Verified email change** — a request → emailed confirmation link → confirm
  flow, with a security notice sent to the old address; active sessions are
  revoked on change.
- **Transactional email** — delivery via Azure Communication Services, with a
  logging fallback when unconfigured.
- **Refresh-token cap** — active refresh tokens are capped per user, revoking the
  oldest past the cap.

#### Launch gating & demo data

- **iRacing-live feature flag** — gates the entire iRacing-dependent surface;
  gated routes render a "Coming Soon" page and their nav items hide until the flag
  is enabled (no redeploy required to flip it).
- **iRacing demo preview** — an `iracing-demo` flag plus a synthetic-data seeder
  (`--demo`) that lets the full product be previewed without live iRacing
  credentials.

#### Platform

- **On-demand iRacing data layer** — a cached client over the iRacing API
  (`CachedIRacingClient` + `ExternalDataCache`) memoizing mapped DTOs with
  per-call TTLs, plus background cleanup of long-expired rows.
- **RFC-7807 error contract** — unhandled exceptions are returned as
  `application/problem+json` with a consistent status mapping.
- **Typed "iRacing not linked" response** — iRacing-linked endpoints return a
  typed `409` when the caller has not linked a customer ID.

#### UI & dashboard

- A support page, top-nav breadcrumbs, a dashboard KPI row, a notifications bell
  with client-derived alerts, a collapsible sidebar/icon rail, and a "Your pct"
  column on week detail.

#### Testing & tooling

- **Playwright E2E** — a harness with a register→dashboard smoke test and a
  non-blocking per-PR E2E CI workflow.

### Changed

- Test coverage gates raised from 80% to 85% (backend line and branch; frontend
  statements, branches, functions, and lines).
- Users are now restricted to a single role at a time (highest tier wins;
  enforced by a database unique index).
- Profile updates no longer change email directly — email changes go through the
  verified email-change flow.
- Backend tests now run against in-memory SQLite (a real relational provider) to
  validate SQL translatability.
- The frontend moved to the repository root and adopted a feature-based
  structure, with tests colocated alongside their modules.
- Numerous dependency updates across npm, NuGet, and GitHub Actions (grouped
  Dependabot).

### Security

- Local development stack ports are bound to `127.0.0.1` instead of all
  interfaces.
- Password reset revokes all of a user's active refresh tokens; reset and
  verification tokens are never logged.

## [0.0.1] - 2026-06-16

Initial release — the version currently deployed to production
(<https://apexracers.gg>).

### Added

#### Platform

- Lap time percentile tracking and car recommendations for iRacing weekly series.
- ASP.NET Core Web API with use-case-oriented controllers backed by focused
  service classes.
- PostgreSQL persistence via EF Core, with the full iRacing catalog modeled
  (series, seasons, weeks, tracks, cars, car classes, subsessions, and results).

#### Authentication & accounts

- User registration and login issuing JWT access tokens and rotating refresh
  tokens.
- Refresh token rotation, logout/revocation, profile updates, and theme
  preference persistence.
- iRacing OAuth 2.0 callback handling.
- Role-based access control with an `AdminOnly` policy.

#### Features

- **Series** — browse active weekly series.
- **Week detail** — cars and aggregate lap stats for a series week.
- **Percentile** — a driver's lap time percentile for a specific car and week,
  computed and cached.
- **Recommendations** — ranked car recommendations for the authenticated user.
- **Analytics** — per-car percentile history and stats.
- **Telemetry** — iRacing `.ibt` file upload with lap extraction, plus personal
  best laps per track and car.
- **Admin** — user role management and feature flag CRUD.
- **Feature flags** — per-user flag evaluation based on role.

#### Data ingestion & seeding

- Standalone ingestion background worker that pulls data from the iRacing API
  via `Aydsko.iRacingData`.
- Idempotent CLI seeder that loads catalog data and generates synthetic lap time
  data across all series for a usable UI without live data.

#### Frontend

- Vite + React + TypeScript single-page app with a typed API client.
- Public marketing landing page, login, terms, and privacy pages.
- Authenticated app shell (sidebar + top nav + footer) with dashboard, series,
  week detail, percentile, analytics, recommendations, my laps, telemetry,
  profile, settings, and admin pages.
- Fluid design system that scales with viewport width, plus light/dark/auto
  theming.

#### Infrastructure & tooling

- Docker Compose stack (PostgreSQL, pgAdmin, API) for local development.
- Azure deployment: API on App Service, ingestion worker as a Container App,
  with Azure Container Registry, Key Vault, and PostgreSQL Flexible Server.
- GitHub Actions CI/CD with enforced 80% test coverage gates (line and branch
  for the backend; statements, branches, functions, and lines for the frontend),
  Prettier formatting checks, and Dependabot dependency updates.

#### Project documentation

- README, contribution guidelines, code of conduct, support guide, and security
  policy.
- Licensed under the GNU Affero General Public License v3.0.

[Unreleased]: https://github.com/jwh3times/apexracers/compare/v0.4.46...HEAD
[0.4.46]: https://github.com/jwh3times/apexracers/compare/v0.4.45...v0.4.46
[0.4.45]: https://github.com/jwh3times/apexracers/compare/v0.4.44...v0.4.45
[0.4.44]: https://github.com/jwh3times/apexracers/compare/v0.4.43...v0.4.44
[0.4.43]: https://github.com/jwh3times/apexracers/compare/v0.4.42...v0.4.43
[0.4.42]: https://github.com/jwh3times/apexracers/compare/v0.4.41...v0.4.42
[0.4.41]: https://github.com/jwh3times/apexracers/compare/v0.4.40...v0.4.41
[0.4.39]: https://github.com/jwh3times/apexracers/compare/v0.4.38...v0.4.39
[0.4.25]: https://github.com/jwh3times/apexracers/compare/v0.4.24...v0.4.25
[0.4.15]: https://github.com/jwh3times/apexracers/compare/v0.4.14...v0.4.15
[0.3.0]: https://github.com/jwh3times/apexracers/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/jwh3times/apexracers/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/jwh3times/apexracers/compare/v0.0.1...v0.1.0
[0.0.1]: https://github.com/jwh3times/apexracers/releases/tag/v0.0.1
