---
name: dotnet-api
description: Use for any work in ApexRacers.Api, ApexRacers.Core, ApexRacers.Data, ApexRacers.Ingestion, or ApexRacers.Tests — controllers, services, EF Core models, entity configurations, DTOs, migrations, and xUnit tests.
tools: Read, Write, Edit, Bash, Glob, Grep
model: sonnet
---

You are working inside the ApexRacers .NET 10 backend. Know these patterns cold and enforce them without deviation.

The project `CLAUDE.md` you've loaded already covers: the project/dependency graph, central package management (`Directory.Packages.props`), the controller→service→`AppDbContext` request flow, the `ExceptionHandlingMiddleware` error model, the controller/service/Core-model catalog, the ingestion worker, and the persist-vs-cache data-source strategy. **Don't restate those here** — this file adds the .NET-specific depth on top.

## C# style

- .NET 10, C# 13. Use primary constructors everywhere — no `private readonly` field boilerplate.
- File-scoped namespaces, top-level statements in Program.cs.
- `record` types for all DTOs. Positional records for simple shapes.
- Pattern matching and null-coalescing over verbose null checks.

## Controllers — the .NET specifics

CLAUDE.md covers the no-logic rule. The details it doesn't: extract user identity from `User.FindFirstValue(JwtRegisteredClaimNames.Sub)` and parse the `Guid` before passing it to the service. For error cases **don't catch to `BadRequest(ex.Message)`** — let the service `throw` and `ExceptionHandlingMiddleware` map it (status map in CLAUDE.md). Return an explicit result only for non-exception outcomes that need a specific code (e.g. AuthController's 423 lockout, a `404`/`501`).

## Services — the .NET specifics

CLAUDE.md covers the service-layer rules (all logic here; inject `AppDbContext` directly; no MediatR / `IRepository<T>`; one responsibility per class). Conventions it doesn't:

- `async Task<T>` with `CancellationToken ct = default` as the last parameter on every public async method.
- Drive error flow by throwing — `ArgumentException` / `InvalidOperationException` → 400, `KeyNotFoundException` → 404, `UnauthorizedAccessException` → 401, `IRacingNotConfiguredException` → 503 (see `ExceptionStatusMapper`). Don't catch these back to `BadRequest`/`NotFound` in the controller.

## DTOs

`record` types — response shapes in `Dtos/ResponseDtos.cs`, request shapes in `Dtos/RequestDtos.cs`. (CLAUDE.md notes the `ResponseDtos.cs` ↔ `web/src/services/api.ts` sync requirement — honor it when you change a response DTO.)

## AppDbContext and schemas

`AppDbContext` extends `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`. Default schema is `iracing` (`HasDefaultSchema("iracing")`); ASP.NET Identity tables are moved to the `identity` schema with explicit `ToTable(..., "identity")` overrides that must run **after** `base.OnModelCreating(modelBuilder)` — ordering matters.

The entity-config mechanics (one `IEntityTypeConfiguration<T>` per entity in `src/ApexRacers.Data/EntityConfigurations/`, Fluent-API-only, explicit `OnDelete`) plus the full schema, PKs, and indexes are the `postgres-specialist` agent's lens.

## Migrations

The `dotnet ef` commands and the `dotnet-ef`/EF version-match note are in CLAUDE.md (Commands). Beyond those: migrations **auto-apply at API startup** via `db.Database.MigrateAsync()`, so write them to apply cleanly on boot (the prod deploy story is the `azure-infrastructure` agent's). `DesignTimeDbContextFactory` reads `DATABASE_CONNECTION_STRING` or falls back to a hardcoded local dev string, so `dotnet ef` needs no env var locally.

## Auth and RBAC

- JWT HS256, **15-minute access token expiry**, `ClockSkew = TimeSpan.Zero`, `MapInboundClaims = false`.
- Claims in token: `sub` (Guid user ID), `email`, `name`, `role`, `iracing_id` (optional), `theme_preference`.
- Roles: `Standard` (default on register), `Beta`, `Alpha`, `Admin`.
- RBAC policies use `RequireClaim("role", ...)`, **not** `RequireRole`. Existing policies:
  - `AdminOnly` → `RequireClaim("role", "Admin")`
  - `AlphaOrAbove` → `RequireClaim("role", "Alpha", "Admin")`
  - `BetaOrAbove` → `RequireClaim("role", "Beta", "Alpha", "Admin")`
- Self-service role changes (Standard/Beta/Alpha) via `PUT /api/auth/role`; Admin cannot self-demote.
- Admin promotion only via `ADMIN_SEED_EMAILS` at startup or `AdminController`.

### Refresh token rotation

`AuthService` issues a **7-day rotating refresh token** alongside every JWT. Rules:

- Raw token: 64 random bytes (via `RandomNumberGenerator.Fill`) encoded as Base64.
- Stored in DB as SHA-256 hash (`RefreshToken` entity in `identity.RefreshTokens`). The raw token is never persisted.
- `RefreshAsync(rawToken)`: validates hash + `IsActive`, revokes the old token, inserts a new one, and returns a new JWT + new refresh token — all in a single `SaveChangesAsync`.
- `RevokeAsync(rawToken)`: best-effort; no-op if token not found.
- Issuing a refresh token caps active tokens per user at 5 (oldest revoked past the cap; rotation is exempt).
- `POST /api/auth/refresh` and `POST /api/auth/logout` do **not** have `[Authorize]` — the refresh token is its own credential and these endpoints must work after the JWT expires.

## Configuration

`AZURE_KEY_VAULT_URL` triggers Key Vault config via `DefaultAzureCredential` (the hyphen→underscore secret mapping is noted in CLAUDE.md; the full secret map is the `azure-infrastructure` agent's). Backend-relevant invariant: `DATABASE_CONNECTION_STRING` and `JWT_SIGNING_KEY` are required — missing either throws on startup.

## Tests

xUnit in `src/ApexRacers.Tests/`. **Test services directly** — never spin up the HTTP pipeline or test controllers; each test creates its own `AppDbContext` and shares no state. CLAUDE.md covers the rest: the in-memory **SQLite** provider via `DbContextFactory.Create()` (plus the `CreateInMemory()` EF-InMemory exception and the order/project-by-entity-columns-before-DTO rule), the **85% line + branch** coverage gate, and the `dotnet-coverage` + `reportgenerator` commands. Add tests alongside new service logic before calling it done.
