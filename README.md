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
| `infra/` | Azure Bicep infrastructure definitions |
| `.github/workflows/` | GitHub Actions CI/CD pipelines |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org/)
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

Populate all series with synthetic lap time data so the UI is usable without live iRacing data:

```bash
dotnet run --project src/ApexRacers.Seeder
```

### 7. Run the ingestion worker (optional)

```bash
dotnet run --project src/ApexRacers.Ingestion
```

## iRacing OAuth credentials

iRacing does not have a self-service developer portal. To obtain OAuth 2.0 credentials (`IRACING_CLIENT_ID`, `IRACING_CLIENT_SECRET`) you must contact iRacing support directly and request API access. The ingestion worker also requires a dedicated iRacing account (`IRACING_USERNAME`, `IRACING_PASSWORD`) for the Password Limited OAuth flow used to pull data.

## License

This project is licensed under the GNU Affero General Public License v3.0. See [LICENSE](LICENSE) for details.
