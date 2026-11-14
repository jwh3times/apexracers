# Backend architecture (`src/`)

Part of the agent guide: the root `AGENTS.md` holds the ground rules, development flow, commands and
testing rules; this file holds the backend architecture, and loads when work touches `src/`. Edit
this file, not `src/CLAUDE.md`. Pattern detail stays in the `dotnet-api` and `postgres-specialist`
agents.

## API request flow

```text
HTTP request → Controller (binds inputs only) → Service (all logic + EF Core) → AppDbContext → PostgreSQL
```

**Driver lifecycle boundary:** `DriverAuthorization`, `DriverPublication`, and `CopyLifecycle`
coordinate proof/consent generations, journal-first closure, actual protected-writer admission/drain,
and tracked-copy work through `Data.DriverAuthorityStore`. Ordinary startup registers an unavailable
ownership-proof adapter. The enforcement journal and publication history are one `BlobDriverJournal`
(write-once blob storage, independent of primary restore), selected by
`IndependentJournalRegistration` only when every required `INDEPENDENT_JOURNAL_*` setting is
present and every supplied one is valid; otherwise the unavailable adapters remain. Positive protected publication is limited to
controlled synthetic hosts. A stored claim or an `iracing_id` JWT claim grants no Driver access.
For lifecycle transitions, protected results, recovery or copy changes, read
`docs/research/driver-lifecycle-admission-spine.md` before extending these modules, and
`docs/research/independent-journal-adapter.md` before changing the journal adapter, its record
layout or its settings.

`LegacyDriverAccessGuard` runs before model binding. Auth, Admin, FeatureFlags, Cars, Tracks, Series
and Schedule retain independent access; other legacy controllers require Demo provenance, and
Telemetry is unavailable in every namespace. Denied workflows return `503` ProblemDetails with
`no-store`. Catalog detail and Schedule omit private upload overlays. Controlled User deletion
records a User-wide independent veto, atomically closes all historical associations, and drains every
affected writer; the tombstone prevents grants across Customer IDs. For upload attribution, dormant
recovery, account erasure or owner analytics, read `docs/research/private-upload-lifecycle.md`.
The lifecycle migration creates empty
authorization tables and fences old claim writers without promoting any stored claim to ownership;
recovery is forward-only. Synthetic implementation evidence does not establish live authorization
or the complete publication/copy/restore acceptance matrix.

**Scoped Driver references:** `ScopedDriverPublication` owns discovery, detail, comparison and
Follow output through a protected result executor; `DriverReferenceStore` owns opaque,
recipient/purpose/revision/proof/provenance-bound references and private Follow clocks. Read
`docs/research/driver-scoped-references.md` before changing these modules, their SQL fences or
routes. Ordinary startup supplies no controlled reference catalog, so these routes remain
unavailable. Controlled synthetic fixtures establish neither Live permission nor actual catalog
admission or the integrated acceptance matrix.

**Browser and restore boundaries:** sensitive page reads use `useDriverResource` and
`driverDisclosure`; `DriverPrivacy` resolves own-User withdrawal and reports durable intent/drain.
Read `docs/research/driver-browser-validity.md` before changing sensitive display, browser
invalidation, withdrawal or refresh replay. Read `docs/research/driver-migration-restore.md` before
changing rollback fences, snapshot activation, independent history reconciliation or restore tests.
Legacy `UploadedLap` maps to `iracing.QuarantinedUploadedLaps`; the forward-only migration removes
the older binary's physical reader/writer route without inventing attribution or original clocks.

**Operating admission:** read `docs/research/driver-operating-admission.md` before changing stage
approval, pilot enrollment, acquisition/publication budgets, background collection or safety recovery.
Operating admission supplements Driver authority and release accounting; withdrawal and cleanup
retain their independent access. Ordinary API/ingestion startup uses unavailable operating controls;
the explicit PostgreSQL synthetic adapter admits Demo provenance only.

Controllers do no logic beyond binding inputs and returning `Ok(result)`. Services live in
`src/ApexRacers.Api/Services/`; response shapes are `record` types in `Dtos/ResponseDtos.cs`. If an
action needs multiple steps, extract a focused service class injected via DI — no MediatR, no
command/query handlers, no `IRepository<T>` (use `AppDbContext` directly).
Scoped Driver and privacy controllers return the service-owned `IActionResult` to preserve
protected dispatch and lifecycle outcome status.

ApexRacers-owned response fields identify a Driver with `CustomerId` and `DriverName` (serialized as
`customerId` / `driverName`). `ApplicationUser.DisplayName` is the local User's account label and is
not interchangeable with a Driver name. Upstream `cust_id` / `display_name` spellings and SDK member
names remain at parser and mapping adapters; do not expose them through ApexRacers response DTOs.
Likewise, use `RecommendationRank` only for ApexRacers' per-Car recommendation ordering and
`Standing` for positions iRacing awards in championship, Time Trial, qualifying, and category
leaderboard results. Unqualified `Rank` is not an ApexRacers response-field name; `CONTEXT.md` owns
the distinction among Recommendation Rank, Standing, and Percentile Rank.

**Development API docs:** `Microsoft.AspNetCore.OpenApi` generates the `v1` document at
`/openapi/v1.json`, with title `ApexRacers API` and version `v1`; `Scalar.AspNetCore` serves its
interactive reference at `/scalar/v1`. Both endpoints are mapped only inside
`app.Environment.IsDevelopment()` and are exempt from rate limiting. The API project's documentation
stack is `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore`; package versions remain centrally
managed in `Directory.Packages.props`.

**Error handling:** `ExceptionHandlingMiddleware` (registered first) converts unhandled exceptions into
RFC-7807 `application/problem+json`, status from the pure `ExceptionStatusMapper`
(`ArgumentException`/`InvalidOperationException` → 400, `KeyNotFoundException` → 404,
`UnauthorizedAccessException` → 401, `IRacingNotLinkedException` / `ClaimedIdentityConflictException` → 409,
`IRacingNotConfiguredException` / `EvidenceCopyUnavailableException` → 503, else 500 with its message hidden). The not-linked exception is
the deliberate format exception: middleware preserves the established exact JSON
`{ code: "IRACING_NOT_LINKED", message: "…" }` instead of ProblemDetails. A claimed-identity conflict
uses ordinary ProblemDetails with a non-disclosing `detail`. Services should just `throw`;
don't catch to `BadRequest(string)`. Controllers still return explicit results for non-exception
outcomes needing a specific code (e.g. AuthController's `501` for the unimplemented OAuth callback).
Sign-in is the deliberate counter-example: it has exactly two outcomes, `200` and a generic `401`.
It used to answer `423` for a locked account, which only a real account can be, so the status
enumerated the user list (GHSA-28pc-cx5w-g6jp) — see `dotnet-api` for the rule that keeps it closed.
**Client disconnects are handled ahead of that mapping:** the pure `ClientDisconnectDetector` matches an
`OperationCanceledException` or `BadHttpRequestException` raised while `HttpContext.RequestAborted` is
signalled, and the middleware records it at Debug, sets **499** (nginx's "Client Closed Request"), and
writes no body — there is no client left to receive one. Both types are matched only alongside the
cancellation token, so a server-side timeout or a genuinely malformed request keeps its 500/400 and its
Error log. Browsers navigating away mid-request produce these constantly; without the branch they read
as server faults (an E2E run logged 15).

**Cross-cutting middleware & ops endpoints** (`Program.cs`, in pipeline order): `RequestLoggingMiddleware`
(outermost — one structured log line per request: bounded method, trusted route template, status code, elapsed ms, client IP;
level scales with status; skips `/healthz` and `/ready`) → `ExceptionHandlingMiddleware`
→ `SecurityHeadersMiddleware` (baseline headers on every API + SPA response: nosniff, frame-deny,
referrer/permissions policy, full same-origin SPA CSP, HSTS over HTTPS). For asset or API-reference
changes, read `docs/content-security-policy.md` for the built-bundle audit and Development-only Scalar
exceptions; verify with `web/e2e/csp.spec.ts` against the built SPA, not Vite. Rate limiting: a
global per-IP safety net, configurable via `GLOBAL_RATE_LIMIT_PERMIT_PER_MINUTE` (**default 300**; CI/E2E
raises it), plus a stricter per-IP `auth` policy on `AuthController` whose limit is configurable via
`AUTH_RATE_LIMIT_PERMIT_PER_MINUTE` (**default 10**; CI/E2E raises it since the serial suite shares one
runner IP), plus a per-user `iracing-search` policy on `RivalsController`'s driver-search endpoint
(partitioned by the `sub` claim, falling back to IP so the partition is total) whose limit is
configurable via `SEARCH_RATE_LIMIT_PERMIT_PER_MINUTE` (**default 30**) — driver search is the one
iRacing-backed route taking free text, where each distinct term is its own cache key and its own
upstream fetch against the shared service-account quota (GHSA-jv96-89xc-98h2), so bounding the term
length alone doesn't bound how many distinct terms one caller can mint per minute. Those are the
**API** defaults — `docker-compose.yml` raises both original limits for the local stack too
(1000 / 10000), because the documented local E2E loop drives it in parallel from one loopback IP and at
the API defaults the limiter starts returning 429 mid-run, which reads as unrelated test failures rather
than as throttling. Set any of the three vars in `.env` to exercise the limiter locally. This per-IP
`auth` policy is unauthenticated-request-volume protection and is independent of the sign-in throttle
(`SignInThrottleStore`, see Services) that counts failed passwords per (account, source address) —
the two are separate layers, not one restated as the other. Health probes (anonymous, rate-limit-exempt): `GET /healthz` (liveness, no
dependency checks) and `GET /ready` (DB readiness via `AddDbContextCheck`). Per-IP limiting (and
HSTS's `Request.IsHttps` check) are correct behind a reverse proxy only once forwarded headers are
processed; `ApexRacers.Api.Services.ForwardedHeadersPolicy` is the single place that trust decision is
expressed (`Configure(ForwardedHeadersOptions)`; the host registers the middleware from it, gated on
an app setting — see `dotnet-api` for the call rule and `azure-infrastructure` for the deployed
value). `EvidenceOperationalLogging` suppresses payload-capable framework/provider log categories;
application failure logs use bounded types instead of attached exceptions. Raw paths and queries
are excluded. External automatic telemetry, access logs and sink retention require deployed
observation; read the copy-writer inventory before changing these logging boundaries.

## Controllers — use-case-oriented, NOT entity-CRUD

Each controller is one user-facing capability (not a per-entity CRUD surface). `[Authorize]` unless
marked **public**. The lifecycle boundary above determines which Driver routes are available;
eligible Demo endpoints requiring a Subject Driver retain the typed `409` (`IRACING_NOT_LINKED`)
contract when that identity is absent.

The controllers are `src/ApexRacers.Api/Controllers/*.cs`; each action's `[Authorize]` /
`[AllowAnonymous]` attributes say which are public.

## Services (`src/ApexRacers.Api/Services/`)

One responsibility per class; pure heuristics/mappers/parsers are extracted and unit-tested directly.
The competitiveness metrics are one such extraction, shared across services and the Seeder as
`ApexRacers.Core.FieldPercentile` — not a per-service formula. It owns percentile rank, field
position, top share, field size, and field median, and every one of them takes the _other_ drivers'
laps and reconstructs the Field internally. `CONTEXT.md`'s Competitiveness section defines what each
one means; the `dotnet-api` agent carries the call rules.

The race-vs-uploaded evidence choice is a second such extraction: `ApexRacers.Core.PersonalBest.Select`
picks a Subject Driver's Personal Best from their Race Best and Uploaded Best and carries the
`LapEvidence` (`RaceLap` / `UploadedLap`) that produced it, rather than each service running its own
comparison and keeping only the number. A compact response that omits the lap time but presents a
metric derived from that Personal Best still carries its evidence; Week Detail's `my-percentiles`
response is the canonical example. `CONTEXT.md`'s Lap Evidence section defines Race Lap/Uploaded
Lap/Personal Best; the `dotnet-api` agent carries the call rule.

The span a Race Week covers is a third such extraction: `ApexRacers.Core.RaceWeekWindow` owns the
`Start`/`End`/`Contains(recordedAt)` test used to bound which Uploaded Laps may contribute to a
Personal Best — a Race Best already belongs to one Race Week, so an all-time Uploaded Best has to be
bounded the same way before the two are comparable. `For(...)` prefers iRacing's reported
`Week.EndTime`, falls back to the next Week's start, then to a nominal seven days (both fallbacks
marked `IsExact: false`); `ForSeason(...)` builds every Week's window at once, since a Week with no
reported end needs the next Week's start date to derive one. `CONTEXT.md`'s Personal Best entry
defines why the bound exists; the `dotnet-api` agent carries the call rule.

Track Configuration Name absence is a fourth shared boundary rule:
`ApexRacers.Core.ConfigurationName.Normalize` collapses omitted, blank, and `N/A` inputs to the empty
string before any `Track.ConfigName` persistence, while `NullIfAbsent` converts that storage value to
`null` for every public response. Apply both at their respective boundaries; do not persist an
upstream absence sentinel or expose the internal empty-string representation through the API.

The Telemetry Upload size bound is a fifth shared boundary rule: `ApexRacers.Core.TelemetryUpload`
owns `MaxFileSizeMegabytes`/`MaxFileSizeBytes` (250 MB — the number the upload page advertises and the
number the API refuses a file for), `MaxRequestBytes` (the file bound plus 1 MB of multipart-framing
headroom, enforced as the transport backstop `TelemetryController` reads for `[RequestSizeLimit]` /
`[RequestFormLimits]`), and the derived `TooLargeMessage`. `web/src/features/telemetry/uploadLimits.ts`
mirrors the megabyte bound in TypeScript so the upload page can refuse an oversized file before sending
it; change the two together. `CONTEXT.md` does not define this bound — it is a transport/product limit,
not a domain term — and the `dotnet-api` agent carries the call rule for why the request bound must stay
strictly above the file bound.

Sign-in brute-force refusal is a sixth shared boundary rule: `ApexRacers.Core.SignInThrottle` (+
`SignInThrottleOptions.Validated()`) decides, from failure counts and a clock alone, whether one more
sign-in attempt from one scope — ordinarily a source address, or a recognized `KnownDevice` (issue
#314) — is allowed. Address scoping is (account, source address) rather than the account alone, which
is what let a stranger who merely knew a Driver's email lock that Driver out indefinitely (issue #300,
closing the last open finding of GHSA-28pc-cx5w-g6jp). A caller presenting a cookie naming a device
that has already signed in to the account is instead throttled against that device's own counter,
closing the residual case where an attacker shares an address with the Driver (a CGNAT pool, a shared
office egress, a VPN exit). Both scopes are charged; `SignInProtection` coordinates admission for
each User in a short database transaction covering device and address claims, including address-only
callers. The transaction ends before password work, notices or success cleanup. Per-scope claims
remain individually atomic, and dropping the cookie adds no allowance on the same source address.
A device may only ever add allowance, never refuse an attempt the address counter would admit. Crossing a much higher account-wide failure count never denies a caller by itself; it
only tightens every *unrecognized* scope's allowance (5 down to 1) — a known device keeps the full
allowance even while the account is under attack. An address (or device) with no failures against it
is still admitted on the first correct password. `Validated()` fails startup if the tightened
allowance or the high-water mark could be 0, since either would silently deny the account owner too.
`SignInThrottleStore` and `KnownDeviceStore` are the persistence halves (`identity.SignInAddressFailures`
/ `identity.SignInAccountFailures` / `identity.KnownDevices`); `dotnet-api` carries the call rules and
`postgres-specialist` the schema.

- `SeriesService`, `WeekCarStatsService` — series list (one card per series, on its Current Season); per-car week lap stats (median via `Core.FieldPercentile`). Week Detail alone adds each car's world record and the Field gap through `WorldRecordService`, looked up sequentially on the request's context; a failed lookup stops the remaining lookups (a failed cache write can leave the shared context unusable) and blanks that car and the cars after it, and the plain per-week car list makes no lookups. Current-season/current-week lookups across these two plus `ScheduleService`, `StrategyService`, `StandingsService`, `PercentileCalculationService`, and `CarRecommendationService` go through `SeasonQueries` (`src/ApexRacers.Api/Services/SeasonQueries.cs`) instead of a hand-written predicate. "Which season is current" is `Core.SeasonCalendar.CurrentSeasonId` — the season whose **first race week began most recently**, holding the slot through the inter-season gap until a later season's first race week start date arrives; iRacing flags the incoming season active before racing starts, so `Active` selects which _series_ appear, never which season backs them. A series with no season that has begun falls back to the newest active one. "Which week is the season in" is `Core.SeasonCalendar.CurrentRaceWeekIndex` (start date first, Race Week Index to break a tie), with the pre-season fallback left to the caller. Zero-based Race Week Indexes stay zero-based through persistence, APIs, route parameters, and frontend state; only upstream iRacing adapters retain `race_week_num` / `RaceWeekNum`, and only the display boundary converts an Index to a one-based Race Week Number. See `CONTEXT.md` for the canonical distinction.
- `PercentileCalculationService` — percentile detail and world-record overlay. An explicit authenticated User whose current Demo Subject matches the requested Driver refreshes classified history through `Data.PercentileRefresh`; arbitrary Driver lookup is transient. Quarantined Uploaded Laps supply no overlay; typed Personal calculation uses its verified grant path separately.
- `CarRecommendationService` — recommendation ordering and projections over shared fresh classified readings. Explicit authenticated User identity resolves Demo ownership for recommendations, Week Detail's `my-percentiles` and Strategy; stored claims never recover a history owner. Expected Percentile averages distinct eligible Weeks through the requested Week's witnessed start date across Seasons. Missing Personal Best and Fields below five contribute nothing; history never falls back to legacy rows. A projected-only car has no current Percentile Rank, Top Share or Field Size. Week Detail carries `PersonalBestLapEvidence`.
- `PersonalBestEvidence` — request evidence policy, defaulting to Race-only, and canonical selection adapter for shared refresh. Its legacy query helper grants no history access; current readers never promote quarantined uploads.
- `StrategyService` (+ pure `StrategyAnalysis`) — week briefing from BoP + weather + track/pit; personal overlay.
- `UserAnalyticsService` — authenticated Demo analytics from an immutable discovered `PercentileHistorySnapshot`; weekly identity, Personal Best/evidence, Field median and selection stay coherent. `PercentileAnalyticsProjection` owns presentation; legacy rows, claims and quarantined uploads never supply its history. `PercentileHistoryPublication` owns controlled verified Personal projection/serialization, final source/authority checks and admitted writer terminality; ordinary Real adapters remain unavailable.
- `DriverStatsService` — progression / driver profile / comparison-side via `CachedIRacingClient` (6 h).
  Aydsko SDK types such as `MemberSummary` and `MemberProfileInfo` keep iRacing's `member_*` endpoint
  language at that adapter boundary; ApexRacers-owned names use the terms in `CONTEXT.md`.
- `AchievementsService` (+ pure `AchievementsMapper`) — awards trophy case (6 h).
- `RaceHistoryService` — recent official races (10 min); resolves car names and track configuration from the local catalog, keyed on the track identifier the payload carries (never the track name — see `docs/adr/0002-track-identity-follows-iracing-track-id.md`).
- `SubsessionDetailService` — one ingested subsession from the DB; normalizes stored weather units.
- `LapDataService` (+ pure `LapAnalysis`) — per-lap pace + pace stats (24 h).
- `ScheduleService` — active-season schedule (Race Weeks + Track + weather/BoP); private Uploaded Lap presence omitted pending protected personal integration.
- `WorldRecordService` — fastest car+track lap (24 h); null when iRacing unconfigured. Feeds the percentile detail overlay and Week Detail.
- `LeaderboardService` (+ pure `LeaderboardCsvParser`) — category global top-200 (24 h).
- `StandingsService` (+ pure `QualifyResultsParser`, `IChunkDownloader`) — driver/TT/qualifying standings (24 h). Qualifying is special-cased: the SDK omits the qual lap time, so it downloads + parses the chunk files itself. A caller-supplied `carClassId` or `raceWeekIndex` is validated against the season's own `SeasonCarClasses`/weeks before it reaches a cache key or an upstream fetch, throwing `KeyNotFoundException` (→ 404) for a value that isn't one of them; an omitted value still falls back to the week in progress (GHSA-jv96-89xc-98h2).
- `RaceGuideService` — "race now" board (60 s).
- `RivalService` — follow/search (30 min/term)/suggestions (from shared `SubsessionResult` rows). A search term over `IRacingCacheKeys.MaxDriverSearchLength` (64) throws `ArgumentException` (→ 400) rather than searching, since the term is the only unbounded caller input reaching a cache key (GHSA-jv96-89xc-98h2); the endpoint is also rate-limited per user (see Rate limiting above).
- `RivalComparisonService` (+ pure `SharedRaceAnalysis`) — assembles the head-to-head DTO.
- `CarCatalogService` / `TrackCatalogService` (+ pure `CarCatalogMapper` / `TrackCatalogMapper`) — catalog read from the **persisted** `Car`/`Track` tables; no creds at read time. Lists omit retired entries, while ID-based detail keeps them reachable with their class relationships. Private Uploaded Best overlays are omitted pending protected Driver integration.
- `UploadedBestQuery` — retained legacy per-car-and-track Uploaded Best projection (fastest or
  most-recent order). Its raw `UploadedLap` scope establishes no private read authority; current
  owner reads use `PrivateUploadStore` through protected `DriverPublication` dispatch. See
  `dotnet-api` for the legacy projection invariants.
- `ExternalDataCacheCleanupService` — reconciles durable copy deadlines, erases unclassified payloads and expires explanatory proof authority every thirty minutes; see the copy-writer inventory below.
- `AuthService` — registration, login, JWT issuance, profile/password/email-change, and reset; delegates the refresh-token lifecycle to `RefreshTokenStore` and the complete sign-in protection lifecycle to `SignInProtection`. `LoginAsync` is a thin wrapper over `SignInAsync`, which issues JWT/refresh credentials after authentication and returns a `SignInOutcome` carrying the cookie value to set; that value is deliberately not a field on `AuthResultDto`, so it can only reach the client as the `HttpOnly` cookie `AuthController` writes, never through the response body. Password change, password reset, and email-change forget every known device (`KnownDeviceStore.ForgetAllAsync`) alongside revoking refresh tokens. Needs `AddDefaultTokenProviders()`. Registration returns **nothing** and emails a confirmation link; sign-in is refused until the address is confirmed, with the same generic result an unknown address gets, so neither the response nor a follow-up sign-in reveals whether an address already has an account. Anything that creates an account outside registration must set `EmailConfirmed` — see `dotnet-api` for the full rule set. The JWT contract (signing key, issuer, audience) is bound once as `JwtSettings` (`Program.cs`), which builds both the issuing side's `SigningCredentials` (used by `AuthService`) and the validating side's `TokenValidationParameters` and enforces a minimum signing-key length at startup — see `dotnet-api` for the rule.
- `AdminSeedService` — startup promotion from `ADMIN_SEED_EMAILS` requires a confirmed account email; missing or unconfirmed accounts are skipped and existing Admin memberships are preserved. Role replacement is transactional; failed Identity operations abort startup. Ordinary registration now satisfies the confirmation requirement, since an account cannot sign in until its address is confirmed.
- `RefreshTokenStore` — owns issue/rotate/revoke/revoke-all/retention cleanup over an injected `TimeProvider`. Its canonical active predicate is `RevokedAt == null && ExpiresAt > now`; issuance caps active tokens at 5 per user. Rotation consumes the presented token with a conditional update rather than a plain read-then-write, so two requests racing the same token can never both succeed, then inserts the replacement in the same transaction; losing that race is a generic rejection, never a reuse-triggered revocation. `RevokeAllActiveAsync` re-sweeps until a pass finds nothing left active, so a token issued mid-revocation is still caught. Raw tokens leave only as return values; persistence stores their SHA-256 hashes.
- `SignInProtection` — owns `AuthenticateAsync(request, sourceAddress, deviceToken, ct)`: cookie recognition before account lookup, confirmation gating, coordinated device/address attempt claims and fallback, real or stand-in password verification, paced queued owner notices, asymmetric success cleanup, and device renewal. Returns an internal `ProtectedSignIn` (authenticated User plus optional device cookie) or the same null for every refusal; it issues no credentials. `Core.SignInThrottle` and the stores retain decision/SQL ownership. Device cleanup/renewal faults preserve successful authentication; cancellation propagates. See `dotnet-api` for the security call rules.
- `SignInThrottleStore` — the persistence half of the sixth shared boundary rule above: reads and records the failure counters `Core.SignInThrottle` decides on. `ClaimAttemptAsync` claims one attempt with a single atomic upsert **before** the password is checked, so a concurrent burst against one address gets distinct counts rather than every request reading the same stale one and all passing the gate; a refused attempt records nothing, so a caller can't hold its own window open by continuing to knock. `NoteFailureAsync` takes a nullable address — null when the attempt was throttled against a known device instead, in which case it takes the device's own exhaustion flag since this store cannot see a device's counter — and paces the owner's "someone is guessing" notice to one per account per `SignInThrottleOptions.NoticeInterval`. `SignInThrottleCleanupService` purges address/account rows once their window plus a grace period has passed, and known-device rows once their own `ExpiresAt` passes (no grace period — recognition has genuinely lapsed).
- `KnownDeviceStore` (+ `KnownDeviceCookie`) — the persistence half of the known-device exemption (issue #314). `RecogniseAsync` resolves a presented cookie value to a device by its hash alone, **before** any account lookup, so recognition cost never varies with whether the named address has an account; `ClaimAttemptAsync` is a single atomic `UPDATE … RETURNING`, mirroring `SignInThrottleStore.ClaimAttemptAsync`'s reasoning; `RememberAsync` renews an existing device in place or mints a new one only after a password has actually been verified, then evicts this account's least-recently-seen device past the 10-device cap. `KnownDeviceCookie` is the single owner of the cookie's name (`__Host-apexracers_device`, `Secure`/`HttpOnly`/`SameSite=Strict`/root `Path` wherever the environment can be secure; the unprefixed `apexracers_device` only in Development over plain HTTP) and is the only reader/writer — see `dotnet-api` for why the `__Host-` prefix and the environment-keyed `Secure` flag are load-bearing rather than cosmetic.
- `IEmailSender` / `AcsEmailSender` / `LoggingEmailSender` / `FileDropEmailSender` (+ pure `AccountEmailTemplates`, `EmailDelivery`) — transactional email over the `OutboundEmail` DTO; links built from `APP_BASE_URL`. `AccountEmailTemplates` also owns the account-confirmation and duplicate-registration messages, which are how a registration outcome the HTTP response withholds reaches the mailbox owner. `EmailDelivery.Select` owns which sender binds: a `DEV_MAIL_DROP_PATH` directory wins (each email written there as JSON — Development only, and startup **fails** if it is set anywhere else), else ACS when configured, else bounded delivery-status logging. Account links carry single-use credentials and are never logged, and no endpoint ever returns one — see `dotnet-api` for the rule. The one exception to sending inline: `AccountEmailTemplates.SuspiciousSignInAttempts` (the sign-in throttle's owner notice) goes through `IOutboundEmailQueue`/`OutboundEmailQueue`, drained outside the request by `OutboundEmailDispatcher` — an inline send there would cost real accounts a mail round trip that unknown addresses never pay, reopening the account-existence oracle by latency or by a 500 on mail failure.
- `TelemetryUploadService` — transient non-identifying preview; its controlled synthetic seam
  requires verified Personal authority before parsing and persists through `PrivateUploadStore`.
  `UploadedLapService` refuses legacy User-ID-only reads. HTTP access remains fenced before model
  binding; retained controller size/multipart bounds apply only if an authorized workflow reopens.
- `PrivateUploadStore` — typed verified-owner persistence, dormant recovery and source-backed owner
  analytics under Driver/copy coordination; protected publication belongs to `DriverPublication`.
- `AdminService` — role + flag CRUD; delegates active-flag resolution to `FeatureFlagEligibility`.
  Users are **single-role** (`Standard` < `Beta` < `Alpha` < `Admin`).
- `FeatureFlagEligibility` — single owner of the role hierarchy and active-flag eligibility
  (`MinimumRole` level ≤ user level), shared by `AdminService` and `SubjectDriverContext`. Unknown or role-less
  users receive Standard eligibility; an unknown `MinimumRole` fails closed.
- `CachedIRacingClient` — mapped get-or-fetch over `IDataClient`; a miss requires an issued purpose and a preparation receipt captured before acquisition. Ordinary Real issuers remain unavailable. Commit failures propagate and cannot return fresh evidence. Over-length keys fail before fetching; input bounds belong on the `IRacingCacheKeys` factory (GHSA-jv96-89xc-98h2).
- `WholeCohortCandidates` / `Core.WholeCohortBands` — offline synthetic review artifacts with joint
  ranges and declared audience/owner variants. Before changing candidate grouping or review inputs,
  read `docs/research/whole-cohort-publication-candidates.md`. These artifacts confer no catalog
  admission, release reservation or protected dispatch.
- `ControlledCohortPublication` / `Data.PublicationReleaseStore` — controlled synthetic cohort
  execution reached through `DriverPublication.ReadControlledCohortAsync`. Before changing
  composition admission, whole-representation invalidation, writer drain or release-history
  reconciliation, read `docs/research/atomic-publication-reservations.md`. Ordinary startup does not
  enable this seam; controlled review fixtures confer no production catalog approval.
- `IndependentJournalRegistration` — binds the `INDEPENDENT_JOURNAL_*` settings and registers one
  `Data.IndependentJournal.BlobDriverJournal` as both `IDriverEnforcementJournal` and
  `IPublicationHistoryAuthority` only when every required one is present and every supplied one is
  valid. Otherwise it keeps the
  unavailable adapters, and its binding names the problem settings (never their values) for the
  startup warning.
- `SubjectDriverContext` — resolves an existing caller to the eligible synthetic Demo Driver;
  a stored Real claim yields no authorized Subject Driver. Optional callers use
  `GetSubjectDriverCustIdAsync`; required callers use
  `GetRequiredSubjectDriverCustIdAsync` / `RequireSubjectDriverCustId`, which throw the typed
  `IRacingNotLinkedException` mapped
  to the exact `409` contract above. `GetSubjectDriverAsync` also returns the selected provenance;
  under the selected Demo scope it resolves the Subject Driver to the Demo Driver
  (`DemoData.DriverCustId` = 100001) instead. An unavailable selected scope throws the typed `503`.
  `CONTEXT.md`'s Identity section defines Subject Driver / Claimed Identity / Demo Driver.

## Core models (`src/ApexRacers.Core/Models/`)

Domain entities below — see the `postgres-specialist` agent for full schema (PK types, schemas,
indexes, FK/`OnDelete` behavior).

| Model                                                                | Purpose                                                                                                                                                                                                                                                   |
| -------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ApplicationUser`                                                    | extends `IdentityUser<Guid>` — adds `DisplayName`, `IRacingCustomerId` (nullable; the user's Claimed Identity — see `docs/adr/0001-drivers-referenced-by-customer-id.md`), `ThemePreference`                                                              |
| `Series` / `Season` / `Week`                                         | series → season → race week (`Week.Id` is a Guid; carries weather summary JSON as an owned `WeatherForecastSnapshot`; nullable `EndTime` is iRacing's exact `week_end_time`, used by `Core.RaceWeekWindow` — see above)                                  |
| `Track` / `Car` / `CarClass` / `CarClassCar`                         | iRacing catalog + car-class membership. One `Track` is one drivable configuration, not a venue — several share a `Name` (see `docs/adr/0002-track-identity-follows-iracing-track-id.md`)                                                                  |
| `SeasonCar` / `SeasonCarClass` / `SeasonCarBop`                      | per-season cars/classes; per-week BoP (composite PK)                                                                                                                                                                                                      |
| `Subsession` / `SubsessionResult`                                    | one Split of a Race Session + per-Driver Race Result (+ race context; owned weather/track-state snapshot JSON). `RaceSessionId` persists iRacing's `session_id` so sibling Splits can be grouped; null means the Subsession predates that persistence. Only the race Sim Session's results are stored, and only for race Event Types; `CONTEXT.md`'s Race Sessions section defines the hierarchy. `SplitIndex`/`SplitCount` are nullable and derived from per-Split Strength of Field, not from array order — null is an unknown position, never index 0 (see `docs/adr/0003-split-index-is-derived-from-strength-of-field.md`). `TeamEntryCount`/`AiEntryCount` record the entries that produced no Race Result, so a Field with gaps is distinguishable from a complete one |
| `WeatherSnapshot` / `WeatherForecastSnapshot` / `TrackStateSnapshot` | SDK-independent persisted JSON contracts with pinned wire names                                                                                                                                                                                           |
| `QuarantinedDataCache` / `ProvenanceMigrationInventory` | Unclassified pre-cutover cache copies and observed Unknown row counts; unavailable to ordinary evidence reads |
| `DriverProofReceipt` / `DriverAuthorizationGrant` | Participating proof bindings and versioned personal/sharing authorization; no migration from legacy claims |
| `DriverLifecycleOperation` / `DriverPublicationAdmission` | Original-clock lifecycle progress and incarnation-bound protected-writer terminal checkpoints |
| `PublicationRelease` / `PublicationReleaseDependency` | Restricted composition-accounting bindings, original dispatch/terminal clocks and full-cohort authorization dependencies |
| `DriverTrackedCopy` / `DriverCopyCleanup` | Purpose/generation-bound copies and durable earliest-deadline cleanup work |
| `EvidencePurpose` / `EvidenceCopyMarker` / `EvidenceCopyDependency` | Issued purpose scope, durable copy versions/original clocks, and contributing-source bindings |
| `AuthorizedDriverNameCopy` | Name material bound to a current authorization grant and its revision |
| `ScopedDriverReference` / `PrivateDriverFollow` | hashed short-lived reference bindings and private Follow original loss/reactivation/removal clocks; restricted grant FKs, separate from legacy Rivals |
| `PrivateUploadSession` / `PrivateUploadedLap` | Typed verified-owner source and cascading Timed Laps, bound to original User/Driver/provenance and an evidence-copy marker |
| `DriverOperatingRecord` | Singleton controlled synthetic operating state, decision history, budget-window usage and reservations; see the operating admission boundary above |
| `UploadedLap`                                                        | Legacy claim-attributed Uploaded Lap; `DriverCustId` is the Driver the file named (null = not established); not promoted into verified private sources |
| `CarPercentileResult`                                                | Legacy selection-less cache in `iracing.LegacyUnclassifiedPercentileResults`; Preserved unclassified history, excluded from current readers and new seed production                                                                                                                                                                                  |
| `ClassifiedCarPercentileResult` | Classified current reading in `iracing.SelectionScopedCarPercentileResults`, bound to owner, canonical selection, Personal Best and preparation/source metadata; shared controlled synthetic refresh for API writers, verified typed private uploads and fresh Demo seeding; discovered coherent readers and controlled protected Personal dispatch; no Real activation |
| `FeatureFlag`                                                        | feature flag (`Key` unique; `MinimumRole`)                                                                                                                                                                                                                |
| `RefreshToken`                                                       | rotating refresh token (SHA-256 `TokenHash`; `identity` schema)                                                                                                                                                                                           |
| `ExternalDataCache`                                                  | mapped evidence (`Provenance` + `CacheKey` unique, owned DTO JSON) — backs `CachedIRacingClient`                                                                                                                                                            |
| `Rival`                                                              | a driver a user follows (unique on (Provenance, UserId, RivalCustId); cascade FK to Users)                                                                                                                                                                            |
| `SignInAddressFailure` / `SignInAccountFailure`                      | sign-in brute-force counters (issue #300) — failures per (User, source address) and per User across every address; `identity` schema, cascade FK to Users; back `Core.SignInThrottle`'s decision                                                          |
| `KnownDevice`                                                        | a browser that has completed a successful sign-in (issue #314) — SHA-256 `TokenHash` (unique) of the httpOnly cookie value, its own failure counter/window, `CreatedAt`/`LastSeenAt`/`ExpiresAt` (90-day lifetime, capped at 10 per User, least-recently-seen evicted); `identity` schema, cascade FK to Users; the second dimension of identity `Core.SignInThrottle`'s known-device exemption throttles against instead of the source address |

## iRacing data ingestion

`ApexRacers.Ingestion` is a `BackgroundService` using `Aydsko.iRacingData` with
`UsePasswordLimitedOAuth()` (four `IRACING_*` env vars). Resolve `IDataClient` + `AppDbContext` per cycle
through `IServiceScopeFactory` (scoped services from a singleton worker). Each run refreshes the full
car/track catalog into `Car`/`Track` via the pure, tested `CatalogIngest` helper. The worker fetches and
coordinates only: `SeasonIngest` owns season/schedule relational upserts, `SubsessionMapper` owns the
subsession/result field mapping, and `WeatherIngest` / `TrackStateIngest` map nested SDK blocks to owned
Core snapshots. (See `dotnet-api`.)

**Percentile refresh:** read `docs/research/percentile-history-refresh.md` before changing history
writers, batch preparation or source eligibility. `Data.PercentileRefresh` coordinates API,
verified private-upload and fresh Demo seed production; authenticated User identity selects the
Demo Subject, never a stored claim. Read `docs/research/percentile-history-readers.md` before changing
history discovery, immutable snapshots, weekly display, Demo read concurrency/connection cleanup
or protected Personal dispatch.

**Percentile storage cutover:** read `docs/research/percentile-history-storage.md` before changing
history storage, typed percentile copies, Demo seed classification or restore. Migration
`ClassifyPercentileHistory` preserves unclassified rows under a new physical name and fences old
binaries; it is forward-only. Current readers and fresh seeding use classified history; preserved legacy rows stay excluded. No old row acquires selection or Driver authority.

## Data source strategy — persist vs cache (read before adding an iRacing-backed feature)

Three ways iRacing data reaches a read path. Pick deliberately:

1. **Persist into typed entities** (worker/seeder → Postgres): `Series`, `Season`, `Week`, `Track`,
   `Car`, `Subsession`, `SubsessionResult`, `SeasonCarBop`, … Read paths query with SQL/joins. Pure
   SDK→entity mapping goes in a tested helper (mirror `CatalogIngest` / `SubsessionMapper`); relational
   upsert rules belong in a directly tested persistence module (mirror `SeasonIngest`). This includes
   JSON columns nested inside a persisted row: `Subsession.WeatherJson`, `Week.WeatherSummaryJson`, and
   `Subsession.TrackStateJson` serialize the owned `WeatherSnapshot`, `WeatherForecastSnapshot`, and
   `TrackStateSnapshot` (`ApexRacers.Core.Models`), mapped at ingest by the pure, tested `WeatherIngest`
   / `TrackStateIngest` seams (`ApexRacers.Ingestion`).
2. **On-demand cache** (`CachedIRacingClient` → `ExternalDataCache`): fetch live per request, memoize
   **mapped DTOs** as JSON with a per-call TTL. Backs progression, profile, race history, lap data,
   world records, leaderboards, standings, race guide, driver search.
3. **Persisted user-owned data**: typed `PrivateUploadSession` / `PrivateUploadedLap` under verified
   Personal authority, classified `ClassifiedCarPercentileResult`, legacy `UploadedLap`, `Rival`, `CarPercentileResult`, Identity. Stored User or
   Driver identifiers alone do not authorize reading this material.

Choose **persist (#1)** if you need to query/filter/aggregate/join it in SQL, it's canonical/shared, or
you need point-in-time history. Choose **cache (#2)** for read-mostly, staleness-tolerant, per-user/-query
stat lookups displayed roughly as-is (the common member/season-stat case). Don't keep a second cached
copy of something that already has an entity (e.g. the car/track catalog).

**Cache and persisted-JSON rules (#1 and #2):** cache/persist mapped DTOs or owned Core types,
**never** raw Aydsko SDK types (their wire shape drifts and carries `[Obsolete]` fields) — for a
persisted column this matters more than for the cache, since a cache row expires but a persisted row
does not: an SDK rename doesn't just break a fresh fetch, it silently deserializes a historical row
into a default-valued (zeroed) object with no exception.

Every cache key, TTL, purpose and optional Season scope is authored once, as a `CacheSpec` factory on
`IRacingCacheKeys` (`src/ApexRacers.Api/Services/IRacingCacheKeys.cs`) — that module is the single
source of truth for key format and freshness window, not this prose; a new cache-backed read path adds
a factory there rather than interpolating a key at the call site. **A factory that embeds unbounded
caller input must bound it before composing the key** (e.g. `MaxDriverSearchLength` on the driver-search
term) — `ExternalDataCache.CacheKeyMaxLength` (200) is a hard column limit, not a soft guideline, and
`CachedIRacingClient.GetOrFetchAsync` only refuses an already-over-length key as a backstop
(GHSA-jv96-89xc-98h2); a query-parameter value used as (or folded into) a key without its own bound —
a car class id, a race week index, a free-text term — is validated by the service against the data it
actually indexes (throwing `KeyNotFoundException`/`ArgumentException` as appropriate) before it ever
reaches `IRacingCacheKeys`. Reads require current purpose/copy/source metadata as well as freshness;
the cleanup service reconciles physical erasure against durable original deadlines.
Demo preservation and teardown use provenance and explicit preview incarnations.

**Demo cache seeding** (`ApexRacers.Seeder --demo` → `DemoCacheSeeder`): seeds `ExternalDataCache` rows
with synthetic mapped DTOs under each service's **exact** runtime cache keys — enforced by both trees
calling the same `IRacingCacheKeys` factories rather than by hand-matching interpolated strings — with
a far-future `ExpiresAt` for freshness. `DataProvenance.Demo` identifies synthetic copies;
`purge_demo_data.sql` mirrors that owned enum and purges only Demo copies. The seeder also seeds synthetic
`SeasonCarBop`, `Week.DemoWeatherSummaryJson`, a world record for every synthetic car+track (the percentile overlay and Week Detail), lap traces, and curated `/compare`
driver-search terms. The Seeder references `ApexRacers.Api` to reuse the real cached DTO types, so seeded
JSON matches what live services write. **Demo caveats** (not page-breakers): `/analytics` populates lazily
after a Recommendations/percentile visit; the race-guide board shows static "in-progress" sessions;
`/compare` search only hits a curated term set (arbitrary terms 503 — use the suggestions list instead);
the percentile page shows its manual customer-ID form rather than resolving the Demo Driver
automatically. That last one is **known and accepted**, not a bug to fix: `SubjectDriverContext` is the only
demo-aware resolver, and `PercentileController` deliberately takes a caller-supplied `customerId` so
the page can look up _any_ Driver. A demo User has no Claimed Identity (no real `IRacingCustomerId`),
so the `iracing_id` JWT claim the page reads first is absent and it falls through to the form. Enter
`100001` (`DemoData.DriverCustId`) to see the Demo Driver's percentiles.

**Provenance:** before changing acquisition, mapped caches, seeded evidence, teardown or migration,
read `docs/research/demo-acquisition-provenance.md`. It owns the request namespace, mapped-contract,
legacy quarantine, old-writer fencing and forward-recovery rules and their current evidence limits.

**Copy fencing:** before changing an evidence writer, derivative, cleanup, Demo reseed or copy-schema
deployment, read `docs/research/driver-copy-writer-inventory.md`. It owns purpose issuance, preparation
receipts, closed batches, source-version checks, retention clocks and the controlled migration handoff.
