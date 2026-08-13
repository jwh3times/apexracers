---
name: postgres-specialist
description: Use for PostgreSQL schema design, EF Core entity configurations, index strategy, migration planning, query optimization, and seed data for ApexRacers.
tools: Read, Write, Edit, Bash, Glob, Grep
model: sonnet
---

You are working with the ApexRacers PostgreSQL database. Know the schema, EF Core patterns, and query access patterns thoroughly.

## Database

PostgreSQL 18 (`postgres:18-alpine` in Docker; `apexracers-pg` flexible server in Azure westus3).

Local connection: `postgresql://apexracers:devpassword@localhost:5432/apexracers`  
pgAdmin: `http://localhost:5050` (admin@apexracers.gg / admin)

## Schema layout

Two schemas in one database:

**`iracing` schema** (default via `HasDefaultSchema("iracing")`) — all domain tables:

| Table                  | Key         | Notes                                                                                                                                                        |
| ---------------------- | ----------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Series`               | int PK      | A racing series (e.g., GT3 Cup)                                                                                                                              |
| `Seasons`              | int PK      | Belongs to Series; `Active` bool                                                                                                                             |
| `SeasonCars`           | composite   | Links Season ↔ Car                                                                                                                                          |
| `SeasonCarClasses`     | composite   | Links Season ↔ CarClass                                                                                                                                     |
| `Weeks`                | **Guid PK** | Belongs to Season; `TrackId` FK; `WeatherSummaryJson` is a serialized `WeatherForecastSnapshot`                                                              |
| `Tracks`               | int PK      | Full iRacing track catalog (Name, ConfigName, Category, TrackConfigLength, IsDirt, IsOval, Location, TimeZone, Retired)                                      |
| `Cars`                 | int PK      | Car definitions (Name, RelativeSpeed)                                                                                                                        |
| `CarClasses`           | int PK      | Car class groupings (Name, ShortName, RelativeSpeed)                                                                                                         |
| `CarClassCars`         | composite   | Many-to-many: CarClass ↔ Car                                                                                                                                |
| `Subsessions`          | int PK      | One Split of a Race Session (SeasonId, WeekNumber, WeekId, TrackId, OfficialSession, EventStrengthOfField, StartTime, SplitNum); `WeatherJson` / `TrackStateJson` serialize owned `WeatherSnapshot` / `TrackStateSnapshot` contracts. `SplitNum` is a zero-based Split Index derived at ingest, not an iRacing field; iRacing's session identity is not stored, so Splits of one Race Session cannot be reassociated |
| `SubsessionResults`    | composite   | One Race Result per Driver (SubsessionId, CustId, CarId, CarClassId, BestLapSeconds, FinishPosition, Incidents, …). The composite key assumes a Customer ID, so team entries are not representable                                     |
| `SeasonCarBops`        | composite   | Per-week BoP for one car (SeasonId, WeekNumber, CarId); `CarId` has no FK (BoP ingestion order never blocks on catalog)                                      |
| `CarPercentileResults` | composite   | Cache — one row per (UserId, CarId, SeriesId, WeekId); upserted on each compute                                                                              |
| `PersonalLaps`         | Guid PK     | One Uploaded Lap — every timed lap of a Telemetry Upload, never a per-car-and-track best; includes `SessionType`, `TrackTempCelsius`, `TrackWetness`         |
| `FeatureFlags`         | int PK      | Unique index on `Key`                                                                                                                                        |
| `ExternalDataCaches`   | int PK      | Backs `CachedIRacingClient` get-or-fetch; unique index on `CacheKey` (max length 200); `Payload` is the serialized DTO JSON, `ExpiresAt` drives TTL eviction |
| `Rivals`               | Guid PK     | A driver a user follows; unique index on (UserId, RivalCustId) for idempotent add; cascade FK → `identity.Users`                                             |

**`identity` schema** — all ASP.NET Identity tables plus refresh tokens:

`Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `UserTokens`, `RoleClaims`, `RefreshTokens`

`ApplicationUser` extends `IdentityUser<Guid>` with `DisplayName string`, `IRacingCustomerId long?` (the user's Claimed Identity — see `docs/adr/0001-drivers-referenced-by-customer-id.md`), and `ThemePreference string`.

`RefreshTokens`: `Id` (Guid PK), `UserId` (Guid FK → Users, cascade delete), `TokenHash` (unique index — SHA-256 hex of the raw token, raw token is never stored), `ExpiresAt`, `CreatedAt`, `RevokedAt?`. A token is active when `RevokedAt` is null and `ExpiresAt > UtcNow`.

## Persisted JSON columns hold owned Core types, not SDK types

`Weeks.WeatherSummaryJson`, `Subsessions.WeatherJson`, and `Subsessions.TrackStateJson` serialize
`ApexRacers.Core.Models.WeatherForecastSnapshot`, `WeatherSnapshot`, and `TrackStateSnapshot`, mapped
from the Aydsko SDK at ingest by the pure, tested `WeatherIngest` / `TrackStateIngest` seams — never the
raw SDK `WeatherSummary`, `ResultsWeather`, or `TrackState` types. **Their `[JsonPropertyName]` values
are pinned to the SDK's existing snake_case wire names on purpose** (`temp_value`, `wind_units`,
`leave_marbles`, `race_grip_compound`, …) so every row already on disk still deserializes; renaming one
is a persisted-contract change requiring a data migration, not a refactor. This is the pattern for any
future persisted JSON column: define an owned Core record and a pure, tested mapper (mirror
`WeatherIngest` / `TrackStateIngest` / `CatalogIngest`), then serialize only that owned shape.

## Week.Id is a Guid

`Week.Id` is application-generated (`Guid.NewGuid()`) not a database sequence. All foreign keys to `Weeks` use `Guid`. Every other single-column entity PK is an `int` (database sequence), except `PersonalLap` (Guid), `RefreshToken` (Guid), and `Rival` (Guid). Do not switch these PKs without a migration plan.

## Critical indexes

`SubsessionResult` has indexes that drive every percentile query:

- `(CarId, WeekId)` — filters all results for a car in a week (via `Subsession.WeekId`)
- `(CustId, SeasonId, WeekNumber)` — finds a specific driver's results for a week

`RefreshToken.TokenHash` has a unique index — lookup by hash is the only way to find a token (raw tokens are never stored).

`FeatureFlag.Key` has a unique index — enforced at DB level, not just application level.

`ExternalDataCache.CacheKey` has a unique index (max length 200) — the only lookup path for `CachedIRacingClient`'s get-or-fetch. Two concurrent misses on the same missing key both read null and both attempt an insert; the loser hits this unique index and `SaveChangesAsync` throws `DbUpdateException`, which `GetOrFetchAsync` catches (only when its own read was null, so a genuine update failure still surfaces) and returns the value it fetched rather than re-querying — the winner's row is already correct.

`Rival` has a unique index on `(UserId, RivalCustId)` — makes the follow endpoint idempotent (re-adding an existing rival is a no-op, not a duplicate row).

`ApplicationUser.IRacingCustomerId` has a filtered unique index (`WHERE "IRacingCustomerId" IS NOT NULL`) — enforces at most one User per Customer ID (a Claimed Identity) without constraining the many users who have none.

`SeasonCarBop` has an index on `(SeasonId, WeekNumber)` — backs the per-week BoP lookup from `ScheduleService`/`StrategyService`.

When adding new query patterns in services, consider whether a new index is needed. Check the existing entity configuration first.

## EF Core entity configurations

Each entity has its own `IEntityTypeConfiguration<T>` class in `src/ApexRacers.Data/EntityConfigurations/`. They are registered via `modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly)` — no manual registration needed.

Rules:

- **Fluent API only** — no data annotations on model classes.
- Specify `OnDelete` on every relationship:
  - **Cascade** for ownership (e.g. `Rival → User`, week/season ownership) — deleting the parent removes its children.
  - **Restrict** for catalog references (anything → `Car` / `Track`) — prevent orphaned references.
  - Follow the same pattern for new entities; choose Cascade or Restrict intentionally.
- Use `HasIndex(e => new { e.A, e.B })` for composite indexes.
- Unique constraints: `HasIndex(e => e.Key).IsUnique()`.

## Migrations

The `dotnet ef migrations add` / `database update` commands are in AGENTS.md (Commands); auto-apply-at-startup and the `DesignTimeDbContextFactory` fallback are the `dotnet-api` agent's. The DB-side concern that's yours: for **additive** changes (new columns with defaults, new tables) migrations are safe to run while the app starts; for **destructive** changes (column drops, renames), coordinate a deployment window.

## Query patterns in services

Services query `AppDbContext` directly via LINQ — no raw SQL, no stored procedures.

Key access patterns to be aware of:

- **Percentile query** (`PercentileCalculationService`): resolves `Week.Id` (Guid) via `Season` join on `SeriesId`, then queries `SubsessionResults` (joined through `Subsession`) for driver best and field distribution. The caller's `UserId` (from JWT sub) is used for cache writes and personal lap lookup; the public `customerId` (iRacing customer ID query param) is used only for race field lookup.
- **Recommendations** (`CarRecommendationService`): joins `Seasons → SeasonCars → Cars` to find cars available in a series this week, then cross-references `CarPercentileResults` for the authenticated user.
- **Analytics** (`UserAnalyticsService`): loads `CarPercentileResults` joined with `Car` and `Week` for a user's history across series.
- **CarPercentileResult upsert**: check-then-insert-or-update pattern; not a true SQL UPSERT but safe for single-instance API.
- **Refresh token rotation**: single `SaveChangesAsync` revokes the old `RefreshToken` row (sets `RevokedAt`) and inserts the new one atomically.
- **Uploaded Best projection** (`PersonalBestQuery`): groups `PersonalLap` rows by a key that spans navigation properties (`Car.Name`, `Track.Name`, `Track.ConfigName`) alongside the `Min`/`Count`/`Max` aggregates — a shape neither Npgsql nor SQLite translates. It runs `.ToListAsync()` first and groups in memory rather than pushing the `GroupBy` into SQL; the same untranslatable-shape family as the order/project-by-entity-columns-before-DTO rule in AGENTS.md's Testing section (positional-record DTO properties don't translate as `ORDER BY`/`SELECT` targets either). Follow this pattern for any new query that groups or orders by a navigation-property-derived key.
- **Current-season / current-week resolution** (`SeasonQueries`, `src/ApexRacers.Api/Services/SeasonQueries.cs`): `AppDbContext.CurrentSeasonIdAsync` / `CurrentSeasonIdsAsync` centralize picking the *current* season — the one whose first race week began most recently, per `Core.SeasonCalendar.CurrentSeasonId` — that `ScheduleService`, `StrategyService`, `StandingsService`, `PercentileCalculationService`, `CarRecommendationService`, and `WeekCarStatsService` all query through rather than re-deriving. It is deliberately **not** `Where(Active).OrderByDescending(Year).ThenByDescending(Quarter)`: a series can have two seasons flagged active during a changeover (iRacing marks the incoming one active before it has raced), and ordering by year/quarter alone picks that upcoming, empty season while the quarter actually being raced is still current. `CurrentSeasonIdsAsync` runs two queries for a whole batch of series — season rows (id, year, quarter, active) and a grouped `MIN(Week.StartDate)` per season — then hands both to the pure rule in memory rather than expressing the tiebreak in SQL. `IQueryable<Week>.InSeason(seasonId, weekNumber)` composes on an already-resolved id for the week projection. `SeriesService.GetActiveSeriesAsync` is the pattern to follow for a similar "resolve once, then query by id" reshape: active series ids, then `CurrentSeasonIdsAsync` for the whole batch, then the selected seasons, then their weeks, then `SubsessionResults` filtered to just the resolved current-week ids — with "which week is current" resolved once in memory via `Core.SeasonCalendar.CurrentWeekNumber` and counts grouped in memory afterward. Prefer this shape (bulk-fetch by a resolved id set, aggregate in memory) over a `Select` whose subqueries would otherwise repeat per row.

Avoid N+1 queries. Use `.Include()` for navigation properties loaded eagerly, or project to DTOs with `.Select()` when only a subset of fields is needed.

## Seed data

The seeder command (idempotent — safe to re-run), the `--demo` cache seed, and the SQL scripts in `src/ApexRacers.Data/Seeds/` (`truncate_seed_data.sql`, `purge_demo_data.sql`) with their `Get-Content … | docker compose exec -T postgres psql` invocation are all in AGENTS.md (Commands). It seeds synthetic lap data for all 7 series.
