# Dependabot

What reviewing a Dependabot pull request takes here, read by the `dependabot` skill in a session
and in the scheduled review. These facts apply to this repository only. How a Dependabot PR merges
is in [`merging.md`](merging.md#dependabot-prs).

## Stack

- .NET 10 solution, `ApexRacers.sln`: the API (`src/ApexRacers.Api`, which also serves the built
  SPA), the ingestion worker (`src/ApexRacers.Ingestion`), EF Core in `src/ApexRacers.Data`, the
  seeder (`src/ApexRacers.Seeder`) and the tests (`src/ApexRacers.Tests`). `global.json` pins SDK
  `10.0.100` with `rollForward: latestMinor`; the workflows' `actions/setup-dotnet` installs `10.x`.
  `Directory.Packages.props` manages versions centrally with
  `CentralPackageTransitivePinningEnabled`.
- The SPA in `web/`: Vite, React, TypeScript 7, Tailwind CSS 4, oxlint, Vitest, Playwright. Node 26
  is pinned only by the workflows' `actions/setup-node` `'26'` and the `Dockerfile`
  (`node:26-alpine`); `engines` in `web/package.json` says `>=26`, and there is no `.nvmrc`.
- Images: `Dockerfile` (API and SPA) builds from `node:26-alpine`,
  `mcr.microsoft.com/dotnet/sdk:10.0` and `mcr.microsoft.com/dotnet/aspnet:10.0`;
  `ingestion.Dockerfile` from `dotnet/sdk:10.0` and `dotnet/runtime:10.0`. `docker-compose.yml`
  runs `postgres:18-alpine` and `dpage/pgadmin4`.
- Production: every push to `main` builds both images and deploys them to Azure Container Apps
  (`deploy.yml`, jobs `Deploy API` and `Deploy Ingestion Worker`). The database is an Azure
  Database for PostgreSQL Flexible Server on PostgreSQL 18 (`.claude/agents/postgres-specialist.md`;
  its resource names are in the private companion).
- Workflows, all on GitHub-hosted `ubuntu-latest`: `deploy.yml` (Format, Lint, Test, then the
  deploys), `driver-privacy-contract.yml`, `e2e.yml`, `security-audit.yml`,
  `agent-config-sync.yml`, `changelog-version.yml` and `version.yml`.
  `backend-shared-experiment.yml` runs only on a prototype branch or by hand.
- `.github/dependabot.yml` updates `nuget`, `github-actions`, `docker` and `docker-compose` at `/`,
  and `npm` at `/web`. Minor and patch bumps are grouped per ecosystem. Majors open one PR each,
  except the peer-locked families (`react`; `vite`, `vitest`, `@vitejs/plugin-react` and
  `@vitest/*`; `@testing-library/*`; `oxlint`, `oxlint-tsgolint` and `typescript`), which travel
  together.

## Label

ApexRacers

## Review triggers

- **`postgres`, any bump.** A PostgreSQL major is always a break here (`Grounding facts`), and the
  compose tag, `18-alpine`, names only the major.

## Resolution

Under Node 26 (the workflows' `setup-node`; no file pins it) and the SDK `global.json` names:

- NuGet (`Directory.Packages.props`, a `.csproj`), from the root: `dotnet restore ApexRacers.sln`,
  then `dotnet build -c Release --no-restore`.
- npm, from `web/`: `npm ci`.

## Where to look

- The `postgres` pins. Dependabot's `docker-compose` update moves only `docker-compose.yml`
  (`postgres:18-alpine`), and leaves the rest: the service container in `e2e.yml`
  (`postgres:18-alpine`), and the Testcontainers images in `src/ApexRacers.Tests/`
  (`postgres:18.0-alpine` in `Helpers/PostgreSqlFixture.cs`, `References/BrowserFixture.cs`, and the
  `PostgreSqlImage` recorded in `Data/`, `Lifecycle/`, `Publication/` and `Restore/`). The compose
  data volume `postgres_data` is mounted at `/var/lib/postgresql`.
- The .NET 10 pins: `global.json`, `dotnet-version` in the workflows, and the `dotnet` tags in both
  `Dockerfile` and `ingestion.Dockerfile`.
- The Node 26 pins: `node-version` in the workflows, `node:26-alpine` in `Dockerfile`, `engines` in
  `web/package.json`.
- The ignore rules in `.github/dependabot.yml`: .NET-versioned majors, and `Npgsql.OpenTelemetry`
  majors with them, are held until the .NET 11 migration (#512); `postgres` majors in
  `docker-compose.yml` are held until the production database move (#533) picks the next major.
- The SPA's tool configs: `web/vite.config.ts` (with the Vitest coverage thresholds),
  `web/tsconfig*.json`, `web/postcss.config.cjs`, `web/.prettierrc`,
  `web/playwright.config.ts` and `web/playwright.privacy.config.ts`.

## Grounding facts

- **No PR job builds a Docker image.** The only `docker build` steps are in `deploy.yml`'s deploy
  jobs, which run on a push to `main` (`if: github.event_name == 'push'`), so they show as skipped
  on a PR. A green PR says nothing about `Dockerfile` or `ingestion.Dockerfile`, and a broken image
  breaks the production deploy right after the merge.
- A PostgreSQL major, of the `postgres` image or the Flexible Server, is an inherent break: a newer
  major refuses to start on a data directory an older major created. It is always NEEDS EFFORT and
  needs no external confirmation, but ground the steps in this repository. CI's databases start
  empty, so a green CI does not show that existing data survives.
- A .NET major moves together with `global.json` and the `dotnet` tags in both `Dockerfile` and
  `ingestion.Dockerfile`. `rollForward: latestMinor` does not cross a major, so a lone SDK image
  major fails `dotnet publish` in the image build (#506). Until #512 these majors are ignored.
- Central transitive pinning is on, so a package whose new release needs a higher version of
  another centrally pinned package fails restore with `NU1109: Detected package downgrade` until
  the pin moves with it (#509).
- Coverage gates are 85%: backend line and branch in `deploy.yml`'s `Test` job, frontend lines,
  functions, branches and statements in `web/vite.config.ts`.

## Playbook

- **A Docker image bump** (`Dockerfile`, `ingestion.Dockerfile`): CI is not the gate (see
  `Grounding facts`). Build both images from the PR head: `docker build -f Dockerfile .` and
  `docker build -f ingestion.Dockerfile .`. Read the diff for every `FROM` line the PR moves.
- **A PostgreSQL major (18 to N).** Bumping the tag does not migrate data. Move every pin in
  `Where to look` together. Locally the old data directory has to go: `docker compose down -v`
  deletes the seeded volume, so it is the user's step, never an agent's (`workspace.md`); use
  `pg_dump` and a restore if the data must survive. Then `docker compose up -d`, apply the
  migrations with
  `dotnet ef database update --project src/ApexRacers.Data --startup-project src/ApexRacers.Api`
  and re-seed with `dotnet run --project src/ApexRacers.Seeder` (which also applies pending
  migrations); `dotnet-ef` is a global tool matching the EF Core version (`AGENTS.md`). The compose
  image never reaches production: the Flexible Server's major is a separate operator upgrade, whose
  resource names are in the private companion. Verify: the app boots, the migrations apply, and a
  query returns seeded data.
- **A .NET major, once #512 lifts the ignores**, and only for a breaking change step 5 found: an EF
  Core major needs a migration and runtime review. Move `global.json`, the workflows'
  `dotnet-version` and the `dotnet` tags in both Dockerfiles together, run `dotnet build` and
  `dotnet test --project src/ApexRacers.Tests/ApexRacers.Tests.csproj`, and build both images.
  Cite the specific change.
- **A build-critical npm major in `web/`**: from `web/`, `npm run build`, `npm run lint` and
  `npx vitest run --coverage` (the 85% gate). Cite the specific change.
