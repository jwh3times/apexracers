---
name: dotnet-api
description: Use for any work in ApexRacers.Api, ApexRacers.Core, ApexRacers.Data, ApexRacers.Ingestion, or ApexRacers.Tests — controllers, services, EF Core models, entity configurations, DTOs, migrations, and xUnit tests.
tools: Read, Write, Edit, Bash, Glob, Grep
model: sonnet
---

You are working inside the ApexRacers .NET 10 backend. Know these patterns cold and enforce them without deviation.

The project guide `AGENTS.md` (already loaded into this session) already covers: the project/dependency graph, central package management (`Directory.Packages.props`), the controller→service→`AppDbContext` request flow, the `ExceptionHandlingMiddleware` error model, the controller/service/Core-model catalog, the ingestion worker, and the persist-vs-cache data-source strategy. **Don't restate those here** — this file adds the .NET-specific depth on top.

## C# style

- .NET 10, C# 13. Use primary constructors everywhere — no `private readonly` field boilerplate.
- File-scoped namespaces, top-level statements in Program.cs.
- `record` types for all DTOs. Positional records for simple shapes.
- Pattern matching and null-coalescing over verbose null checks.

## Controllers — the .NET specifics

AGENTS.md covers the no-logic rule. The details it doesn't: extract user identity from `User.FindFirstValue(JwtRegisteredClaimNames.Sub)` and parse the `Guid` before passing it to the service. When that User's Subject Driver is needed, resolve it through `SubjectDriverContext` and name the resulting local `subjectDriverCustId`; a Customer ID supplied for an arbitrary Driver lookup is already the Subject Driver and does not go through that context. For error cases **don't catch to `BadRequest(ex.Message)`** — let the service `throw` and `ExceptionHandlingMiddleware` map it (status map in AGENTS.md). Return an explicit result only for non-exception outcomes that need a specific code (e.g. a `404`/`501`). Note sign-in is not one of these: it has exactly two outcomes, `200` and a generic `401` — see "Account existence never leaks out of the auth surface".

## Services — the .NET specifics

AGENTS.md covers the service-layer rules (all logic here; inject `AppDbContext` directly; no MediatR / `IRepository<T>`; one responsibility per class). Conventions it doesn't:

- `async Task<T>` with `CancellationToken ct = default` as the last parameter on every public async method.
- Drive error flow by throwing — `ArgumentException` / `InvalidOperationException` → 400, `KeyNotFoundException` → 404, `UnauthorizedAccessException` → 401, `ClaimedIdentityConflictException` → 409, `IRacingNotConfiguredException` → 503 (see `ExceptionStatusMapper`). A claimed-identity conflict is ordinary ProblemDetails with non-disclosing detail; `IRacingNotLinkedException` is the separate 409 exact-JSON contract. Don't catch these back to `BadRequest`/`NotFound` in the controller.
- Don't swallow `OperationCanceledException` to "clean up" cancellation noise. Let it propagate: `ClientDisconnectDetector` already distinguishes a client disconnect (token signalled → Debug + 499) from a real server-side cancellation (→ Error + 500), and catching it in a service destroys the distinction.
- When a controller returns a bare status result the **user will see**, give it a message: `Problem(detail: "…", statusCode: …)`, not `Unauthorized()`/`NotFound()`. ASP.NET Core's automatic ProblemDetails carries only type/title/status/traceId, and the web client renders `detail` — a bare result leaves it with nothing human-readable. Keep the wording non-enumerating for auth failures ("Invalid email or password.", never "no such account"). Internal guards that can't reach a user (e.g. an unparseable `sub` claim) can stay bare.
- Competitiveness metrics are never re-derived inline: call `ApexRacers.Core.FieldPercentile.Rank` / `Position` / `TopSharePercent` / `FieldSize` / `MedianOfSorted`. Every one of them takes `otherDriversLaps` — the field **excluding** the ranked driver, built with `.Where(d => d.CustId != customerId)` — and reconstructs the Field internally as those laps plus the ranked lap. Hand the raw field in and the driver is counted twice, because the row bearing their customer id is not necessarily the value being ranked (an Uploaded Lap can supersede it).
  - `Rank` is a percentile rank in the ordinary statistical sense: `(slower + 0.5 × tied) / fieldSize`, the driver included in their own denominator, ties split. It does not reach 0 or 100, and a lone driver sits at 50. Do not "fix" it back to strictly-slower-over-others — see `CONTEXT.md`'s Competitiveness section.
  - **A DTO's `FieldSize` is `FieldPercentile.FieldSize(otherLaps)`, not the row count you queried.** The queried rows contain the driver only when they actually raced, so reporting their count makes the same number mean two different things. A projected-only recommendation has no current-week Field membership and therefore carries `FieldSize: null`.
  - **A Top Share cannot be derived from a percentile rank** — the rank splits ties and counts the driver in its denominator, so it does not invert. Compute it with `TopSharePercent` server-side and carry it on the DTO; that is why `CarPercentileResult` stores it rather than recomputing on read.
  - Distinct from `CarRecommendationService`'s `RunningAveragePercentile`, which averages successive percentile *readings* into an Expected Percentile. The recommendation contract carries that value in `ExpectedPercentile`; it is never substituted into `PercentileRank` and has no Field Position, Top Share, or Field Size.
- The race-vs-uploaded choice behind a Personal Best is never re-compared inline either: call
  `ApexRacers.Core.PersonalBest.Select(raceBest, uploadedBest)` (nullable overload) or
  `Select(raceBest, uploadedBest)` (non-nullable overload, for a caller that has already proved the
  Race Best exists) and carry the returned `PersonalBest.Evidence` onto the DTO. Carry it alongside
  `LapSeconds` when the DTO exposes the lap; a compact DTO that exposes only a derived reading still
  carries the evidence (`WeekCarPercentileDto` is the canonical example). Don't discard the evidence
  just because the response omits the winning `double`. **A Race Best wins an exact tie**: it was set
  against the Field being ranked, so it is the better-evidenced of two equally fast laps; this is
  the same strict `<` test `PercentileCalculationService`, `CarRecommendationService`, and
  `UserAnalyticsService` each ran by hand before they shared this seam. `UserAnalyticsService`
  additionally keys its per-week `PersonalBest` by `(CarId, WeekId)` before reducing across weeks, so
  the fastest lap across the counted weeks keeps its own evidence instead of inheriting whichever
  week was last in iteration order.
- **A Telemetry Upload is validated before anything is written.** `TelemetryUploadService` refuses a file whose recording Driver (`DriverUserID`, absent → 0 → stored as null, never compared) disagrees with the caller's Claimed Identity, and rejects unknown Car or Track catalog IDs before writing any Uploaded Laps. Upload metadata must never create or modify catalog entries; a refused upload must leave no trace. A caller with no Claimed Identity has nothing to check against and is accepted, with the file's Driver still recorded on the lap. Keep any new validation in that same pre-write block rather than adding it after the first `db.Add`.
- **The two `TelemetryUpload` size bounds are not interchangeable — keep `MaxRequestBytes` strictly above `MaxFileSizeBytes`.** `TelemetryController.UploadAsync`'s `[RequestSizeLimit]` / `[RequestFormLimits]` attributes read `TelemetryUpload.MaxRequestBytes` and cut a request off *during model binding* — before the action runs, and before `TelemetryUploadService` sees it — answering a bare `400` ("Failed to read the request form.") that never reaches `ExceptionHandlingMiddleware`. The action's own `file.Length > TelemetryUpload.MaxFileSizeBytes` check runs *after* that buffering has already happened, so it saves no I/O; what it buys is a `413` naming the limit instead of the framework's generic form-read failure. A multipart request carrying a file at exactly `MaxFileSizeBytes` is necessarily larger than that (boundary markers, part headers), so setting the two bounds equal would make the in-action check unreachable and hand every oversized upload to the framework's less specific rejection — that gap is GHSA-6hf4-vppr-mxpj's fix (the upload page had advertised 250 MB while the API's only enforced bound was a hardcoded 500 MB).
- Uploaded Best rows are never projected inline either: call `UploadedBestQuery.RunAsync(scope, order, ct)` (`src/ApexRacers.Api/Services/UploadedBestQuery.cs`). It ranges over Uploaded Laps only, so what it returns is an Uploaded Best, not a Personal Best — a Personal Best also weighs the Subject Driver's Race Best. Two invariants it owns, both easy to reintroduce by hand if a caller writes its own version:
  - **Every persisted Uploaded Lap is already a Timed Lap.** `TelemetryUploadService` rejects untimed parser rows before insertion, so neither the caller's `scope` nor `UploadedBestQuery` needs a validity or timed predicate.
  - **The `GroupBy` must not be pushed into SQL.** The group key is `{ CarId, TrackId }` — identifiers, never `Car.Name`/`Track.Name`/`Track.ConfigName` labels, since a Track's `Name` belongs to the venue and is shared by every layout there. Those labels ride along via `g.First()` instead: selecting navigation-property labels alongside the aggregates is what neither Npgsql nor SQLite translates, so the query materializes to a list first and groups in memory — deliberately, not an oversight. Getting this wrong throws at runtime rather than failing to compile; see the order/project-by-entity-columns-before-DTO rule in AGENTS.md's Testing section, which is the same underlying translation gap.
  - `UploadedBestQuery` has no Race Week bound — it is the all-time Uploaded Best, used where that is
    the correct answer (My Laps, the catalog overlays). `PercentileCalculationService`,
    `CarRecommendationService`, and `UserAnalyticsService` deliberately do **not** call it: ranking a
    Personal Best needs the Uploaded side bounded to one Race Week (see the `RaceWeekWindow` rule
    below), which `UploadedBestQuery` has no parameter for. Each of those three writes its own scoped
    query instead — relying on the same persisted-Timed-Lap invariant and still grouping/ordering by
    `{ CarId, TrackId }` in the same style, just with an added `RecordedAt` range filter. Don't read that as three call sites
    forgetting the shared helper; it's the same helper's invariants without its all-time scope.
- **A Personal Best's Uploaded side is bounded to the Race Week being ranked, never fetched all-time.**
  Call `AppDbContext.RaceWeekWindowAsync(seasonId, raceWeekIndex, ct)` (single Week) or
  `RaceWeekWindowsAsync(seasonId, ct)` (whole season, keyed by Race Week Index — `SeasonQueries`,
  `src/ApexRacers.Api/Services/SeasonQueries.cs`) to get a `Core.RaceWeekWindow`, then filter
  `UploadedLap.RecordedAt` with `window.Contains(recordedAt)` (or the equivalent `>= Start && <
  End` range pushed into SQL — see the SQLite-vs-Npgsql translation note in the Tests section below).
  Fetch the whole season's windows rather than one Week's when the caller is iterating rows that each
  belong to a different Week (`UserAnalyticsService`) — a Week with no reported `EndTime` is bounded by
  the *next* Week's start, which isn't derivable from that Week's own row alone. When a faster Uploaded
  Lap exists outside the window, surface it rather than silently dropping it: project it separately
  (`OrderBy(LapTimeSeconds).FirstOrDefault()` over the *excluded* range, kept apart from the `MIN`
  over the *included* range so the lap and its own `RecordedAt` travel together — a grouped `MIN(lap)`
  beside `MAX(RecordedAt)` would attribute the wrong date to it), and only surface it when it's
  actually faster than the Personal Best that got ranked; a slower excluded lap changes nothing and
  reporting it would be noise. `CONTEXT.md`'s Personal Best entry is the product rationale.
- Personal Best read paths take a `PersonalBestEvidence` value rather than separate booleans or lists.
  Controllers construct it with `FromRequest(includeUploadedLaps, uploadedLapTypes)`; internal callers
  with no user choice pass `OfficialRaceLapsOnly`. Use `ScopeUploadedLaps` to apply source and session-type
  eligibility, then pass that scope to `UploadedBestQuery.RunAsync` so it remains the owner of Uploaded
  Best projection. An empty selected-type list means all types; do not duplicate
  either module's predicates in a service.
- Resolving a series' current season or one numbered week of it is never a hand-written
  `Where(... && s.Active).OrderByDescending(Year).ThenByDescending(Quarter)` — that ordering picks
  whichever season iRacing flagged active most recently, which during a changeover is the *incoming*
  season before it has raced a single week, not the one drivers are actually racing. Call the
  `SeasonQueries` extensions (`src/ApexRacers.Api/Services/SeasonQueries.cs`) instead:
  `AppDbContext.CurrentSeasonIdAsync(seriesId, ct, today?)` / `CurrentSeasonIdsAsync(seriesIds, ct,
  today?)` (batched — one query per set of series, not per series), `IQueryable<Week>.InSeason(seasonId,
  raceWeekIndex)`, `AppDbContext.CurrentSeasonOrThrowAsync` (the one
  `KeyNotFoundException("No current season for series {id}.")` wording), `AppDbContext.SeriesNameAsync`.
  The `today` parameter exists only so tests can sit exactly on a changeover boundary — production
  callers never pass it. The selection rule itself — the season whose first race week began most
  recently, `Active` deliberately not a filter — is
  `ApexRacers.Core.SeasonCalendar.CurrentSeasonId(IEnumerable<SeasonStart>, DateOnly today)`; don't
  reimplement it inline, and don't reintroduce a `Year`/`Quarter`-only ordering as a shortcut — that's
  exactly the bug this replaced. `InSeason` composes on an already-resolved season id (a `CurrentSeasonId*`
  call is a round trip, so callers await it once before composing the week projection they need).
- "Which week is the season currently in" is never re-derived per caller either — call
  `ApexRacers.Core.SeasonCalendar.CurrentRaceWeekIndex(weeks, today)`: latest week whose start date is
  on/before `today`, Race Week Index only breaking a tie. It returns `null` before the season starts
  **by design** — the pre-season fallback (blank cell vs. first week) is a per-caller UI choice, not
  something this method should decide for every caller.

### `CachedIRacingClient` — the get-or-fetch seam

`CachedIRacingClient(AppDbContext db, IDataClient? client)` — `GetOrFetchAsync<T>(CacheSpec spec, Func<IDataClient, Task<T>> fetch, CancellationToken ct)`. `CacheSpec` (`Key` + `Ttl`) always comes from a factory on `IRacingCacheKeys` (`src/ApexRacers.Api/Services/IRacingCacheKeys.cs`) — that module is the sole author of every key string and its TTL; adding a cache-backed read path means adding a factory there, never interpolating a key at the call site. `client` is nullable rather than resolved from an `IServiceProvider`: it's registered in `Program.cs` via an explicit factory lambda (`sp.GetService<IDataClient>()`) because the SDK client itself is only registered when all four `IRACING_*` credentials are present; a null `client` on a cache miss throws `IRacingNotConfiguredException`. There is no `IsConfigured` property — check for a 503 by attempting the call, not by probing state first.

**Bound unbounded caller input before it reaches a key, not after (GHSA-jv96-89xc-98h2).** A key
factory on `IRacingCacheKeys` that folds in free-text or ID-shaped caller input owns its own length
bound beside the factory — `MaxDriverSearchLength` next to `DriverSearch(...)`, checked via the paired
`TermIsTooLong(...)` at the call site (`RivalService.SearchDriversAsync`) before the factory is even
called. `GetOrFetchAsync` also throws `ArgumentException` when `spec.Key` exceeds
`ExternalDataCache.CacheKeyMaxLength` (200), but treat that as a backstop, not the control: a key that
reaches this check has already failed to persist once, and the existing
`catch (DbUpdateException) when (row is null)` a few lines below — written for the legitimate
cold-start uniqueness race — cannot tell that failure apart from a real one without the explicit length
check, which is exactly how an over-long key used to degrade into a live iRacing fetch on every
request instead of an error. A caller-supplied value that indexes something the service already
knows the valid set of (a car class id, a race week index) isn't a key-length problem at all — validate
it against that set (`StandingsService.ResolveAsync`/`GetQualifyResultsAsync` throw
`KeyNotFoundException` for a `carClassId`/`raceWeekIndex` not in the current season) before it ever
composes a key, the same way a service-level check guards the free-text case.

Driver search additionally sits behind its own rate-limit policy (`"iracing-search"` in `Program.cs`,
config `SEARCH_RATE_LIMIT_PERMIT_PER_MINUTE`, default 30/min, partitioned by the `sub` claim) —
alongside the project guide's `auth` policy — because the length bound caps one key's size but not how
many distinct valid terms one caller can mint per minute, each its own cache row and its own upstream
fetch.

## DTOs

`record` types — response shapes in `Dtos/ResponseDtos.cs`, request shapes in `Dtos/RequestDtos.cs`. (AGENTS.md notes the `ResponseDtos.cs` ↔ `web/src/services/api.ts` sync requirement — honor it when you change a response DTO.)

The project guide owns the Driver identity names on ApexRacers response DTOs. The .NET-specific
boundary rule is to translate upstream JSON/SDK spellings in parser and mapping adapters. If a renamed
response DTO is one of the mapped types stored in `ExternalDataCache`, migrate its existing cache-key
families explicitly; never rewrite unrelated cached SDK payloads by matching property names globally.
The same boundary applies to positional names: response DTOs expose `RecommendationRank` for the
ordering ApexRacers computes over Cars and `Standing` for a position iRacing awards to a Driver.
Translate an upstream SDK or CSV `Rank` member in the mapper/parser; do not carry that ambiguous name
across the ApexRacers contract boundary. `PercentileRank` remains the separate population statistic.

## AppDbContext and schemas

`AppDbContext` extends `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`. Default schema is `iracing` (`HasDefaultSchema("iracing")`); ASP.NET Identity tables are moved to the `identity` schema with explicit `ToTable(..., "identity")` overrides that must run **after** `base.OnModelCreating(modelBuilder)` — ordering matters.

The entity-config mechanics (one `IEntityTypeConfiguration<T>` per entity in `src/ApexRacers.Data/EntityConfigurations/`, Fluent-API-only, explicit `OnDelete`) plus the full schema, PKs, and indexes are the `postgres-specialist` agent's lens.

## Migrations

The `dotnet ef` commands and the `dotnet-ef`/EF version-match note are in AGENTS.md (Commands). Beyond those: migrations **auto-apply at API startup** via `db.Database.MigrateAsync()`, so write them to apply cleanly on boot (the prod deploy story is the `azure-infrastructure` agent's). `DesignTimeDbContextFactory` reads `DATABASE_CONNECTION_STRING` or falls back to a hardcoded local dev string, so `dotnet ef` needs no env var locally.

## Auth and RBAC

- JWT HS256, **15-minute access token expiry**, `ClockSkew = TimeSpan.Zero`, `MapInboundClaims = false`.
- Claims in token: `sub` (Guid user ID), `email`, `name`, `role`, `iracing_id` (optional), `theme_preference`.
- The token contract (signing key, issuer, audience) is bound exactly once, as `JwtSettings.FromConfiguration(config)` in `Program.cs`, and shared as a singleton by both sides that need it: `Program.cs`'s `TokenValidationParameters` (validating) and `AuthService.GenerateJwtAsync` (issuing, via a constructor-injected `JwtSettings jwt`). **Never read `JWT_SIGNING_KEY`/`JWT_ISSUER`/`JWT_AUDIENCE` from `IConfiguration` directly outside `JwtSettings`** — the two sides must derive the identical `SymmetricSecurityKey`/issuer/audience, and a mismatch (e.g. one side keeping a stale default while the other changes) is a total-auth outage with no compile error and, if the test suite constructs its own `JwtSettings` instead of binding through `FromConfiguration`, no failing test either. `AuthService`'s constructor is `(UserManager<ApplicationUser> userManager, IConfiguration config, JwtSettings jwt, RefreshTokenStore refreshTokens, IEmailSender emailSender, SignInThrottleStore signInThrottle, KnownDeviceStore knownDevices, IOutboundEmailQueue emailQueue, ILogger<AuthService> logger)` — `config` remains only for `APP_BASE_URL` (email links), not JWT settings. Any new `AuthService(...)` construction site (tests included) needs the `JwtSettings` and store arguments; bind the former with `JwtSettings.FromConfiguration(config)` from the same `IConfiguration`, and construct the latter with the test `AppDbContext` plus a controlled `TimeProvider` when time matters. `SignInThrottleStore` and `KnownDeviceStore` likewise each need a `SignInThrottleOptions` (use `SignInThrottle.Defaults` in tests unless the test is specifically about tuning) and the same `TimeProvider` — `KnownDeviceStore` reads `SignInThrottleOptions.PerAddressWindow`/`AccountWindow`/`AccountHighWaterFailures` to keep a device's window and the under-attack test defined identically to the address path.
- `JwtSettings` builds **both** sides of the contract, not just the key: `IssuingCredentials()` for `AuthService.GenerateJwtAsync` and `ValidationParameters()` for `Program.cs`'s `AddJwtBearer`. Don't hand-build a `SigningCredentials` or a `TokenValidationParameters` anywhere else, tests included — that is how `JwtSettings.Algorithm` (HS256) came to be pinned on the issuing side and left unset on the validating one, where any HMAC variant the library accepts for a symmetric key would have validated. A test that restates the validation parameters by hand is the same drift with a passing suite on top of it.
- `FromConfiguration` rejects a `JWT_SIGNING_KEY` shorter than `JwtSettings.MinimumSigningKeyBytes` (32 bytes / 256 bits), measured as **UTF-8 bytes** because that is what reaches HMAC — not characters. This is a startup failure by design: a key weaker than the HMAC-SHA256 digest is brute-forceable offline from one captured token, and startup is the last moment an operator sees why. Don't relax it to a warning, and don't move the check to a use site — the single bind point is what makes it unavoidable.
- Roles: `Standard` (default on register), `Beta`, `Alpha`, `Admin`.
- RBAC policies use `RequireClaim("role", ...)`, **not** `RequireRole`. Existing policies:
  - `AdminOnly` → `RequireClaim("role", "Admin")`
  - `AlphaOrAbove` → `RequireClaim("role", "Alpha", "Admin")`
  - `BetaOrAbove` → `RequireClaim("role", "Beta", "Alpha", "Admin")`
- Self-service role changes (Standard/Beta/Alpha) via `PUT /api/auth/role`; Admin cannot self-demote.
- Email-change requests require the current password before address lookup or email delivery.
  Profile updates require it before any mutation when a supplied non-null Customer ID differs from
  the stored Claimed Identity. An omitted/null ID keeps the existing claim; an unchanged ID and
  display-name/theme-only updates remain password-free. Missing and incorrect passwords share one
  non-disclosing error. This checks the local User's password, not ownership of the iRacing Driver.
- Admin promotion uses `AdminSeedService` at startup; the project guide owns its eligibility rules.
  `AdminService.SetUserRoleAsync` rejects an Admin target role using case-insensitive comparison
  (matching Identity role lookup), and rejects changes to an existing Admin.

### Account credentials never leave through a response

Account-confirmation, password-reset, and email-change tokens are single-use credentials. They leave
the server **only** inside the link `AccountEmailTemplates` builds, and they are never returned in a response body,
never written to a log, and never surfaced by any endpoint — not even behind an
`IWebHostEnvironment.IsDevelopment()` check. `AuthService.RequestPasswordResetAsync` deliberately
returns `Task`, not the token, so no controller can echo it; do not "helpfully" restore a return
value. It previously returned the token for a Development-only echo in
`POST /api/auth/forgot-password`, which turned any network-reachable Development instance into
full account takeover from nothing but an email address (GHSA-qmqp-gxpr-867g). The forgot-password
response is the same generic acknowledgement in every environment and whether or not the account
exists.

To exercise a link-bearing flow without an email provider, set `DEV_MAIL_DROP_PATH` and read the
`FileDropEmailSender` output — `EmailDelivery.Select` throws on startup if that variable is set
outside Development. Service tests read tokens the same way, out of `FakeEmailSender` via
`EmailLinks.TokenFrom`, rather than from a return value.

### Account existence never leaks out of the auth surface

No endpoint tells an unauthenticated caller whether an address has an account. Login, forgot-password
and email-change already held that line; registration is the one that had to be rebuilt for it
(GHSA-72v6-mw4c-q96r), and the rules it now runs under are the ones to keep:

- `AuthService.RegisterAsync` returns `Task`, not an `AuthResultDto`. The controller answers with a
  fixed `MessageResponse` in every case — free address, taken address, and a race between them all
  reach the same body. Do not restore a return value, and do not add a status code that varies with
  the outcome.
- A duplicate is disclosed **only to the mailbox**: `NotifyAddressAlreadyRegisteredAsync` resends the
  confirmation link to an unconfirmed account and sends the security notice to a confirmed one. That
  branch must stay invisible to the caller: it changes what is sent and to whom, never what the
  response says. (Both paths send exactly one email, but the free path also writes a user row, so
  the two are not claimed to be indistinguishable by timing — only by what they return.)
- Identity errors are filtered by code, not by message text. `IsDuplicateAccount` withholds
  `DuplicateUserName`/`DuplicateEmail`; everything else describes the values the caller submitted and
  still surfaces. `UserManager.CreateAsync` validates the password before it touches the store, so a
  weak password is rejected identically for a taken and a free address — don't reorder that by
  checking existence first, which would make the password error the new oracle.
- `LoginAsync` refuses an unconfirmed account with the same null an unknown
  address gets, **before** the sign-in throttle claim. This is the half that actually
  closes the oracle: a generic registration response alone would still let an attacker register a
  victim's address and read the answer off whether their chosen password then works. Checking ahead
  of the throttle also stops a stranger throttling an account from the moment it signs up.
- Confirmation is what makes an account usable, so anything that proves control of the mailbox must
  grant it. `ResetPasswordAsync` sets `EmailConfirmed`; without that, a user whose confirmation email
  went astray could reset a password and still be refused, with no self-service route back.

Sign-in is gated on `EmailConfirmed`, so any environment that creates accounts outside registration
(seeders, fixtures, an operator bootstrapping an Admin) must set it. Accounts predating the
requirement were grandfathered by the `ConfirmPreExistingAccountEmails` migration.

Sign-in and password reset carry the rest of it (GHSA-28pc-cx5w-g6jp):

- `LoginAsync` returns `AuthResultDto?`, and **every** refusal is the same null. The return type has
  nowhere to put a reason on purpose — it used to be a `LoginResult` record whose `LockedOut` flag
  became a `423`, and only a real account can be locked, so five wrong passwords against a registered
  address returned `423` while an unregistered one returned `401` forever. Do not reintroduce a type,
  status, or body that varies with why the sign-in failed.

### Sign-in brute-force protection is per (account, source address), not per account

Issue #300 was the last open finding of GHSA-28pc-cx5w-g6jp: counting failures per account meant a
stranger who merely knew a Driver's email could keep that Driver signed out indefinitely for five
requests every fifteen minutes. `Program.cs` now sets `options.Lockout.AllowedForNewUsers = false`,
and it must stay off — `ApplicationUser.AccessFailedCount`/`LockoutEnd` still exist as Identity
columns but nothing reads them. Brute-force protection is `ApexRacers.Core.SignInThrottle` (decision)
+ `SignInThrottleStore` (persistence, `identity.SignInAddressFailures` / `SignInAccountFailures`),
scoped to (account, source address):

- **Never call `AccessFailedAsync`/`IsLockedOutAsync` on the sign-in path.** Those are Identity's own
  lockout, counted per account regardless of who produced the failure — reintroducing either rebuilds
  issue #300. The counters live in `SignInThrottleStore` now, not on the user.
- `LoginAsync` claims the attempt via `SignInThrottleStore.ClaimAttemptAsync` **before** the password
  is checked, and a refusal there records nothing — an attempt never checked carries no information,
  and counting it would let a caller hold its own window open by continuing to knock. `ClaimAttemptAsync`
  itself is a single atomic SQL upsert rather than a read-then-check-then-write, because the gap
  between reading a counter and writing it back is wide enough (tens of milliseconds of PBKDF2) for a
  concurrent burst to have every request read the same stale count and all pass the gate — exactly
  where the design leans hardest, since the tightened allowance is 1.
- The account-wide high-water mark (`SignInThrottleOptions.AccountHighWaterFailures`, default 50/hour)
  **must never deny a sign-in by itself.** Crossing it only shrinks the per-address allowance (default
  5 down to `TightenedPerAddressMaxFailures`, default 1); an address with no failures against it is
  still admitted on the first correct password. `SignInThrottleOptions.Validated()` (called at
  startup) throws if the tightened allowance or the high-water mark could be `0` — either would
  silently refuse the account owner too, rebuilding the denial of service. Don't relax that check or
  move it out of startup.
- **Never report the throttle state on a correct password**, tempting as it sounds. Doing so turns
  the throttle window into a password oracle: an attacker guessing through it would learn from a
  differing response that they had found the password, which is the one thing the throttle exists to
  stop. The owner is told by email (`AccountEmailTemplates.SuspiciousSignInAttempts`, paced by
  `SignInThrottleStore.NoteFailureAsync`/`ClaimNoticeAsync` to one per account per
  `SignInThrottleOptions.NoticeInterval`) instead.
- That notice **must be queued (`IOutboundEmailQueue.Enqueue`), never sent inline from `LoginAsync`**.
  It fires only for addresses that have an account, so an inline send would cost real accounts a mail
  round trip that unknown addresses never pay — the account-existence oracle again, this time by
  latency, or by a 500 when mail delivery fails. `OutboundEmailDispatcher` (a `BackgroundService`)
  drains the queue outside any request.
- A correct password clears only the winning address's row (`ClearAddressAsync`). The account-wide
  counter is deliberately left alone — the owner signing in from their own machine is not evidence
  that whoever is guessing elsewhere has stopped — and expires on its own once failures actually stop.
- Every refusal path still goes through `RefuseWithoutDisclosing`, which verifies the submitted
  password against a cached stand-in hash before returning. Without it an unknown address answers
  before any hash is computed while a real one pays the full PBKDF2 cost, and that gap is readable on
  the clock. It equalises the dominant cost, not the whole request — describe it as closing the
  measurable gap, not as constant time.
- **The shared-address residual is closed for a known device, not eliminated.** The throttle is scoped
  to source address, so an attacker sharing one with the Driver (CGNAT, a corporate egress, a shared
  VPN exit) can still exhaust that address's allowance and deny the Driver from it — unless the Driver
  is signing in from a browser that has already completed a successful sign-in to the account. See
  "The known-device exemption (issue #314)" below for what closed it and the residual that remains
  (a Driver on a genuinely new device, on a hostile network, is still bounded by the address window).
- `ResetPasswordAsync` answers `InvalidResetRequest` for both an unknown address and an `InvalidToken`
  result. Identity verifies the token before it validates the new password and returns on the first
  failure, so password-policy errors are only reachable once a valid token has been presented — by
  someone who already controls the mailbox — and those still surface, because collapsing them would
  strand a real user with no idea why their new password was refused.

### The known-device exemption (issue #314)

`SignInAsync` (the internal method `LoginAsync` now wraps) resolves a caller-presented cookie to a
`KnownDevice` via `KnownDeviceStore.RecogniseAsync` **before** the account is looked up, and keyed
**only** on the presented token — never on the account named in the request. That ordering is the
whole of what keeps the exemption from becoming a second account-existence oracle alongside the ones
GHSA-72v6-mw4c-q96r and GHSA-28pc-cx5w-g6jp closed: the lookup runs for every caller who sends a
cookie and for none who don't, so its cost tracks something the caller already knows, not whether the
address they typed has an account.

- **A recognised device is charged against both scopes, judged on its own verdict.** When the resolved
  device belongs to the account being signed into, `SignInAsync` claims an attempt against **both**
  `KnownDeviceStore.ClaimAttemptAsync` and `SignInThrottleStore.ClaimAttemptAsync`, and the address
  claim's `Refused` is discarded while the device has room. Charging only the device would let a
  caller spend its window, drop the cookie, and spend the address's window too — making the ceiling
  the *sum* of the two; charging both makes it the *larger* of the two, so dropping the cookie
  mid-attack buys nothing.
- **A device may only ever add allowance, never remove one.** When `ClaimAttemptAsync` returns
  `Refused: true` (or `null`, because the row vanished between recognition and the claim), the caller
  falls back to the address's own verdict rather than being refused outright. Refusing here would let
  anyone who copied a cookie deny the Driver — who presents the identical cookie — with the correct
  password from any network: issue #300's shape rebuilt on a new key, and cheaper to reach than the
  shared-egress attack #314 set out to fix.
- **The tightened state exempts a known device entirely.** `Core.SignInThrottle.AllowanceFor(underAttack,
  knownDevice, options)` is the single definition both `Evaluate` (address path) and
  `KnownDeviceStore.ClaimAttemptAsync` (device path, which cannot use `Evaluate` because it tests a
  count it just wrote with `>`, not a count read before the attempt with `>=`) read from — a recognised
  device keeps the ordinary allowance even while the account-wide high-water mark is crossed. Do not
  restate the allowance rule in either path; call `AllowanceFor`.
- **One success clears the device unconditionally.** `SignInAsync` calls
  `KnownDeviceStore.ClearFailuresAsync` for this account's device on **any** successful sign-in, not
  only when the device was the scope that admitted the caller — a thief who pinned the device counter
  and then the Driver signs in through the address must not leave the device counter spent for the
  rest of that window. The address counter is cleared only when it was the actual gating scope, for
  the pre-existing reason (clearing it on a device-scoped success would hand a fresh address allowance
  to whoever the Driver is sharing the address with).
- **`NoteFailureAsync`'s owner notice still fires on the device path**, paced the same as the address
  path. An earlier draft skipped it, reasoning a recognised device running out is the Driver mistyping
  — true in the common case, but with the shipped numbers a device-scoped caller tops out well below
  the account-wide high-water mark, so skipping it would silence the only detection signal for exactly
  the adversary the exemption empowers (someone who stole the cookie and is guessing unobserved).
- **Remembering the device must never cost a correct password its sign-in.** `KnownDeviceStore.RememberAsync`
  runs inside a `try`/`catch (Exception ex) when (ex is not OperationCanceledException)`; a fault there
  logs a warning and returns no cookie rather than failing the request — the worst case is a lost
  exemption, never a 500 on the right password. `OperationCanceledException` still propagates: that's
  the caller giving up, not a device-table fault.
- **Password change, reset, and email-change forget every device, alongside revoking refresh tokens**
  (`KnownDeviceStore.ForgetAllAsync`, beside the existing `RefreshTokenStore.RevokeAllActiveAsync`
  call at each site). Skipping it would leave a device record outliving the credential change it's
  supposed to remedy — a standing guessing allowance against the *new* password for the rest of the
  90-day lifetime, reachable by anyone who still holds the old cookie.
- **The cookie itself lives entirely in `KnownDeviceCookie`** (`src/ApexRacers.Api/Services/KnownDeviceCookie.cs`)
  — the only place its name and `CookieOptions` are decided, read, or written. `AuthController` calls
  `KnownDeviceCookie.Read`/`Write`; nothing else should touch `Request.Cookies`/`Response.Cookies` for
  this cookie. Two attributes are load-bearing, not stylistic:
  - **Name is `__Host-apexracers_device` wherever the environment can be `Secure`** (i.e. everywhere
    but Development over plain HTTP, where it falls back to unprefixed `apexracers_device`). The
    `__Host-` prefix is what makes a browser refuse to accept the cookie if it carries a `Domain`,
    arrived over plain HTTP, or has a non-root `Path` — which is what stops a compromised sibling
    subdomain or a network attacker against any plain-HTTP host on the same registrable parent domain
    from planting a same-named cookie that shadows the real one. Reading only accept the name the
    current environment writes; accepting the unprefixed name in a `Secure` environment would hand
    back exactly the shadowing the prefix exists to prevent.
  - **`Secure` follows `KnownDeviceCookie.IsSecure(env)` (the hosting environment), never
    `Request.IsHttps`.** That property only reflects the client's real scheme once forwarded headers
    are processed, and that registration is gated on an app setting whose absence the host only warns
    about (see "Forwarded headers" below) — keying `Secure` off it would mean a dropped setting
    silently downgrades a 90-day cookie to one sent in the clear and overwritable by a network
    attacker.
- The device cookie is never accepted from the request **body** — only from the cookie header via
  `KnownDeviceCookie.Read`. A device name the page's own script could submit is a device an injected
  script could mint, defeating the `HttpOnly` protection entirely.
- `SignInThrottle`'s XML remarks (`src/ApexRacers.Core/SignInThrottle.cs`) are the canonical statement
  of what this exemption closes and what residual remains after it; read them before changing any of
  the rules above.

### Refresh token rotation

`AuthService` delegates the complete refresh-token lifecycle to `RefreshTokenStore`, which issues a **7-day rotating refresh token** alongside every JWT. Rules:

- Raw token: 64 random bytes (via `RandomNumberGenerator.Fill`) encoded as Base64.
- Stored in DB as SHA-256 hash (`RefreshToken` entity in `identity.RefreshTokens`). The raw token is never persisted.
- Active means exactly `RevokedAt == null && ExpiresAt > timeProvider.GetUtcNow()`; a token expiring exactly now is inactive. Keep every active-token query behind the store's canonical predicate rather than adding a time-reading property to the entity.
- `RotateAsync(rawToken)`: looks up the hash including revoked rows. Presenting a retained revoked
  token revokes that user's active refresh tokens before rejecting the request, even if the presented
  token has also expired. Unknown tokens and unrevoked expired tokens only reject. Successful rotation
  consumes the old token and inserts its replacement inside one explicit transaction; it is cap-exempt.
  Reuse warnings contain the User ID, never raw credentials or their hashes. This is account-wide
  revocation, not a persisted per-device token-family model; issued access tokens keep their expiry.
- `RevokeAsync(rawToken)`: best-effort; unknown and already-revoked credentials are no-ops. A specifically presented expired credential may still be stamped revoked.
- Issuance caps active tokens per user at 5 by revoking the oldest before adding the new token; `RevokeAllActiveAsync` touches only canonically active rows.
- **The initial read in `RotateAsync` decides whether a token *looks* usable, never who wins a race
  (GHSA-87m2-6r5g-9q47).** Two requests can present the same refresh token concurrently and both read
  it while `RevokedAt` is still null; consumption is a conditional update
  (`ExecuteUpdateAsync` with `Where(t => t.Id == id && t.RevokedAt == null)`), so the database — not
  the earlier read — picks exactly one winner. `0` rows affected means the caller lost the race; throw
  the same `InvalidTokenMessage` a losing caller would already get for an unknown or expired token.
  Do not go back to setting `RevokedAt` on the tracked entity and calling `SaveChangesAsync` — that
  reopens the double-read window the advisory is about. Because the conditional consume is its own
  statement, the revoke and the replacement insert no longer share one `SaveChangesAsync`; wrap both
  in an explicit `Database.BeginTransactionAsync`/`CommitAsync` so a failed insert still leaves the
  credential spendable (there is a test for that rollback).
- **Losing a rotation race is never reuse, and must never trigger `RevokeAllActiveAsync`.** Browser
  tabs share one refresh credential through the client's storage while the single-flight guard against
  a duplicate in-tab refresh is a per-tab module variable (see `react-frontend`), so honest clients
  rotate the same token concurrently as a matter of course. Escalating a lost race to family-wide
  revocation would sign a user out of every tab for the ordinary act of refreshing twice at once.
  Sequential replay of an already-spent (retained, `RevokedAt` already set) credential is unchanged and
  still revokes the whole family — only the race-losing branch is exempt.
- `RevokeAllActiveAsync` sweeps in a loop (`MaxRevocationPasses`), not once: a rotation that commits
  while revocation sits between reading the user's active tokens and writing them mints a successor the
  first sweep never saw, and nothing would revisit it under a single-pass sweep. Each pass is its own
  `ExecuteUpdateAsync` over the canonical active predicate and stops as soon as a pass revokes zero
  rows. Reaching the pass cap without converging logs a warning rather than throwing — callers run this
  as cleanup on a path already about to reject the request, and a thrown exception there would change
  what the caller reports back.
- Successful password changes, password resets, and email changes revoke the account's active
  refresh tokens through the store. Password change issues no replacement token pair; existing access
  tokens retain their normal expiry, including on the requesting device.
- `PurgeExpiredAsync(retention)` deletes only rows with `ExpiresAt < now - retention`; the exact boundary remains.
- `POST /api/auth/refresh` and `POST /api/auth/logout` do **not** have `[Authorize]` — the refresh token is its own credential and these endpoints must work after the JWT expires.

## Configuration

`AZURE_KEY_VAULT_URL` triggers Key Vault config via `DefaultAzureCredential` (the hyphen→underscore secret mapping is noted in AGENTS.md; the full secret map is the `azure-infrastructure` agent's). Backend-relevant invariant: `DATABASE_CONNECTION_STRING` and `JWT_SIGNING_KEY` are required — missing either throws on startup (`JWT_SIGNING_KEY`'s check lives in `JwtSettings.FromConfiguration`, called once in `Program.cs`).

## Forwarded headers — never call `app.UseForwardedHeaders()`

`ForwardedHeadersPolicy.Configure` (`src/ApexRacers.Api/Services/ForwardedHeadersPolicy.cs`) is the
single place the forwarded-header trust decision is expressed — which headers, `ForwardLimit`, and
that `KnownNetworks`/`KnownProxies` are cleared (GHSA-fq5w-frqr-6px2). `Program.cs` only binds it via
`Configure<ForwardedHeadersOptions>`; it never calls `app.UseForwardedHeaders()`. Do not add that
call. The host (App Service) registers the middleware itself, gated on the
`ASPNETCORE_FORWARDEDHEADERS_ENABLED` app setting — adding the call here as well would register the
middleware **twice**, consuming two `X-Forwarded-For` entries instead of one and resolving a caller's
own forged left-hand entry as if it were the real client, which is the exact spoofing hole the
advisory is about. Registering it unconditionally (bypassing the app setting) is equally wrong in the
other direction: it would process forwarded headers in environments with no front end (local Compose,
CI) and, with `KnownProxies` cleared, hand any caller the address the rate limiter partitions on.
`ForwardedHeadersPolicy.IsEnabledByHost` only logs a startup warning when the setting is absent — it
never changes what gets registered.

## Tests

xUnit in `src/ApexRacers.Tests/`. **Test services directly** — never spin up the HTTP pipeline or test controllers; each test creates its own `AppDbContext` and shares no state. The project guide covers the rest: the native Microsoft Testing Platform v2 test/filter/coverage commands and supported IDEs, the SQLite/PostgreSQL provider contract and Docker prerequisite, the order/project-by-entity-columns-before-DTO rule, and the **85% line + branch** coverage gate. Add tests alongside new service logic before calling it done.

Use `DbContextFactory.Create()` for the ordinary fast service test. Move a class into
`PostgreSqlCollection` and inject `PostgreSqlFixture` when the behavior depends on Npgsql-only
translation (including `DateTimeOffset` comparisons/order), PostgreSQL transactions, or production
constraints such as a unique index, user foreign key, or cascade. The fixture shares one pinned
container but creates a unique database per test; `EnsureCreated` validates the current model rather
than migration application. Auth/JWT tests that issue refresh tokens use this path because the token
must persist against its real user relationship. Keep fixtures provider-honest instead of replacing a
production-valid query with a non-relational stand-in.

The ingestion `Worker` is a coverage-excluded I/O shell. Put SDK field mapping in the covered
`CatalogIngest`, `WeatherIngest`, `TrackStateIngest`, or `SubsessionMapper` seams and relational
season/schedule upsert rules in `SeasonIngest`. Mapper tests assert every persisted field, fallback,
and pinned JSON name; `SeasonIngest` tests use SQLite to prove insert/update parity and the save ordering
needed before dependent rows are added. Keep the worker at fetch, coordination, and logging.

**Guard against a non-nullable SDK field standing in for an omitted one.** The Aydsko SDK types some
wire fields as non-nullable even though iRacing's payload can omit them — `SeasonScheduleItem.WeekEndTime`
(`DateTimeOffset`, not `DateTimeOffset?`) is the concrete case: an omitted `week_end_time` deserializes
to `default` (`0001-01-01`), a *successful* parse carrying a value that isn't a date, rather than
throwing or leaving something checkable as absent. Persisting that verbatim would make the row lie
forever, not just on the one bad fetch — this is the same silent-zeros trap the persisted-JSON rules in
AGENTS.md warn about, just triggered by the SDK's nullability choice instead of a raw JSON gap. The
fix is a small pure guard at the mapping seam — `SeasonIngest.EndTimeOrNull` rejects anything not after
the row's own start date — rather than trusting the SDK's type to mean "always present." Test the guard
with all three cases: a genuine value, the `default` zero-value, and a value that parses but fails the
sanity check (before the start date).

A single `AppDbContext` serializes everything through one change tracker, so it cannot express two
callers racing the same row. For that case only, `DbContextFactory.CreateShared()` returns a
`SharedSqliteDatabase` holding one in-memory SQLite connection that hands out multiple independent
`AppDbContext` instances (`NewContext()`) over it — the connection, not any one context, owns the
database's lifetime, since in-memory SQLite drops it when its last connection closes.
