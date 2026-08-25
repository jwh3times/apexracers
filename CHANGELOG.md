# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Percentile badges are no longer presented for Fields smaller than five Drivers. Percentile,
  recommendation, strategy, analytics, week-detail, and dashboard surfaces instead report the
  known Field Size or omit the undersized reading.

## [1.0.2] - 2026-08-24

### Removed

- Removed the repository-scoped Codex sandbox configuration and lifecycle hooks, so Codex sessions now use each developer's own configuration without the project overriding permissions or running a session bootstrap hook. Agent and skill generation remains shared across Claude Code and Codex, while hooks remain specific to Claude Code.

## [1.0.0] - 2026-08-24

### Added

- Contributor workflow: an `/end-session` skill (`.agents/skills/end-session/`) that closes out a work session — it records what the session learned into memory, updates the GitHub issues it touched and the maintainer-only planning docs, and clears the regenerable coverage, test-output, and worktree leftovers out of the working tree. It stops well short of `/ship`: it never opens a PR, rolls `[Unreleased]` into a dated section, or pushes, and it refuses blanket ignored-file deletes so the gitignored environment files and the captured iRacing response objects — which cannot be re-fetched while the credentials blocker stands — survive the cleanup.

### Fixed

- Race Weeks no longer label any Uploaded Lap at the Track as "Your PB here." The track-familiarity badge now says "Uploaded lap here," and the API field is renamed from `hasPersonalBest` to `hasUploadedLapAtTrack` instead of claiming that lap is the caller's Personal Best for a Car and Race Week.

## [0.9.0] - 2026-08-20

### Changed

- **Uploaded telemetry no longer calls its best lap a "personal best."** A personal best is the fastest lap known for a driver from whichever evidence they allow to count, and it is what gets ranked against a field. The telemetry surfaces never saw a race lap at all — they only ever showed the fastest lap a driver had *uploaded* — so a driver with race data and no uploads read "—" under a heading claiming to be their personal best. My Laps, the profile page, the dashboard, and the car and track pages now say "uploaded best" and mean it.
- The stored entity, its table, and the service, query, enum, and DTO names behind those surfaces use the uploaded-lap language too, so the code says what the rows are rather than what they might turn out to be.

### Removed

- **Two request parameters were renamed:** `includePersonalLaps` is now `includeUploadedLaps`, and `personalLapTypes` is now `uploadedLapTypes`, on the percentile, recommendation, week-detail, and analytics endpoints. The car and track detail responses rename `yourBestLaps` to `yourUploadedBests`. Any client that hardcoded the old names must update; the app's own frontend was updated in step.

## [0.8.2] - 2026-08-20

### Fixed

- **A personal best is now drawn from the race week it is ranked against.** A race best already belongs to one race week, but the uploaded lap it was compared with was the driver's fastest ever at that track — so a dry practice lap from a previous season, on a different build and a different BoP, could take the percentile from a wet race week's race best, and nothing on the page said it had. An uploaded lap now counts toward a race week only when it was driven inside it, which makes both sides of the comparison mean the same thing. This applies to the percentile breakdown, car recommendations, and per-car analytics.
- The race week's closing time comes from iRacing rather than being assumed. A week closes at 21:00 UTC on its seventh day, not at midnight, so treating it as seven days would have admitted laps for three hours after the week had actually ended.

### Added

- The percentile breakdown says when the race week's bound left out a faster uploaded lap, naming the lap and the day it was driven, instead of quietly dropping it. It stays silent when nothing was left out, or when the excluded lap was slower and would have changed nothing.

### Changed

- Uploaded best laps shown for their own sake — on My Laps and on the car and track pages — are unchanged and remain all-time. Only the personal best that gets ranked against a field is bounded to a race week.

## [0.8.1] - 2026-08-20

### Added

- The contributor and agent guides now record how a test filter actually reaches the runner. `global.json` selects the Microsoft Testing Platform, so `dotnet test` hands any option it does not own straight to the test executable — which means `--filter-class` needs a fully qualified type name, and `--nologo` is rejected by the runner and surfaces as `Zero tests ran` with exit code 5. That failure reads as a broken filter rather than as a bad flag, and it cost an investigation to tell the two apart; both rules now sit beside the commands they govern, along with the pointer to the runner's own `--help` for the full filter vocabulary.

### Fixed

- The agent-config sync check no longer reports permanent drift over line endings alone. It compared each skill's `SKILL.md` as normalized text but every other file in the generated tree byte-for-byte, so any tool that rewrote `.claude/skills/` with Windows line endings after checkout left those files failing `--check` indefinitely, with no content difference to point at. Committed content was never affected — git normalizes on the way in, so the repository and CI stayed correct and the failure was visible only to a Windows working copy. Text files are now normalized the way hook scripts already were, and anything that is not text still copies byte-for-byte, so a future binary asset is untouched.

## [0.8.0] - 2026-08-20

### Added

- **A personal best now says which evidence produced it.** A driver's best lap is chosen from two kinds of evidence that are never interchangeable — a race lap set in an official race, or a lap from telemetry they uploaded themselves — and until now the choice was made and then thrown away, leaving a bare number. That mattered because the field a driver is ranked against is composed entirely of race laps: a percentile could rest on a dry practice lap uploaded in a previous season and read exactly like one set wheel-to-wheel in this week's race. The percentile breakdown, recommendations, and per-car analytics now name the evidence beside the lap, so the two can be told apart.
- Percentile, recommendation, and analytics responses carry the evidence alongside the lap it describes. On recommendations and analytics it is absent exactly when the lap is — a car the driver holds no lap for has nothing to attribute.

### Changed

- The race-versus-uploaded comparison is made in one place instead of three. Each service previously ran its own "is the uploaded lap faster" test and kept only the winning number, which is why no response could report where the lap came from. The choice and the record of which evidence won are now a single decision, and a tie goes to the race lap — it was set against the field being ranked, so it is the better-evidenced of two equally fast laps. This preserves the behaviour all three call sites already happened to share, now as a stated rule rather than a coincidence of three separate comparisons.

## [0.7.1] - 2026-08-20

### Fixed

- **Race detail no longer presents a partial field as the whole classification.** A result names exactly one Driver, so a team entry — which races under a team's identity rather than one Customer ID — produces none, and neither does an AI entry. Both were silently dropped at ingest while still having held finishing positions, which left the classified field with unexplained gaps and lead-lap and interval context computed over fewer cars than actually raced. A Race now records how many entries it could not represent, of each kind, and the race-detail page says so beneath the results heading. A complete field carries no caveat, and a Race ingested before the counts existed reports them as unknown rather than claiming to be complete.
- The race-detail response no longer describes itself as the "full classified field." It returns the individually classified Drivers, and now carries the counts that say what is missing alongside them.

## [0.7.0] - 2026-08-19

### Added

- Race detail now shows which Split of its Race Session a race was — "Split 1 of 3" — beside Strength of Field, so a result can be read against the Split it was set in rather than against the Race Session as a whole. The tile is omitted entirely when the Split's position is unknown, rather than guessing at one.
- `CONTEXT.md` defines **Split Number** as the one-based counterpart of the zero-based **Split Index**, used only where a Split's position is shown to a reader. Storage, the API, and request parameters carry the Index; the Number exists at the display boundary alone, exactly as Race Week Index and Race Week Number are separated.

### Fixed

- **A Subsession's Split Index no longer conflates "the strongest Split" with "we don't know."** The stored value was `0` in three unrelated situations: the Subsession really was the strongest Split, iRacing supplied no Splits at all, or it supplied a list the Subsession was absent from. Index 0 is the one value carrying a strong claim — the top Split, the strongest field a Driver could have been sorted into — so overloading it made every reading of it unfalsifiable. The position is now nullable, and unknown is a distinct value that no longer reads as the strongest Split.
- **The Split Index is derived from Strength of Field rather than from the order iRacing lists Splits in.** Each entry of a Race Session's split list reports its own Strength of Field, so the position is now computed by ranking on that value, with equal Strength of Field broken by ascending subsession identifier. Nothing in iRacing's contract promised the list arrives sorted, and an out-of-order payload would previously have labelled a weak Split the strongest. A Subsession absent from its own split list is recorded with an unknown position and logged as a payload that disagrees with itself. Recorded as `docs/adr/0003-split-index-is-derived-from-strength-of-field.md`.

### Removed

- The `SplitNum` column is dropped rather than renamed, because no stored value could be carried across honestly — every `0` in it was ambiguous between three meanings. Subsessions ingested before this release read as an unknown Split position until they are ingested again; no read path consumed the column, so nothing downstream changes.

## [0.6.9] - 2026-08-17

### Changed

- ApexRacers-owned identity names now follow the domain language: `SubjectDriverContext` resolves the requesting User's Subject Driver, `DriverStatsService` assembles Driver statistics, and progression uses `DriverProgressionDto` / `DriverProgression`. Aydsko SDK types that mirror iRacing's `member_*` endpoints retain their upstream names, and no HTTP route, JSON field, or response shape changed.

## [0.6.8] - 2026-08-16

### Fixed

- Personal Best evidence now has one consistent application-wide choice across percentile detail, Week Detail's "Your pct," recommendations, analytics, dashboard summaries, and alerts. Official race laps are the default; drivers can opt in uploaded laps and optionally filter them by session type from any pace-source selector, and that choice follows them between pages until the signed-in user changes.

## [0.6.7] - 2026-08-16

### Fixed

- Claiming an iRacing Customer ID already claimed by another account now returns a non-disclosing `409 Conflict` with an actionable message instead of an opaque 500. The filtered database unique index remains the race-safe source of truth, including for concurrent claims.

## [0.6.5] - 2026-08-14

### Changed

- The `docs-updater` agent's drift-detection rules now record that `private/` is gitignored, so git history is never evidence about the maintainer-only docs. Every commit looks as though it left `private/archive.md` untouched, because none of them can touch it observably — and three consecutive sessions each skipped the completed-work log by citing the previous session's apparent skip, leaving seven shipped issues unrecorded before anyone read the file. The agent is now told to compare the archive's newest headings against recent merge history instead of inferring a cadence from a method that cannot observe the file.

## [0.6.4] - 2026-08-14

### Fixed

- The "Unique Tracks" count on My Laps counted distinct track *names*, so laps driven on all four Homestead Miami Speedway layouts reported as one track. It now counts the tracks themselves.

### Changed

- Uploaded best laps are grouped by car and track identifier instead of by the car and track display names. The names had kept the layouts apart only because iRacing happens to label every configuration distinctly today — nothing enforced it, and eight tracks are already named with a trailing space that any later cleanup would have merged. Best-lap rows on My Laps and on a car's detail page now also link to the track they were set at.

## [0.6.3] - 2026-08-14

### Fixed

- Recent races now say which track a race was run at. iRacing's history payload gives a track identifier and a name but no configuration, and only the name was kept — so a race at the Nordschleife's Industriefahrten layout and one at its Touristenfahrten layout both read "Nürburgring Nordschleife" with nothing to tell them apart. The identifier is now kept, the configuration is resolved from the local catalog the way the car name already was, and each row links to the track it was run at. A track iRacing named but the catalog has not ingested still shows its name, without a configuration.

## [0.6.2] - 2026-08-13

### Fixed

- Head-to-head track pace no longer merges different layouts at the same venue. Best laps were grouped by track name, so a lap set on Homestead's 1.50-mile oval could be compared against one set on its 2.30-mile road course and reported as faster; the eight tracks whose names carry a trailing space were also split into rows of their own. Pace is now grouped by track identifier, and each row names its configuration and links to that track.

## [0.6.1] - 2026-08-13

### Security

- Telemetry recorded by a different driver is no longer accepted as your own pace. An `.ibt` file names the driver who recorded it, and that name was previously read, shown back on the upload page, and then discarded — so a file driven by anyone could be uploaded, stored as the uploader's laps, and ranked against a field of real race results under their account. An upload whose recording driver disagrees with the iRacing customer ID linked to the account is now refused, with a message naming both and pointing at Settings. The check runs before anything is written, so a refused upload leaves nothing behind.

### Added

- Uploaded laps record the driver the telemetry named, so a lap now says whose it is. Where the file names nobody the value is stored as unknown rather than as customer 0. Uploading still works without a linked iRacing customer ID — there is simply nothing to check the file against, and the recording driver is stored either way.

### Changed

- Laps uploaded before this release carry no recording driver and cannot be backfilled, since the value was never captured. They are indistinguishable from laps whose file named no driver, and both read as "not established".

## [0.6.0] - 2026-08-13

### Changed

- **Percentile rank is now a true percentile rank.** It was previously the share of *other* drivers a driver had beaten, which meant beating everyone you were compared with reported 100 however small that group was — and a driver alone in a field, having beaten nobody at all, was also shown 100. A percentile is now computed over the whole field with the driver counted in it and drivers on an identical lap splitting the tie, so it no longer reaches 0 or 100 and a lone driver sits at the median of their field of one. Every displayed percentile changes.
- **"TOP X%" now means a placement in the field rather than the complement of a percentile.** Fastest of two drivers is the top 50%, not the top 1%. The share is computed where the field is known and carried through the API, because a percentile rank cannot be turned back into a placement once ties have been split.
- The percentile page reports the driver's actual position and field size instead of estimating them from the percentile, and no longer labels the driver count "Laps analysed" — it is drivers, one best lap each. Its field best and field median are now taken over the same field the percentile was computed against, so a driver whose uploaded lap leads the field sees a zero gap to the field best rather than a negative one.
- The recommendations page shows a percentile as a percentage. It previously rendered it as an ordinal — a 92.3 percentile read as "92.3th".
- Sample size now consistently means the number of drivers in the field, counting the driver themselves. It previously counted the driver only when they had raced, so the same field meant two different populations depending on whether the lap came from a race or an upload.

### Added

- `CONTEXT.md` now defines ApexRacers' competitiveness vocabulary, separating three shapes of number that were all called a rank. A **Field** is the drivers a **Subject Driver** is measured against for one car and race week, one personal best each and the subject included; **Percentile Rank** is their share of it; **Field Position** is their place in it; **Top Share** is that position as a share of the whole field. An **Expected Percentile** averages past readings for a car the driver has not raced this week and so has no position behind it, a **Projected Lap** is a pace estimate rather than a lap anyone drove, a **Recommendation Rank** orders cars rather than drivers, and a **Standing** is a position iRacing awards. *Rank* unqualified is recorded as a term to avoid.
- Percentile responses carry the driver's field position and top share, and the analytics, dashboard, strategy, and week-detail surfaces read the share from the API rather than deriving it. Cached percentile rows store the share alongside the rank for the same reason.

### Fixed

- Cached percentile rows computed under the previous formula are cleared on upgrade. They are recomputed on the next visit; left in place they would have been blended with new readings in the running average behind car recommendations, mixing two conventions in one number.

## [0.5.20] - 2026-08-13

### Added

- `CONTEXT.md` now defines ApexRacers' track vocabulary, separating where racing happens from what is driven. A **Venue** is a physical facility that several **Tracks** may share; a Track is one drivable configuration at a Venue, addressed by its iRacing track identifier, and that identifier is the only thing that makes two lap times comparable. A **Track Name** belongs to the Venue rather than the Track — 95 of iRacing's 463 track identifiers share a name with another, so Homestead Miami Speedway is one name over a 1.5-mile oval, two road courses, and an open-wheel oval — and is never an identity. A **Configuration Name** is frequently absent, which costs no identity. A **Retired Track** keeps its identifier and everything driven at it.
- A second architecture decision record, documenting why track identity follows iRacing's track identifier — so a rebuilt or rescanned layout is a different Track rather than a continuation of the old one — and what follows from that: a driver's history at a venue splits when iRacing rebuilds, venue is not a stored concept, and grouping on a track name silently merges unrelated layouts.

## [0.5.19] - 2026-08-13

### Added

- `CONTEXT.md` now defines ApexRacers' lap-evidence vocabulary, separating how a lap reaches the product from what is chosen out of it. A **Race Lap** comes from iRacing's results and belongs to a **Driver**; an **Uploaded Lap** exists only because a **User** submitted the **Telemetry Upload** that recorded it, and the driver behind it is claimed by the file rather than established. A **Timed Lap** is one that produced a time and a **Clean Lap** one driven without an incident — **Pace** is summarized over Clean Laps, while best-lap selection is not, so a best lap may carry an incident. On top of those sit three bests that were previously one overloaded phrase: a **Race Best** (fastest Race Lap in one car during one race week), an **Uploaded Best** (fastest Uploaded Lap for one car at one track configuration), and the **Personal Best** — the fastest lap known for a **Subject Driver** from whichever evidence the User has allowed to count, and the lap that gets ranked against a field. *Personal Lap* and *Best Lap* are recorded as terms to avoid.

### Fixed

- Contributor guidance no longer describes a stored personal lap as "the user's personal best per track+car". Each row is one uploaded lap — every timed lap of a telemetry upload — and the corrected entries in `AGENTS.md`, the database specialist's schema reference, the .NET and code-review agent guides, and the public feature list now also record that the shared per-car-and-track projection sees uploaded laps only, so what it returns is an uploaded best rather than a personal best.

## [0.5.18] - 2026-08-13

### Added

- `CONTEXT.md` now defines ApexRacers' race-session vocabulary and, for the first time, the cardinalities between its levels: a **Race Session** is one scheduled timeslot that divides into one or more **Splits**; each Split is exactly one **Subsession**, so a Subsession never contains Splits; and each Subsession runs one or more **Sim Sessions** in sequence, of which the race segment is number 0. A **Race** is a Subsession whose **Event Type** is a race, and is the word used in URLs and driver-facing copy. A **Race Result** names exactly one **Driver**, so a team entry racing under no Customer ID produces none. **Split Index** is zero-based and ordered by **Strength of Field** descending, with no one-based counterpart because Splits are never shown to drivers. *Session* and *Event* are recorded as terms to avoid, each naming more than one level of the hierarchy.

### Fixed

- Contributor guidance no longer describes a subsession as "one race session". A subsession is one split *of* a race session, and the corrected entries in `AGENTS.md` and the database specialist's schema reference now also record that only the race sim session's results are stored, that only race event types are ingested, that the stored split number is a value ApexRacers derives rather than one iRacing supplies, and that the results key assumes a customer ID and therefore cannot represent a team entry.

## [0.5.17] - 2026-08-13

### Added

- `CONTEXT.md` now defines ApexRacers' identity vocabulary, separating an ApexRacers **User** from an iRacing **Driver** and naming the relationship between them: a **Claimed Identity** is the Driver a User asserts is them (at most one each way, asserted rather than proven), a **Verified Identity** is one proven by an iRacing sign-in, a **Subject Driver** is whoever a page or calculation represents, and a **Demo Driver** is resolved as the Subject Driver on the demo surface without becoming a claim. *Member* and *Customer* are recorded as iRacing's own words, retained only where a name mirrors their API.
- `docs/adr/` with its first architecture decision record, documenting why Drivers are referenced by iRacing Customer ID rather than modelled as a local entity, and what follows from that — snapshotted driver names, no database-level guarantee that a Customer ID is real, and race data surviving deletion of the account that claimed it.

## [0.5.16] - 2026-08-13

### Added

- `CONTEXT.md` domain glossary defining the shared racing vocabulary — Series, Season, Active Season, Current Season, Upcoming Season, Race Week, Race Week Index, Race Week Number, and Current Race Week.

### Fixed

- The season backing a series is now selected by which season's first race week began most recently, instead of by the newest active year-and-quarter. iRacing marks the incoming season active before racing starts, so schedule, standings, strategy, week, percentile and recommendation data could all read an upcoming, empty season while the current quarter was still running. The season drivers are actually racing now stays current through its final week and the inter-season gap, and hands over on the date the next season's first race week begins — even when upstream active flags overlap or change.
- The series browser now shows one card per series instead of one per active season, so a series no longer appears twice during a season changeover. The card's race week, track, car count and driver count are computed from the selected current season alone.
- Driver-facing race week labels are now one-based across the series browser, schedule, week detail, percentile header, strategy header, breadcrumbs, race-now board, dashboard and profile, so the first week of a season reads as "Week 1" rather than "Week 0" (previously only the qualifying standings selector was correct). API payloads, request parameters, cache keys, persistence and route parameters keep iRacing's zero-based race week index.

## [0.5.14] - 2026-08-12

### Changed

- Backend database tests now combine fast relational SQLite coverage with mandatory isolated PostgreSQL integration coverage for provider-specific `DateTimeOffset` queries, refresh-token constraints and rollback, and refresh-token-issuing auth flows. The EF InMemory provider has been removed; running the full backend suite now requires Docker.

### Security

- Pinned Testcontainers' transitive SSH.NET dependency to 2026.0.0, removing the high-severity recursive-download path-traversal vulnerability from the backend test toolchain.

## [0.5.13] - 2026-08-12

### Changed

- Backend tests now run xUnit natively on Microsoft Testing Platform v2, with direct filtering, built-in Microsoft code coverage, and current Visual Studio Test Explorer / VS Code C# Dev Kit integration. CI now retains stable TRX and Cobertura artifacts while preserving the existing >85% line and branch gates; the legacy VSTest SDK/adapter, Coverlet collector, and `dotnet-coverage` workflow have been removed.

## [0.5.12] - 2026-08-12

### Changed

- API documentation now uses ASP.NET Core's built-in OpenAPI generation with Scalar's development-only interactive reference, replacing the full Swashbuckle dependency while preserving the `ApexRacers API` v1 document contract.

## [0.5.10] - 2026-08-12

### Removed

- Removed the redundant explicit Autoprefixer PostCSS plugin and direct development dependency; Tailwind CSS v4 already handles vendor prefixing through `@tailwindcss/postcss`.

## [0.5.9] - 2026-08-12

### Changed

- Frontend tests now use `@testing-library/jest-dom` 7.0.1, keeping the test assertion tooling on its latest compatible patch release.

## [0.5.5] - 2026-08-10

### Fixed

- The Changelog Version CI check no longer fails pull requests it is meant to exempt. A pull request that adds no new dated changelog section — dependency bumps, docs-only work — is supposed to pass immediately, but the check reported a spurious version-drift error on every one of them, blocking otherwise-green branches until the section was pointlessly renumbered.

## [0.5.4] - 2026-08-10

### Changed

- The frontend compiler has moved from TypeScript 6 to the stable TypeScript 7 native implementation, aligning builds with the type-aware Oxlint backend already adopted in v0.5.3.

## [0.5.3] - 2026-08-10

### Changed

- Frontend linting now runs entirely on Oxlint, including its TypeScript 7-powered type-aware backend and native React compiler analysis. ESLint, typescript-eslint, and their plugin stack have been removed; existing async and type-assertion findings were tightened as part of adoption, Playwright/E2E files now participate in the Node tsconfig, and overrides are limited to test-mock conventions plus the dynamic dependency contract in `useResource`.

## [0.5.2] - 2026-08-10

### Changed

- Read-only page data now uses one resource lifecycle for loading, typed unlinked-account responses, failures, stale-result suppression, and real network cancellation. Optional overlays declare their empty fallback at the request boundary, while user-facing resources render a shared loading/error/link-account presentation. Shared card elevation and scan textures also moved to theme-aware CSS utilities instead of being copied through page-local style objects.

### Fixed

- Driver-profile failures now render an actionable error instead of silently removing the stats section, and failed series requests on Analytics and Recommendations no longer masquerade as a valid empty-series response.

## [0.5.1] - 2026-08-10

### Changed

- Percentile badges and labels now share one conversion from a raw percentile rank to the displayed top-percent value. Pages pass the rank through unchanged, while the shared formatter owns rounding and the minimum `TOP 1%` clamp, preventing the week-detail and percentile-result displays from drifting apart at boundary values.

## [0.5.0] - 2026-08-09

### Changed

- The `/ship` workflow now evaluates the complete release impact before choosing a version: incompatible changes start a major line, backward-compatible capabilities start a minor line, and fixes or maintenance stay on the current line for the standard build increment. Major and minor decisions update both npm version files before the shared version script computes the exact tag.

## [0.4.55] - 2026-08-09

### Fixed

- Flag-gated iRacing routes no longer flash the Coming Soon page while feature flags are loading or the signed-in owner changes. The flag context now distinguishes unresolved state from a resolved disabled flag, failures settle ready and fail closed, and the route guard renders nothing until the current owner has a result. A single `useIracingSurface` hook now owns the `iracing-live`-or-`iracing-demo` rule used by navigation and driver/profile surfaces, preventing those consumers from drifting.

## [0.4.54] - 2026-08-09

### Fixed

- Demo cache rows now share one owned sentinel-range contract across the Seeder, verifier, API cleanup, and production purge SQL. The SQL lower bound is an explicit UTC `timestamptz` instant rather than a session-time-zone-dependent date, and routine cache cleanup explicitly preserves every row in the sentinel range even if its cutoff advances beyond year 9000. Tests pin the below/at/above boundary, the exact SQL operator and UTC value, and preservation of both threshold and writer-sentinel rows.

## [0.4.53] - 2026-08-09

### Changed

- Refresh-token issuance, rotation, revocation, active-token capping and retention cleanup now share one lifecycle owner and one injected clock. A token is active only while it is unrevoked and strictly unexpired; issue and rotation use the same random-token/hash factory, rotation persists the old-token revocation and replacement atomically, and raw credentials are returned to the caller without ever being stored. Exact-expiry, five-token cap, rotation exemption, revoke-all and purge boundaries now have direct database-backed coverage.

## [0.4.52] - 2026-08-09

### Changed

- The ingestion worker now delegates season, schedule, subsession and track-state persistence to focused modules instead of owning roughly fifty SDK-to-database field mappings inline. Persisted track state crosses the SDK boundary through an owned snapshot, so SDK model drift is isolated to one mapper rather than leaking into stored JSON. The extracted mapping and insert/update behavior now have direct field-level and database-backed coverage while the worker remains an orchestration shell.

## [0.4.51] - 2026-08-09

### Changed

- Authenticated iRacing endpoints now share one tested member-identity contract for optional personalization versus required linked-account data. The nine required-link controller paths no longer each reinterpret `null` and `0`; `MemberContext` owns that decision and the middleware preserves the existing `409` `{ code, message }` response used by the web client. Expected unlinked-account requests remain ordinary request warnings rather than producing a second exception warning with a stack trace.
- Feature-flag role eligibility now has one owner shared by the public flag list and the demo-driver identity override, so both paths apply the same enabled/minimum-role rules. The demo-aware identity path also drops from three database queries to two.

### Security

- Unknown or corrupt feature-flag minimum roles now fail closed instead of inheriting Standard-tier eligibility, while valid role names remain case-insensitive.

## [0.4.50] - 2026-08-09

### Changed

- Documented the percentile page's behaviour under the demo flag, which was missing from the demo-caveats list alongside the existing `/analytics`, race-guide and `/compare` entries. Under `iracing-demo` the page shows its manual customer-ID form rather than resolving the demo driver automatically, because the percentile endpoint deliberately accepts a caller-supplied customer id so the page can look up *any* driver, and the demo-aware resolver is deliberately not on that path. A demo user has no real iRacing customer id, so the JWT claim the page reads first is absent and it falls through to the form — entering `100001` shows the demo driver. **This is accepted behaviour, not a defect**; the omission from the caveats list was the actual problem, since it read as a bug to anyone comparing the page against the others. Noted explicitly so it is not "fixed" by routing the endpoint through the demo-aware resolver, which would remove the ability to look up another driver.

## [0.4.49] - 2026-08-09

### Changed

- The bearer-token settings are now bound once and shared by both sides of the contract. The issuing side and the validating side each read `JWT_ISSUER` and `JWT_AUDIENCE` and each carried their own copy of the fallback literals, with issuer and audience validation both switched on. Changing one default would have made every token this API mints be rejected by this same API — a total-auth outage from a one-word edit, with no compile error to catch it. `JwtSettings` also owns deriving the signing key, because both sides previously did their own UTF-8 encoding of it, making that a third thing that had to match. The null-forgiving cast on the signing key in `AuthService` is gone: the settings validate it on bind, matching what startup already did.

### Fixed

- Closed the test gap that made the above invisible. The auth suite set neither `JWT_ISSUER` nor `JWT_AUDIENCE`, so the issuing and validating sides both ran on their defaults and agreed by accident rather than by construction — a divergence between them would have passed every test. There is now a round-trip check that issues a real token through the auth service and validates it with the exact parameters startup builds, deliberately using **non-default** issuer and audience values so agreement has to come from the shared binding. Three negative cases (mismatched issuer, audience and signing key) confirm the positive ones are not passing vacuously. Verified by simulating the outage: changing the issuer in the issuing path to `ApexRacers.API` — one letter cased differently — now fails two tests, where previously nothing in the suite would have noticed.

## [0.4.48] - 2026-08-09

### Changed

- Resolving "which season and week does this series mean right now" now has one owner. The active-season lookup and its exception wording were written verbatim three times, the series-name lookup three times, and the active-week lookup with its Year/Quarter tie-break four times. `SeasonQueries` holds them as composable query fragments rather than methods returning a fixed record, because the duplication was in the filter and the ordering rather than the shape — each of the four week lookups projects different columns, so a record would have forced every caller to fetch the union of them and given up the projection pushdown. The Year/Quarter ordering is the load-bearing part: a series can have more than one season flagged active during a changeover, and getting the order wrong silently reads last quarter's data.
- **"Which week is the season in" had two different answers, and now has one.** The series list took the week with the latest start date; the standings page took the highest week number. Those coincide only while start dates and week numbers run in the same order, which nothing enforces — a duplicated or out-of-order start date would have made the two pages disagree about what week it is, with no shared code to fix. `ApexRacers.Core.SeasonCalendar` settles it as start date first, week number only to break a tie: strictly more defined than either original and identical on well-ordered data.
- What happens *before* a season starts is deliberately still the caller's choice, because the two surfaces want different things and both are right — the series list shows a blank cell, while the standings page has to render some week and falls back to the first. Both behaviours are unchanged.
- `SeriesService` now runs three queries instead of one containing six correlated subqueries per row. It repeated the same "weeks that have started, latest first" subquery six times over, and expressed the current-week rule in SQL where it could not be shared with the standings page answering the same question; resolving the week once fixes both.
- **No behaviour changes.** The 513 pre-existing tests passed against the refactor before any new test was added. The current-week rule now has direct coverage — previously it was reachable only through a three-week database fixture on one side and a full query projection on the other.

## [0.4.47] - 2026-08-09

### Changed

- The personal-best projection now has one owner, `PersonalBestQuery`. The same twenty-line query — project the lap rows, materialize, group by car and track configuration, then aggregate into `PersonalLapDto` — was written three times, for the My Laps page and the car and track catalog detail pages, differing only in the filter and the sort. `PersonalLapDto` is a positional record with seven fields, so adding one meant editing three places, and a mis-ordered argument at any of them would have been a runtime data bug rather than a compile error. Callers now pass their already-scoped query and the ordering they want.
- Two invariants that all three copies carried, and that none of them stated, now belong to the module. **Only valid laps count**: the `IsValidLap` filter is applied inside, so a caller cannot omit it and quietly report an invalidated lap as a personal best — a rule that previously existed as three lines of code and, as it turns out, zero tests, since nothing in the suite had ever seeded an invalid lap. And **the grouping must not be pushed into SQL**: its key spans navigation properties alongside the aggregates, which neither Npgsql nor SQLite can translate, so getting it wrong throws at runtime instead of failing to compile.
- Ordering is a required argument rather than something the caller applies afterwards. Grouping produces no guaranteed order, so leaving it optional would turn an omission into a silently arbitrary result; requiring it makes the choice explicit at each of the three call sites.
- **No behaviour changes.** The 505 pre-existing tests passed against the refactor before any new test was added. Also removed two pieces of dead code left behind by the 0.4.44 cache-key change: `DemoCacheSeeder`'s now-unused `IRatingChart` field and its SDK import.

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

[Unreleased]: https://github.com/jwh3times/apexracers/compare/v1.0.2...HEAD
[1.0.2]: https://github.com/jwh3times/apexracers/compare/v1.0.1...v1.0.2
[1.0.0]: https://github.com/jwh3times/apexracers/compare/v0.9.3...v1.0.0
[0.9.0]: https://github.com/jwh3times/apexracers/compare/v0.8.2...v0.9.0
[0.8.2]: https://github.com/jwh3times/apexracers/compare/v0.8.1...v0.8.2
[0.8.1]: https://github.com/jwh3times/apexracers/compare/v0.8.0...v0.8.1
[0.8.0]: https://github.com/jwh3times/apexracers/compare/v0.7.1...v0.8.0
[0.7.1]: https://github.com/jwh3times/apexracers/compare/v0.7.0...v0.7.1
[0.7.0]: https://github.com/jwh3times/apexracers/compare/v0.6.14...v0.7.0
[0.6.9]: https://github.com/jwh3times/apexracers/compare/v0.6.8...v0.6.9
[0.6.8]: https://github.com/jwh3times/apexracers/compare/v0.6.7...v0.6.8
[0.6.7]: https://github.com/jwh3times/apexracers/compare/v0.6.6...v0.6.7
[0.6.5]: https://github.com/jwh3times/apexracers/compare/v0.6.4...v0.6.5
[0.6.4]: https://github.com/jwh3times/apexracers/compare/v0.6.3...v0.6.4
[0.6.3]: https://github.com/jwh3times/apexracers/compare/v0.6.2...v0.6.3
[0.6.2]: https://github.com/jwh3times/apexracers/compare/v0.6.1...v0.6.2
[0.6.1]: https://github.com/jwh3times/apexracers/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/jwh3times/apexracers/compare/v0.5.20...v0.6.0
[0.5.20]: https://github.com/jwh3times/apexracers/compare/v0.5.19...v0.5.20
[0.5.19]: https://github.com/jwh3times/apexracers/compare/v0.5.18...v0.5.19
[0.5.18]: https://github.com/jwh3times/apexracers/compare/v0.5.17...v0.5.18
[0.5.17]: https://github.com/jwh3times/apexracers/compare/v0.5.16...v0.5.17
[0.5.16]: https://github.com/jwh3times/apexracers/compare/v0.5.15...v0.5.16
[0.5.14]: https://github.com/jwh3times/apexracers/compare/v0.5.13...v0.5.14
[0.5.13]: https://github.com/jwh3times/apexracers/compare/v0.5.12...v0.5.13
[0.5.12]: https://github.com/jwh3times/apexracers/compare/v0.5.11...v0.5.12
[0.5.10]: https://github.com/jwh3times/apexracers/compare/v0.5.9...v0.5.10
[0.5.9]: https://github.com/jwh3times/apexracers/compare/v0.5.8...v0.5.9
[0.5.5]: https://github.com/jwh3times/apexracers/compare/v0.5.4...v0.5.5
[0.5.4]: https://github.com/jwh3times/apexracers/compare/v0.5.3...v0.5.4
[0.5.3]: https://github.com/jwh3times/apexracers/compare/v0.5.2...v0.5.3
[0.5.2]: https://github.com/jwh3times/apexracers/compare/v0.5.1...v0.5.2
[0.5.1]: https://github.com/jwh3times/apexracers/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/jwh3times/apexracers/compare/v0.4.55...v0.5.0
[0.4.55]: https://github.com/jwh3times/apexracers/compare/v0.4.54...v0.4.55
[0.4.54]: https://github.com/jwh3times/apexracers/compare/v0.4.53...v0.4.54
[0.4.53]: https://github.com/jwh3times/apexracers/compare/v0.4.52...v0.4.53
[0.4.52]: https://github.com/jwh3times/apexracers/compare/v0.4.51...v0.4.52
[0.4.51]: https://github.com/jwh3times/apexracers/compare/v0.4.50...v0.4.51
[0.4.50]: https://github.com/jwh3times/apexracers/compare/v0.4.49...v0.4.50
[0.4.49]: https://github.com/jwh3times/apexracers/compare/v0.4.48...v0.4.49
[0.4.48]: https://github.com/jwh3times/apexracers/compare/v0.4.47...v0.4.48
[0.4.47]: https://github.com/jwh3times/apexracers/compare/v0.4.46...v0.4.47
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
