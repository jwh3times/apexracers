---
name: docker-containers
description: Use for changes to Dockerfile, Dockerfile.ingestion, docker-compose.yml, or local container development configuration for the API, ingestion worker, or frontend build.
tools: Read, Write, Edit, Bash, Glob, Grep
model: sonnet
---

You are working with the ApexRacers container setup. Know the multi-stage build structure and Compose topology exactly.

## Two Dockerfiles at repo root

### `Dockerfile` — API + React SPA

Three stages:

1. **`frontend`** (`node:22-alpine`) — installs npm deps then builds React:
   - Copy `package.json` + `package-lock.json` first → `npm ci` → copy `src/web/` → `npm run build`
   - Output: `/app/dist`

2. **`api-build`** (`mcr.microsoft.com/dotnet/sdk:10.0`) — publishes the API:
   - Copy `Directory.Packages.props` first (required for central package management)
   - Copy only the three needed projects: `Core`, `Data`, `Api`
   - `dotnet publish ApexRacers.Api.csproj -c Release -o /publish --no-self-contained`
   - Output: `/publish`

3. **Runtime** (`mcr.microsoft.com/dotnet/aspnet:10.0`):
   - Copy `/publish` from api-build stage
   - Copy `/app/dist` from frontend stage → `./wwwroot`
   - `EXPOSE 8080`, `ENTRYPOINT ["dotnet", "ApexRacers.Api.dll"]`

The React SPA is served from `wwwroot` in production — same origin as the API, no CORS needed.

### `Dockerfile.ingestion` — ingestion worker only

Two stages:

1. **`build`** (`mcr.microsoft.com/dotnet/sdk:10.0`):
   - Copy `Directory.Packages.props` + three projects: `Core`, `Data`, `Ingestion`
   - `dotnet publish ApexRacers.Ingestion.csproj -c Release -o /publish --no-self-contained`

2. **Runtime** (`mcr.microsoft.com/dotnet/runtime:10.0`) — **runtime only, not aspnet** (no HTTP server needed):
   - `ENTRYPOINT ["dotnet", "ApexRacers.Ingestion.dll"]`

## Docker Compose (`docker-compose.yml`)

Four services:

| Service | Image | Port | Notes |
|---|---|---|---|
| `postgres` | `postgres:16-alpine` | 5432 | Healthcheck: `pg_isready -U apexracers` |
| `pgadmin` | `dpage/pgadmin4` | 5050→80 | admin@apexracers.local / admin |
| `api` | built from `Dockerfile` | 8080 | Depends on postgres health |
| `ingestion` | built from `Dockerfile.ingestion` | — | Profile: `ingestion` |

All services that depend on postgres use `condition: service_healthy` — the healthcheck must pass before they start.

The `ingestion` service is gated behind the `ingestion` profile:
```bash
docker compose --profile ingestion up -d   # include ingestion worker
docker compose up -d                        # postgres + pgadmin + api only
```

## `.env` file

Copy `.env.example` to `.env` at repo root before first run. Required:
- `JWT_SIGNING_KEY` — must be set; no default
- `DATABASE_CONNECTION_STRING` — pre-filled for Docker network: `Host=postgres;Database=apexracers;Username=apexracers;Password=devpassword`
- `POSTGRES_PASSWORD` — defaults to `devpassword`
- `ADMIN_SEED_EMAILS` — optional comma-separated list for admin bootstrap

Ingestion-only vars (only needed with `--profile ingestion`):
- `IRACING_USERNAME`, `IRACING_PASSWORD`, `IRACING_CLIENT_ID`, `IRACING_CLIENT_SECRET`
- `INGESTION_INTERVAL_MINUTES` — defaults to 60

## Layer caching — key ordering rules

Both Dockerfiles follow this ordering pattern to maximize cache hits:
1. Copy lock files / props first (rarely change)
2. Run install / restore (expensive, should be cached)
3. Copy source (changes frequently, invalidates from here down)

When editing Dockerfiles, preserve this order. Moving a `COPY src/` before a `RUN npm ci` or `RUN dotnet publish` will break layer caching.

## Useful commands

```bash
# Build and start (with rebuild)
docker compose up -d --build

# Start with ingestion worker
docker compose --profile ingestion up -d

# Rebuild a single service
docker compose build api

# Follow API logs
docker compose logs -f api

# Connect to postgres
docker compose exec postgres psql -U apexracers -d apexracers

# Run SQL seed script
Get-Content src\ApexRacers.Data\Seeds\truncate_seed_data.sql | docker compose exec -T postgres psql -U apexracers -d apexracers
```

## Base image versions

- Node: `22-alpine`
- .NET SDK: `10.0`
- .NET ASP.NET runtime: `10.0`
- .NET runtime (ingestion): `10.0`
- PostgreSQL: `16-alpine`

When updating .NET base images, update both Dockerfiles together. The SDK version must match the `<TargetFramework>net10.0</TargetFramework>` in the `.csproj` files.
