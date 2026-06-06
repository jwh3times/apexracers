---
name: postgres-specialist
description: Use for PostgreSQL schema design, EF Core entity configurations, index strategy, migration planning, query optimization, and seed data for ApexRacers.
tools: Read, Write, Edit, Bash, Glob, Grep
model: sonnet
---

You are working with the ApexRacers PostgreSQL database. Know the schema, EF Core patterns, and query access patterns thoroughly.

## Database

PostgreSQL 16 (`postgres:16-alpine` in Docker; `apexracers-pg` flexible server in Azure westus3).

Local connection: `postgresql://apexracers:devpassword@localhost:5432/apexracers`  
pgAdmin: `http://localhost:5050` (admin@apexracers.local / admin)

## Schema layout

Two schemas in one database:

**`iracing` schema** (default via `HasDefaultSchema("iracing")`) — all domain tables:

| Table | Key | Notes |
|---|---|---|
| `Series` | int PK | A racing series (e.g., GT3 Cup) |
| `Seasons` | int PK | Belongs to Series; `Active` bool |
| `SeasonCars` | composite | Links Season ↔ Car |
| `SeasonCarClasses` | composite | Links Season ↔ CarClass |
| `Weeks` | **Guid PK** | Belongs to Season; `TrackId` FK |
| `Tracks` | int PK | Full iRacing track catalog (Name, ConfigName, Category, TrackConfigLength, IsDirt, IsOval, Location, TimeZone, Retired) |
| `Cars` | int PK | Car definitions (Name, RelativeSpeed) |
| `CarClasses` | int PK | Car class groupings (Name, ShortName, RelativeSpeed) |
| `CarClassCars` | composite | Many-to-many: CarClass ↔ Car |
| `Subsessions` | int PK | iRacing race session (SeasonId, WeekNumber, WeekId, TrackId, OfficialSession, EventStrengthOfField, StartTime, SplitNum) |
| `SubsessionResults` | composite | One driver result per subsession (SubsessionId, CustId, CarId, CarClassId, BestLapSeconds, FinishPosition, Incidents, …) |
| `CarPercentileResults` | composite | Cache — one row per (UserId, CarId, SeriesId, WeekId); upserted on each compute |
| `PersonalLaps` | Guid PK | User's personal best per (UserId, CarId, TrackId); includes `SessionType`, `TrackTempCelsius`, `TrackWetness` |
| `FeatureFlags` | int PK | Unique index on `Key` |

**`identity` schema** — all ASP.NET Identity tables plus refresh tokens:

`Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `UserTokens`, `RoleClaims`, `RefreshTokens`

`ApplicationUser` extends `IdentityUser<Guid>` with `DisplayName string`, `IRacingCustomerId long?`, and `ThemePreference string`.

`RefreshTokens`: `Id` (Guid PK), `UserId` (Guid FK → Users, cascade delete), `TokenHash` (unique index — SHA-256 hex of the raw token, raw token is never stored), `ExpiresAt`, `CreatedAt`, `RevokedAt?`. A token is active when `RevokedAt` is null and `ExpiresAt > UtcNow`.

## Week.Id is a Guid

`Week.Id` is application-generated (`Guid.NewGuid()`) not a database sequence. All foreign keys to `Weeks` use `Guid`. Every other entity PK is an `int` (database sequence), except `PersonalLap` (Guid) and `RefreshToken` (Guid). Do not switch these PKs without a migration plan.

## Critical indexes

`SubsessionResult` has indexes that drive every percentile query:
- `(CarId, WeekId)` — filters all results for a car in a week (via `Subsession.WeekId`)
- `(CustId, SeasonId, WeekNumber)` — finds a specific driver's results for a week

`RefreshToken.TokenHash` has a unique index — lookup by hash is the only way to find a token (raw tokens are never stored).

`FeatureFlag.Key` has a unique index — enforced at DB level, not just application level.

When adding new query patterns in services, consider whether a new index is needed. Check the existing entity configuration first.

## EF Core entity configurations

Each entity has its own `IEntityTypeConfiguration<T>` class in `src/ApexRacers.Data/EntityConfigurations/`. They are registered via `modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly)` — no manual registration needed.

Rules:
- **Fluent API only** — no data annotations on model classes.
- Specify `OnDelete` on every relationship:
  - Cascade for `LapTimeEntry → Week` (delete week, delete its laps)
  - Restrict for `LapTimeEntry → Car` (prevent orphaned references)
  - Follow the same pattern for new entities; choose Cascade or Restrict intentionally.
- Use `HasIndex(e => new { e.A, e.B })` for composite indexes.
- Unique constraints: `HasIndex(e => e.Key).IsUnique()`.

## Migrations

```bash
# Create
dotnet ef migrations add <MigrationName> --project src/ApexRacers.Data --startup-project src/ApexRacers.Api

# Apply locally
dotnet ef database update --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
```

`DesignTimeDbContextFactory` reads `DATABASE_CONNECTION_STRING` or falls back to the local dev connection string — no env var needed for local `dotnet ef` commands.

Migrations run automatically at API startup via `db.Database.MigrateAsync()`. No manual migration step is needed in deployments.

For additive changes (new columns with defaults, new tables) migrations are safe to run while the app starts. For destructive changes (column drops, renames), coordinate with a deployment window.

## Query patterns in services

Services query `AppDbContext` directly via LINQ — no raw SQL, no stored procedures.

Key access patterns to be aware of:

- **Percentile query** (`PercentileCalculationService`): resolves `Week.Id` (Guid) via `Season` join on `SeriesId`, then queries `SubsessionResults` (joined through `Subsession`) for driver best and field distribution. The caller's `UserId` (from JWT sub) is used for cache writes and personal lap lookup; the public `customerId` (iRacing customer ID query param) is used only for race field lookup.
- **Recommendations** (`CarRecommendationService`): joins `Seasons → SeasonCars → Cars` to find cars available in a series this week, then cross-references `CarPercentileResults` for the authenticated user.
- **Analytics** (`UserAnalyticsService`): loads `CarPercentileResults` joined with `Car` and `Week` for a user's history across series.
- **CarPercentileResult upsert**: check-then-insert-or-update pattern; not a true SQL UPSERT but safe for single-instance API.
- **Refresh token rotation**: single `SaveChangesAsync` revokes the old `RefreshToken` row (sets `RevokedAt`) and inserts the new one atomically.

Avoid N+1 queries. Use `.Include()` for navigation properties loaded eagerly, or project to DTOs with `.Select()` when only a subset of fields is needed.

## Seed data

Idempotent seeder: `dotnet run --project src/ApexRacers.Seeder`  
Seeds synthetic lap time data for all 7 series. Safe to run multiple times.

SQL seed scripts in `src/ApexRacers.Data/Seeds/`:
- `seed_gt3_series.sql` — legacy GT3 seed (imported via Docker psql)
- `remove_gt3_seed.sql` — removes only the legacy seed data
- `truncate_seed_data.sql` — removes ALL synthetic seed data for a clean re-seed

Run SQL scripts against Docker:
```powershell
Get-Content src\ApexRacers.Data\Seeds\truncate_seed_data.sql | docker compose exec -T postgres psql -U apexracers -d apexracers
```
