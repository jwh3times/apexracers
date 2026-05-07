# ApexRacers

Lap time percentile tracking and car recommendations for iRacing weekly series. ApexRacers aggregates iRacing lap time data and shows where you rank by percentile against the full field — so you can pick the car where you are most competitive.

## Repo structure

| Path | Description |
|------|-------------|
| `src/ApexRacers.Core/` | Domain models shared across all projects |
| `src/ApexRacers.Data/` | EF Core DbContext, entity configurations, and migrations |
| `src/ApexRacers.Api/` | ASP.NET Core Web API (controllers, auth callback) |
| `src/ApexRacers.Ingestion/` | Background worker that pulls data from the iRacing API |
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
git clone https://github.com/your-org/apexracers.git
cd apexracers
cp .env.example .env
# Edit .env and fill in your iRacing credentials
```

### 2. Start the database

```bash
docker compose up -d
```

PostgreSQL will be available on `localhost:5432`. pgAdmin is available at `http://localhost:5050` (login: `admin@apexracers.local` / `admin`).

### 3. Apply database migrations

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

### 6. Run the ingestion worker (optional)

```bash
dotnet run --project src/ApexRacers.Ingestion
```

## iRacing OAuth credentials

iRacing does not have a self-service developer portal. To obtain OAuth 2.0 credentials (`IRACING_CLIENT_ID`, `IRACING_CLIENT_SECRET`) you must contact iRacing support directly and request API access. The ingestion worker also requires a dedicated iRacing account (`IRACING_USERNAME`, `IRACING_PASSWORD`) for the Password Limited OAuth flow used to pull data.

## License

This project is licensed under the GNU Affero General Public License v3.0. See [LICENSE](LICENSE) for details.
