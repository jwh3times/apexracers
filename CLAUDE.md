# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

## Commands

### .NET (run from repo root)

```bash
# Build the entire solution
dotnet build

# Run the API (requires DATABASE_CONNECTION_STRING env var)
dotnet run --project src/ApexRacers.Api

# Run the ingestion worker (requires all iRacing + database env vars)
dotnet run --project src/ApexRacers.Ingestion

# EF Core migrations — always target Data project, startup project Api
dotnet ef migrations add <MigrationName> --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
dotnet ef database update --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
```

> `dotnet-ef` must be installed globally: `dotnet tool install --global dotnet-ef`. The version must match EF Core (currently 10.0.7).

### Frontend (run from `src/web/`)

```bash
npm install

# Dev servers — Vite runs on localhost:5173, proxying /api to the target below
npm run dev          # → http://localhost:5000  (dotnet API running locally)
npm run dev:all      # → http://localhost:5000  (starts dotnet API + Vite together via concurrently)
npm run dev:docker   # → http://localhost:8080  (API running in Docker Desktop)
npm run dev:cloud    # → https://apexracers-api.azurewebsites.net  (Azure deployed API)

npm run build        # tsc + Vite production build
npm run lint         # ESLint
npm run preview      # Serve the production build locally

# Tests
npm run test         # Vitest one-shot run
npm run test:watch   # Vitest in watch mode
```

The API proxy target is controlled by `API_TARGET` in the relevant `.env.*` file (`src/web/.env.docker`, `src/web/.env.cloud`). The default (`npm run dev` / `dev:all`) falls back to `http://localhost:5000` with no env file needed.

### Infrastructure

```bash
# Local Docker Desktop (postgres + pgadmin + api)
docker compose up -d

# Local Docker Desktop (postgres + pgadmin + api) with rebuild
docker compose up -d --build

# Include ingestion worker
docker compose --profile ingestion up -d

# Seed the database with initial data
Get-Content src\ApexRacers.Data\Seeds\seed_gt3_series.sql | docker compose exec -T postgres psql -U apexracers -d apexracers
```

Copy `.env.example` to `.env` and fill in `JWT_SIGNING_KEY` before running. `DATABASE_CONNECTION_STRING` is pre-filled for the Docker network.

### Azure (resource group: apexracers-rg)

| Resource | Type | Location |
| --- | --- | --- |
| `apexracersacr` | Container Registry | eastus |
| `apexracers-kv` | Key Vault | eastus |
| `apexracers-pg` | PostgreSQL Flexible Server | westus3 |
| `apexracers-plan` | App Service Plan | westus3 |
| `apexracers-api` | App Service (API) | westus3 |
| `apexracers-env` | Container Apps Environment | westus3 |
| `apexracers-ingestion` | Container App (ingestion worker) | westus3 |
| `workspace-apexracersrg0n6Q` | Log Analytics Workspace | westus3 |
| `apexracers.gg` | SSL Certificate | westus3 |

The API is deployed as an App Service; the ingestion worker runs as a Container App. Key Vault secret names use hyphens (e.g. `JWT-SIGNING-KEY`) and are mapped to underscore env var names by `HyphenToUnderscoreSecretManager` in both `Program.cs` files.

---

## Architecture

### Project dependency graph

```text
ApexRacers.Core   ← no dependencies
      ↑
ApexRacers.Data   ← EF Core, Npgsql
      ↑
ApexRacers.Api    ← Web API, Swagger, services, controllers
ApexRacers.Ingestion ← Worker Service, Aydsko.iRacingData
```

`Core` is the only project with no external dependencies. Both `Api` and `Ingestion` reference `Core` and `Data` but never each other.

### NuGet package management

All package versions are centrally managed in `Directory.Packages.props` at the repo root. **Do not add `Version="..."` attributes to `<PackageReference>` elements in `.csproj` files.** Add new packages via `dotnet add package` — NuGet will write the version into `Directory.Packages.props` automatically.

`CentralPackageTransitivePinningEnabled=true` is set intentionally: `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1` pulls EF Core 10.0.4 as a transitive dependency, which conflicts with the explicit 10.0.7 pin. The transitive pinning forces all projects to resolve 10.0.7 everywhere.

### API request flow

```text
HTTP request → Controller (parameter binding only)
                    ↓
              Service class (all business logic, EF Core queries)
                    ↓
              AppDbContext → PostgreSQL
```

Controllers contain no logic beyond binding HTTP inputs and returning `Ok(result)`. Services live in `src/ApexRacers.Api/Services/`. Response shapes are defined as `record` types in `src/ApexRacers.Api/Dtos/ResponseDtos.cs`.

### Backend API design — controllers are use-case-oriented, NOT entity-CRUD

Do not create generic CRUD controllers per entity. Each controller represents one user-facing capability:

- `SeriesController` — active weekly series list
- `WeekController` — cars and aggregate lap stats for a series week
- `PercentileController` — driver's lap time percentile for a specific car and week (computes and caches)
- `RecommendationsController` — ranked car recommendations for the authenticated user
- `AuthController` — account management: register, login, profile update (`PUT /api/auth/profile`), and iRacing OAuth 2.0 callback (pending)

If an action requires multiple steps, extract the logic into a focused service class injected via DI (e.g. `PercentileCalculationService`, `CarRecommendationService`). Do not use MediatR, command handlers, or query handlers.

### No over-abstraction

Do not create generic repository interfaces (`IRepository<T>`). Use `AppDbContext` directly in service classes. Introduce abstractions only when they solve a concrete problem.

### iRacing data ingestion

`ApexRacers.Ingestion` is a standalone `BackgroundService` worker. It uses `Aydsko.iRacingData` registered with `UsePasswordLimitedOAuth()` (four env vars: `IRACING_USERNAME`, `IRACING_PASSWORD`, `IRACING_CLIENT_ID`, `IRACING_CLIENT_SECRET`). The `IDataClient` is resolved per ingestion cycle through `IServiceScopeFactory` to safely use a scoped `AppDbContext` from a singleton service.

### Frontend

Vite dev server proxies all `/api` requests to `http://localhost:5000` (the API). The typed API client is in `src/web/src/services/api.ts` — all fetch calls go through it. Response types in `api.ts` must stay in sync with `ResponseDtos.cs` in the API.

### EF Core design-time factory

`DesignTimeDbContextFactory` in `ApexRacers.Data` reads `DATABASE_CONNECTION_STRING` from the environment and falls back to a hardcoded local dev connection string. This is what allows `dotnet ef` commands to work without setting env vars manually.

---

## Testing & coverage requirements

### Frontend (Vitest)

Coverage thresholds are enforced in `vite.config.ts` at **80%** across statements, branches, functions, and lines. `npx vitest run --coverage` must exit cleanly (no threshold errors) before any frontend change is considered done. When adding new source files, add corresponding tests to keep all four metrics above 80%.

Run coverage:

```bash
cd src/web && npx vitest run --coverage
```

### Backend (.NET)

Unit test coverage must also remain above **80%** (line coverage). Use `dotnet-coverage` + `reportgenerator` to measure:

```bash
dotnet-coverage collect "dotnet test" -f xml -o coverage.xml
reportgenerator -reports:coverage.xml -targetdir:coverage-report -reporttypes:TextSummary
```

When adding new service logic, add corresponding xUnit tests in `src/ApexRacers.Tests/`. Controllers are excluded from coverage targets (they contain no logic). Services and domain helpers in `Core` are the primary targets.

---

## General principles

- Prefer clarity over cleverness
- Introduce patterns when complexity demands them, not preemptively
- Each class has one clear responsibility
- `// TODO:` comments are acceptable stubs during scaffolding — always include a description of what needs implementing
