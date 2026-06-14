# ApexRacers

Lap time percentile tracking and car recommendations for iRacing weekly series. ApexRacers aggregates iRacing lap time data and shows where you rank by percentile against the full field — so you can pick the car where you are most competitive.

## Repo structure

| Path | Description |
| ------ | ------------- |
| `src/ApexRacers.Core/` | Domain models shared across all projects |
| `src/ApexRacers.Data/` | EF Core DbContext, entity configurations, and migrations |
| `src/ApexRacers.Api/` | ASP.NET Core Web API (controllers, services, auth) |
| `src/ApexRacers.Ingestion/` | Background worker that pulls data from the iRacing API |
| `src/ApexRacers.Seeder/` | CLI tool that seeds synthetic lap time data (idempotent) |
| `src/ApexRacers.Tests/` | xUnit unit tests for services and domain helpers |
| `src/web/` | Vite + React + TypeScript frontend |
| `infra/` | Placeholder for Azure Bicep infrastructure definitions (not yet populated) |
| `.github/workflows/` | GitHub Actions CI/CD pipelines |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 26+](https://nodejs.org/)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- iRacing OAuth credentials (see note below)

## Local development setup

### 1. Clone and configure environment

```bash
git clone <repo-url>
cd apexracers
cp .env.example .env
```

For local development, credentials are read from Azure Key Vault via `az login` (run `az login` once and `DefaultAzureCredential` handles the rest). Alternatively, open `.env` and fill in values directly for offline debugging.

### 2. Start the database

```bash
docker compose up -d
```

PostgreSQL will be available on `localhost:5432`. pgAdmin is available at `http://localhost:5050` (login: `admin@apexracers.local` / `admin`).

### 3. Apply database migrations

Install the EF Core CLI tool if you haven't already (`dotnet tool install --global dotnet-ef`), then:

```bash
dotnet ef database update --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
```

### 4. Run the API

```bash
dotnet run --project src/ApexRacers.Api
```

The API starts on `http://localhost:5000`. Swagger UI is available at `http://localhost:5000/swagger`.

### 5. Run the frontend

```bash
cd src/web
npm install
npm run dev
```

The dev server starts on `http://localhost:5173`. All `/api` requests are proxied to the API automatically.

`http://localhost:5173/` serves the public marketing landing page (no login required). The authenticated app starts at `/dashboard` — register or log in to access it.

### 6. Seed the database (optional)

The seeder loads iRacing catalog data (tracks, cars, car classes, series, seasons, weeks) from JSON files in `iracing-api-response-objects/`, then generates synthetic lap time data for all series so the UI is usable without live iRacing data. The directory is gitignored due to file size — you must populate it manually before running the seeder.

Use [apex-iracing](https://github.com/tomtoday/apex-iracing) to fetch the required iRacing API response objects and save them into `iracing-api-response-objects/`.

Once the directory is populated:

```bash
dotnet run --project src/ApexRacers.Seeder
```

The seeder is idempotent — safe to run multiple times.

### 7. Run the ingestion worker (optional)

```bash
dotnet run --project src/ApexRacers.Ingestion
```

## Ports

All ports used across the project's config files (`docker-compose.yml`, `Dockerfile`, `launchSettings.json`, `vite.config.ts`, and the `.env` files):

| Port | Service | Defined in | Notes |
| ---- | ------- | ---------- | ----- |
| `5432` | PostgreSQL | `docker-compose.yml` (`${POSTGRES_PORT:-5432}:5432`), `launchSettings.json`, `.env` | Host port; override with `POSTGRES_PORT` |
| `5050` | pgAdmin (host) | `docker-compose.yml` (`${PGADMIN_PORT:-5050}:80`) | Host port; override with `PGADMIN_PORT` |
| `8080` | API (Docker) | `docker-compose.yml` (`${API_PORT:-8080}:8080`), `Dockerfile` (`EXPOSE`), `.env`, `src/web/.env.docker` | Host port; override with `API_PORT`. Container always listens on `8080` |
| `5000` | API (local `dotnet run`) | `launchSettings.json`, `vite.config.ts` (proxy fallback), `.env.example` | Default when running the API directly |
| `5173` | Vite dev server | Vite default (not pinned in `vite.config.ts`) | Auto-increments if the port is taken |
| `443` | API (Azure cloud) | `src/web/.env.cloud` | `https://apexracers-api.azurewebsites.net` |

The ingestion worker (`Dockerfile.ingestion`) exposes no port — it is a background worker with no HTTP listener.

### Running alongside other local projects

Every host port above is parameterized (`${VAR:-default}`), so only the host-side
mapping ever moves — container ports, the Dockerfile, and the deployed Azure image
are untouched. To shift ApexRacers off its defaults so a plain `docker compose up`
never collides with another local stack, set `POSTGRES_PORT` / `API_PORT` /
`PGADMIN_PORT` in `.env`.

Request flow by mode (the frontend always talks to Vite on `5173`, which proxies `/api` onward):

```text
LOCAL   (npm run dev / dev:all)   :5173 ──/api──▶ :5000 (dotnet API) ──▶ :5432 (Postgres)
DOCKER  (npm run dev:docker)      :5173 ──/api──▶ :8080 (API container) ──▶ :5432 (Postgres)
                                                  pgAdmin → :5050 → (container :80)
CLOUD   (npm run dev:cloud)       :5173 ──/api──▶ :443  apexracers-api.azurewebsites.net
```

> **Note:** The OAuth redirect URI differs by environment file — `.env` targets `:8080` (Docker) while `.env.example` targets `:5000` (local). It must match wherever the API is actually listening.

## iRacing OAuth credentials

iRacing does not have a self-service developer portal. To obtain OAuth 2.0 credentials (`IRACING_CLIENT_ID`, `IRACING_CLIENT_SECRET`) you must contact iRacing support directly and request API access. The ingestion worker also requires a dedicated iRacing account (`IRACING_USERNAME`, `IRACING_PASSWORD`) for the Password Limited OAuth flow used to pull data.

## License

This project is licensed under the GNU Affero General Public License v3.0. See [LICENSE](LICENSE) for details.
