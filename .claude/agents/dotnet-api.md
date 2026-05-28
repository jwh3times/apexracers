---
name: dotnet-api
description: Use for any work in ApexRacers.Api, ApexRacers.Core, ApexRacers.Data, ApexRacers.Ingestion, or ApexRacers.Tests — controllers, services, EF Core models, entity configurations, DTOs, migrations, and xUnit tests.
tools: Read, Write, Edit, Bash, Glob, Grep
model: sonnet
---

You are working inside the ApexRacers .NET 10 backend. Know these patterns cold and enforce them without deviation.

## Project layout

```
ApexRacers.Core        ← models only, no dependencies
ApexRacers.Data        ← AppDbContext, entity configs, ApplicationUser, migrations
ApexRacers.Api         ← controllers, services, DTOs, Program.cs
ApexRacers.Ingestion   ← BackgroundService worker (iRacing data fetch)
ApexRacers.Tests       ← xUnit tests for services only
```

Core and Data are shared; Api and Ingestion never reference each other.

## C# style

- .NET 10, C# 13. Use primary constructors everywhere — no `private readonly` field boilerplate.
- File-scoped namespaces, top-level statements in Program.cs.
- `record` types for all DTOs. Positional records for simple shapes.
- Pattern matching and null-coalescing over verbose null checks.

## Package management

All package versions live in `Directory.Packages.props` at the repo root. **Never add `Version=""` to a `<PackageReference>` in any `.csproj`.** Use `dotnet add package` — it writes the version to `Directory.Packages.props` automatically. `CentralPackageTransitivePinningEnabled=true` is intentional; do not remove it.

## Controllers

Controllers in `src/ApexRacers.Api/Controllers/` contain **no logic**. Their only job is:
1. Bind HTTP inputs (route params, query string, body, `[FromBody]`, `CancellationToken`).
2. Call the injected service.
3. Return `Ok(result)`, `NotFound()`, `BadRequest(ex.Message)`, or `Unauthorized()`.

Extract user identity from `User.FindFirstValue(JwtRegisteredClaimNames.Sub)` when needed; parse the Guid before passing to the service. Do not put EF Core queries, business rules, or multi-step logic in controllers.

## Services

Services in `src/ApexRacers.Api/Services/` hold all business logic. Rules:

- Inject `AppDbContext` directly. No `IRepository<T>`, no MediatR, no command/query handlers.
- One focused responsibility per class (e.g., `PercentileCalculationService`, `CarRecommendationService`).
- `async Task<T>` with `CancellationToken ct = default` as the last parameter on all public async methods.
- Throw `InvalidOperationException` for business rule violations; controllers catch and return `BadRequest(ex.Message)`.
- Return `null` (or `null`-returning nullable) when a resource is not found; controllers map that to `NotFound()`.

## DTOs

- **Response shapes**: `record` types in `src/ApexRacers.Api/Dtos/ResponseDtos.cs`.
- **Request shapes**: `record` types in `src/ApexRacers.Api/Dtos/RequestDtos.cs`.
- When adding or changing a response DTO, also update the matching TypeScript interface in `src/web/src/services/api.ts`. They must stay in sync.

## AppDbContext and schemas

`AppDbContext` extends `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`.

- Default schema: `iracing` (set via `HasDefaultSchema("iracing")`).
- All ASP.NET Identity tables use `identity` schema with explicit `ToTable(..., "identity")` overrides — this must happen after `base.OnModelCreating(modelBuilder)`.
- Entity configurations implement `IEntityTypeConfiguration<T>` in `src/ApexRacers.Data/EntityConfigurations/`, registered via `ApplyConfigurationsFromAssembly`.
- Use Fluent API in configurations; never use data annotations on model classes.
- Specify `OnDelete` behavior explicitly on every `HasOne(...).WithMany(...)` — use `Restrict` for cars, `Cascade` for weeks/seasons.

## Migrations

```bash
dotnet ef migrations add <MigrationName> --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
dotnet ef database update --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
```

`dotnet-ef` version must match EF Core (currently 10.0.7). Migrations run automatically at API startup via `db.Database.MigrateAsync()` — no separate deployment step. Safe because the API is single-instance on App Service.

`DesignTimeDbContextFactory` reads `DATABASE_CONNECTION_STRING` or falls back to the hardcoded local dev string — no env var needed for local `dotnet ef` commands.

## Auth and RBAC

- JWT HS256, 30-day expiry, `ClockSkew = TimeSpan.Zero`, `MapInboundClaims = false`.
- Claims in token: `sub` (Guid user ID), `email`, `name`, `role`, `iracing_id` (optional).
- Roles: `Standard` (default on register), `Beta`, `Alpha`, `Admin`.
- RBAC policies use `RequireClaim("role", ...)`, **not** `RequireRole`. Existing policies:
  - `AdminOnly` → `RequireClaim("role", "Admin")`
  - `AlphaOrAbove` → `RequireClaim("role", "Alpha", "Admin")`
  - `BetaOrAbove` → `RequireClaim("role", "Beta", "Alpha", "Admin")`
- Self-service role changes (Standard/Beta/Alpha) via `PUT /api/auth/role`; Admin cannot self-demote.
- Admin promotion only via `ADMIN_SEED_EMAILS` at startup or `AdminController`.

## Configuration and Key Vault

- `AZURE_KEY_VAULT_URL` env var triggers Azure Key Vault configuration via `DefaultAzureCredential`.
- Key Vault secret names use hyphens (`JWT-SIGNING-KEY`, `DATABASE-CONNECTION-STRING`, `ADMIN-SEED-EMAILS`).
- `HyphenToUnderscoreSecretManager` maps them to the underscore keys the app expects (`JWT_SIGNING_KEY`, etc.).
- Required env vars: `DATABASE_CONNECTION_STRING`, `JWT_SIGNING_KEY`. Missing either throws on startup.

## Ingestion worker

`ApexRacers.Ingestion` is a `BackgroundService`. It uses `Aydsko.iRacingData` with `UsePasswordLimitedOAuth()` (four env vars: `IRACING_USERNAME`, `IRACING_PASSWORD`, `IRACING_CLIENT_ID`, `IRACING_CLIENT_SECRET`). Resolve `IDataClient` and `AppDbContext` through `IServiceScopeFactory` per ingestion cycle — never inject scoped services directly into the singleton worker.

## Tests

- xUnit in `src/ApexRacers.Tests/`.
- Test services directly — never spin up the HTTP pipeline or test controllers.
- `DbContextFactory.Create()` creates an in-memory `AppDbContext` with a unique database name per test.
- Each test method creates its own `AppDbContext` instance; never share state between tests.
- Coverage target: **>80% line coverage** for services and `Core` helpers. Controllers are excluded (no logic to cover).
- Run coverage: `dotnet-coverage collect "dotnet test" -f xml -o coverage.xml`, then `reportgenerator`.
- When adding service logic, add corresponding tests before calling the work done.
