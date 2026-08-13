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

AGENTS.md covers the no-logic rule. The details it doesn't: extract user identity from `User.FindFirstValue(JwtRegisteredClaimNames.Sub)` and parse the `Guid` before passing it to the service. For error cases **don't catch to `BadRequest(ex.Message)`** — let the service `throw` and `ExceptionHandlingMiddleware` map it (status map in AGENTS.md). Return an explicit result only for non-exception outcomes that need a specific code (e.g. AuthController's 423 lockout, a `404`/`501`).

## Services — the .NET specifics

AGENTS.md covers the service-layer rules (all logic here; inject `AppDbContext` directly; no MediatR / `IRepository<T>`; one responsibility per class). Conventions it doesn't:

- `async Task<T>` with `CancellationToken ct = default` as the last parameter on every public async method.
- Drive error flow by throwing — `ArgumentException` / `InvalidOperationException` → 400, `KeyNotFoundException` → 404, `UnauthorizedAccessException` → 401, `IRacingNotConfiguredException` → 503 (see `ExceptionStatusMapper`). Don't catch these back to `BadRequest`/`NotFound` in the controller.
- Don't swallow `OperationCanceledException` to "clean up" cancellation noise. Let it propagate: `ClientDisconnectDetector` already distinguishes a client disconnect (token signalled → Debug + 499) from a real server-side cancellation (→ Error + 500), and catching it in a service destroys the distinction.
- When a controller returns a bare status result the **user will see**, give it a message: `Problem(detail: "…", statusCode: …)`, not `Unauthorized()`/`NotFound()`. ASP.NET Core's automatic ProblemDetails carries only type/title/status/traceId, and the web client renders `detail` — a bare result leaves it with nothing human-readable. Keep the wording non-enumerating for auth failures ("Invalid email or password.", never "no such account"). Internal guards that can't reach a user (e.g. an unparseable `sub` claim) can stay bare.
- Percentile rank and field median are never re-derived inline: call `ApexRacers.Core.FieldPercentile.Rank`/`MedianOfSorted`. `Rank(driverBest, otherDriversLaps)` takes the field **excluding** the ranked driver — build that list with `.Where(d => d.CustId != customerId)` before calling it, not the raw field. Getting this backwards silently inflates the rank by one driver whenever that driver's own race lap is slower than the value being ranked (e.g. a personal lap superseding it). This is distinct from `CarRecommendationService`'s `RunningAveragePercentile` — that's a running average of successive percentile *readings*, unrelated to how a single reading is computed.
- Uploaded Best rows are never projected inline either: call `PersonalBestQuery.RunAsync(scope, order, ct)` (`src/ApexRacers.Api/Services/PersonalBestQuery.cs`). It ranges over Uploaded Laps only, so what it returns is an Uploaded Best, not a Personal Best — a Personal Best also weighs the Subject Driver's Race Best. Two invariants it owns, both easy to reintroduce by hand if a caller writes its own version:
  - **The caller's `scope` must not filter on `IsValidLap`.** `PersonalBestQuery` applies that filter itself, so no caller can forget it and quietly report an invalidated lap as a best.
  - **The `GroupBy` must not be pushed into SQL.** The group key spans navigation properties (`Car.Name`, `Track.Name`, `Track.ConfigName`) alongside the aggregates, which neither Npgsql nor SQLite translates, so the query materializes to a list first and groups in memory — deliberately, not an oversight. Getting this wrong throws at runtime rather than failing to compile; see the order/project-by-entity-columns-before-DTO rule in AGENTS.md's Testing section, which is the same underlying translation gap.
- Resolving a series' current season or one numbered week of it is never a hand-written
  `Where(... && s.Active).OrderByDescending(Year).ThenByDescending(Quarter)` — that ordering picks
  whichever season iRacing flagged active most recently, which during a changeover is the *incoming*
  season before it has raced a single week, not the one drivers are actually racing. Call the
  `SeasonQueries` extensions (`src/ApexRacers.Api/Services/SeasonQueries.cs`) instead:
  `AppDbContext.CurrentSeasonIdAsync(seriesId, ct, today?)` / `CurrentSeasonIdsAsync(seriesIds, ct,
  today?)` (batched — one query per set of series, not per series), `IQueryable<Week>.InSeason(seasonId,
  weekNumber)`, `AppDbContext.CurrentSeasonOrThrowAsync` (the one
  `KeyNotFoundException("No current season for series {id}.")` wording), `AppDbContext.SeriesNameAsync`.
  The `today` parameter exists only so tests can sit exactly on a changeover boundary — production
  callers never pass it. The selection rule itself — the season whose first race week began most
  recently, `Active` deliberately not a filter — is
  `ApexRacers.Core.SeasonCalendar.CurrentSeasonId(IEnumerable<SeasonStart>, DateOnly today)`; don't
  reimplement it inline, and don't reintroduce a `Year`/`Quarter`-only ordering as a shortcut — that's
  exactly the bug this replaced. `InSeason` composes on an already-resolved season id (a `CurrentSeasonId*`
  call is a round trip, so callers await it once before composing the week projection they need).
- "Which week is the season currently in" is never re-derived per caller either — call
  `ApexRacers.Core.SeasonCalendar.CurrentWeekNumber(weeks, today)`: latest week whose start date is
  on/before `today`, week number only breaking a tie. It returns `null` before the season starts
  **by design** — the pre-season fallback (blank cell vs. first week) is a per-caller UI choice, not
  something this method should decide for every caller.

### `CachedIRacingClient` — the get-or-fetch seam

`CachedIRacingClient(AppDbContext db, IDataClient? client)` — `GetOrFetchAsync<T>(CacheSpec spec, Func<IDataClient, Task<T>> fetch, CancellationToken ct)`. `CacheSpec` (`Key` + `Ttl`) always comes from a factory on `IRacingCacheKeys` (`src/ApexRacers.Api/Services/IRacingCacheKeys.cs`) — that module is the sole author of every key string and its TTL; adding a cache-backed read path means adding a factory there, never interpolating a key at the call site. `client` is nullable rather than resolved from an `IServiceProvider`: it's registered in `Program.cs` via an explicit factory lambda (`sp.GetService<IDataClient>()`) because the SDK client itself is only registered when all four `IRACING_*` credentials are present; a null `client` on a cache miss throws `IRacingNotConfiguredException`. There is no `IsConfigured` property — check for a 503 by attempting the call, not by probing state first.

## DTOs

`record` types — response shapes in `Dtos/ResponseDtos.cs`, request shapes in `Dtos/RequestDtos.cs`. (AGENTS.md notes the `ResponseDtos.cs` ↔ `web/src/services/api.ts` sync requirement — honor it when you change a response DTO.)

## AppDbContext and schemas

`AppDbContext` extends `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`. Default schema is `iracing` (`HasDefaultSchema("iracing")`); ASP.NET Identity tables are moved to the `identity` schema with explicit `ToTable(..., "identity")` overrides that must run **after** `base.OnModelCreating(modelBuilder)` — ordering matters.

The entity-config mechanics (one `IEntityTypeConfiguration<T>` per entity in `src/ApexRacers.Data/EntityConfigurations/`, Fluent-API-only, explicit `OnDelete`) plus the full schema, PKs, and indexes are the `postgres-specialist` agent's lens.

## Migrations

The `dotnet ef` commands and the `dotnet-ef`/EF version-match note are in AGENTS.md (Commands). Beyond those: migrations **auto-apply at API startup** via `db.Database.MigrateAsync()`, so write them to apply cleanly on boot (the prod deploy story is the `azure-infrastructure` agent's). `DesignTimeDbContextFactory` reads `DATABASE_CONNECTION_STRING` or falls back to a hardcoded local dev string, so `dotnet ef` needs no env var locally.

## Auth and RBAC

- JWT HS256, **15-minute access token expiry**, `ClockSkew = TimeSpan.Zero`, `MapInboundClaims = false`.
- Claims in token: `sub` (Guid user ID), `email`, `name`, `role`, `iracing_id` (optional), `theme_preference`.
- The token contract (signing key, issuer, audience) is bound exactly once, as `JwtSettings.FromConfiguration(config)` in `Program.cs`, and shared as a singleton by both sides that need it: `Program.cs`'s `TokenValidationParameters` (validating) and `AuthService.GenerateJwtAsync` (issuing, via a constructor-injected `JwtSettings jwt`). **Never read `JWT_SIGNING_KEY`/`JWT_ISSUER`/`JWT_AUDIENCE` from `IConfiguration` directly outside `JwtSettings`** — the two sides must derive the identical `SymmetricSecurityKey`/issuer/audience, and a mismatch (e.g. one side keeping a stale default while the other changes) is a total-auth outage with no compile error and, if the test suite constructs its own `JwtSettings` instead of binding through `FromConfiguration`, no failing test either. `AuthService`'s constructor is `(UserManager<ApplicationUser> userManager, IConfiguration config, JwtSettings jwt, RefreshTokenStore refreshTokens, IEmailSender emailSender)` — `config` remains only for `APP_BASE_URL` (email links), not JWT settings. Any new `AuthService(...)` construction site (tests included) needs the `JwtSettings` and store arguments; bind the former with `JwtSettings.FromConfiguration(config)` from the same `IConfiguration`, and construct the latter with the test `AppDbContext` plus a controlled `TimeProvider` when time matters.
- Roles: `Standard` (default on register), `Beta`, `Alpha`, `Admin`.
- RBAC policies use `RequireClaim("role", ...)`, **not** `RequireRole`. Existing policies:
  - `AdminOnly` → `RequireClaim("role", "Admin")`
  - `AlphaOrAbove` → `RequireClaim("role", "Alpha", "Admin")`
  - `BetaOrAbove` → `RequireClaim("role", "Beta", "Alpha", "Admin")`
- Self-service role changes (Standard/Beta/Alpha) via `PUT /api/auth/role`; Admin cannot self-demote.
- Admin promotion only via `ADMIN_SEED_EMAILS` at startup or `AdminController`.

### Refresh token rotation

`AuthService` delegates the complete refresh-token lifecycle to `RefreshTokenStore`, which issues a **7-day rotating refresh token** alongside every JWT. Rules:

- Raw token: 64 random bytes (via `RandomNumberGenerator.Fill`) encoded as Base64.
- Stored in DB as SHA-256 hash (`RefreshToken` entity in `identity.RefreshTokens`). The raw token is never persisted.
- Active means exactly `RevokedAt == null && ExpiresAt > timeProvider.GetUtcNow()`; a token expiring exactly now is inactive. Keep every active-token query behind the store's canonical predicate rather than adding a time-reading property to the entity.
- `RotateAsync(rawToken)`: validates the hash against that predicate, revokes the old token, inserts a replacement, and returns its user ID + raw credential in one `SaveChangesAsync`. Rotation is cap-exempt because it replaces one active credential with one.
- `RevokeAsync(rawToken)`: best-effort; unknown and already-revoked credentials are no-ops. A specifically presented expired credential may still be stamped revoked.
- Issuance caps active tokens per user at 5 by revoking the oldest before adding the new token; `RevokeAllActiveAsync` touches only canonically active rows.
- `PurgeExpiredAsync(retention)` deletes only rows with `ExpiresAt < now - retention`; the exact boundary remains.
- `POST /api/auth/refresh` and `POST /api/auth/logout` do **not** have `[Authorize]` — the refresh token is its own credential and these endpoints must work after the JWT expires.

## Configuration

`AZURE_KEY_VAULT_URL` triggers Key Vault config via `DefaultAzureCredential` (the hyphen→underscore secret mapping is noted in AGENTS.md; the full secret map is the `azure-infrastructure` agent's). Backend-relevant invariant: `DATABASE_CONNECTION_STRING` and `JWT_SIGNING_KEY` are required — missing either throws on startup (`JWT_SIGNING_KEY`'s check lives in `JwtSettings.FromConfiguration`, called once in `Program.cs`).

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

A single `AppDbContext` serializes everything through one change tracker, so it cannot express two
callers racing the same row. For that case only, `DbContextFactory.CreateShared()` returns a
`SharedSqliteDatabase` holding one in-memory SQLite connection that hands out multiple independent
`AppDbContext` instances (`NewContext()`) over it — the connection, not any one context, owns the
database's lifetime, since in-memory SQLite drops it when its last connection closes.
