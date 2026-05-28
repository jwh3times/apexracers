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
| `Weeks` | **Guid PK** | Belongs to Season; has `IracingTrackId` |
| `Cars` | int PK | Car definitions |
| `LapTimeEntries` | int PK | One row per driver lap; `DriverCustomerId` is iRacing ID |
| `CarPercentileResults` | int PK | Cache/upsert — one row per (UserId, CarId, WeekId) |
| `PersonalLaps` | int PK | Best personal lap per (UserId, CarId, TrackName, ConfigName) |
| `FeatureFlags` | int PK | Unique index on `Key` |

**`identity` schema** — all ASP.NET Identity tables:

`Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `UserTokens`, `RoleClaims`

`ApplicationUser` extends `IdentityUser<Guid>` with `DisplayName string` and `IRacingCustomerId long?`.

## Week.Id is a Guid

`Week.Id` is application-generated (`Guid.NewGuid()`) not a database sequence. All foreign keys to `Weeks` use `Guid`. Every other entity PK is an `int` (database sequence). Do not switch Week to int or introduce database-generated GUIDs on other entities without a migration plan.

## Critical indexes

`LapTimeEntry` has two composite indexes — these drive every percentile query:
- `(CarId, WeekId)` — filters all laps for a car in a week
- `(DriverCustomerId, WeekId)` — finds a specific driver's laps

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

- **Percentile query** (`PercentileCalculationService`): resolves `Week.Id` (Guid) by joining through `Season` on `SeriesId`, then queries `LapTimeEntries` twice — once for driver best, once for counts. Relies on both `LapTimeEntry` composite indexes.
- **Recommendations** (`CarRecommendationService`): joins `Seasons → SeasonCars → Cars` to find cars available in a series this week.
- **Analytics** (`UserAnalyticsService`): loads `CarPercentileResults` joined with `Car` and `Week` for a user's history across series.
- **CarPercentileResult upsert**: check-then-insert-or-update pattern; not a true SQL UPSERT but safe for single-instance API.

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
