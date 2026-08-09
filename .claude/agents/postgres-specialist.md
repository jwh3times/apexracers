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
| `Subsessions`          | int PK      | iRacing race session (SeasonId, WeekNumber, WeekId, TrackId, OfficialSession, EventStrengthOfField, StartTime, SplitNum); `WeatherJson` is a serialized `WeatherSnapshot`; `TrackStateJson` is still a raw Aydsko `TrackState` (no reader today — see note below) |
| `SubsessionResults`    | composite   | One driver result per subsession (SubsessionId, CustId, CarId, CarClassId, BestLapSeconds, FinishPosition, Incidents, …)                                     |
| `SeasonCarBops`        | composite   | Per-week BoP for one car (SeasonId, WeekNumber, CarId); `CarId` has no FK (BoP ingestion order never blocks on catalog)                                      |
| `CarPercentileResults` | composite   | Cache — one row per (UserId, CarId, SeriesId, WeekId); upserted on each compute                                                                              |
| `PersonalLaps`         | Guid PK     | User's personal best per (UserId, CarId, TrackId); includes `SessionType`, `TrackTempCelsius`, `TrackWetness`                                                |
| `FeatureFlags`         | int PK      | Unique index on `Key`                                                                                                                                        |
| `ExternalDataCaches`   | int PK      | Backs `CachedIRacingClient` get-or-fetch; unique index on `CacheKey` (max length 200); `Payload` is the serialized DTO JSON, `ExpiresAt` drives TTL eviction |
| `Rivals`               | Guid PK     | A driver a user follows; unique index on (UserId, RivalCustId) for idempotent add; cascade FK → `identity.Users`                                             |

**`identity` schema** — all ASP.NET Identity tables plus refresh tokens:

`Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `UserTokens`, `RoleClaims`, `RefreshTokens`

`ApplicationUser` extends `IdentityUser<Guid>` with `DisplayName string`, `IRacingCustomerId long?`, and `ThemePreference string`.

`RefreshTokens`: `Id` (Guid PK), `UserId` (Guid FK → Users, cascade delete), `TokenHash` (unique index — SHA-256 hex of the raw token, raw token is never stored), `ExpiresAt`, `CreatedAt`, `RevokedAt?`. A token is active when `RevokedAt` is null and `ExpiresAt > UtcNow`.

## Persisted JSON columns hold owned Core types, not SDK types

`Weeks.WeatherSummaryJson` and `Subsessions.WeatherJson` serialize `ApexRacers.Core.Models.WeatherForecastSnapshot` / `WeatherSnapshot`, mapped from the Aydsko SDK at ingest by `ApexRacers.Ingestion.WeatherIngest` — never the raw SDK `WeatherSummary`/`ResultsWeather` types. **Their `[JsonPropertyName]` values are pinned to the SDK's existing snake_case wire names on purpose** (`temp_value`, `wind_units`, `skies_high`, …) so every row already on disk still deserializes; do not rename them — that's a persisted-contract change, not a refactor, and it would silently zero out every historical row rather than throw. This is the pattern for any future persisted JSON column: define an owned Core record and a pure, tested mapper (mirror `WeatherIngest`/`CatalogIngest`), never serialize an SDK type straight into a column that outlives a single request.

`Subsessions.TrackStateJson` does **not** yet follow this pattern — it's still `JsonSerializer.Serialize(data.TrackState)`, a raw Aydsko `TrackState`, because nothing reads it today. There's no live bug (no reader means no silent-zeros failure mode yet), but the trap is armed: give it a `TrackStateSnapshot` + mapper the moment a reader is added, rather than deserializing the SDK type back out of a column that already has years of rows on disk by then.

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
- **Personal-best projection** (`PersonalBestQuery`): groups `PersonalLap` rows by a key that spans navigation properties (`Car.Name`, `Track.Name`, `Track.ConfigName`) alongside the `Min`/`Count`/`Max` aggregates — a shape neither Npgsql nor SQLite translates. It runs `.ToListAsync()` first and groups in memory rather than pushing the `GroupBy` into SQL; the same untranslatable-shape family as the order/project-by-entity-columns-before-DTO rule in AGENTS.md's Testing section (positional-record DTO properties don't translate as `ORDER BY`/`SELECT` targets either). Follow this pattern for any new query that groups or orders by a navigation-property-derived key.
- **Active-season / active-week resolution** (`SeasonQueries`, `src/ApexRacers.Api/Services/SeasonQueries.cs`): `IQueryable<Season>.ActiveForSeries` / `IQueryable<Week>.InActiveSeason` centralize the `Where(Active).OrderByDescending(Year).ThenByDescending(Quarter)` predicate that `ScheduleService`, `StrategyService`, `StandingsService`, `PercentileCalculationService`, `CarRecommendationService`, and `WeekCarStatsService` all query through rather than re-deriving. `SeriesService.GetActiveSeriesAsync` is the pattern to follow for a similar "resolve once, then query by id" reshape: it used to run six correlated per-row subqueries (current week, track name, track config, distinct car count, distinct driver count — twice, once for cars once for drivers) inside a single `Seasons.Select(...)`. It's now three queries — seasons, then all their weeks, then `SubsessionResults` filtered to just the resolved current-week ids — with "which week is current" resolved once in memory via `Core.SeasonCalendar.CurrentWeekNumber` and counts grouped in memory afterward. Prefer this shape (bulk-fetch by a resolved id set, aggregate in memory) over a `Select` whose subqueries would otherwise repeat per row.

Avoid N+1 queries. Use `.Include()` for navigation properties loaded eagerly, or project to DTOs with `.Select()` when only a subset of fields is needed.

## Seed data

The seeder command (idempotent — safe to re-run), the `--demo` cache seed, and the SQL scripts in `src/ApexRacers.Data/Seeds/` (`truncate_seed_data.sql`, `purge_demo_data.sql`) with their `Get-Content … | docker compose exec -T postgres psql` invocation are all in AGENTS.md (Commands). It seeds synthetic lap data for all 7 series.
