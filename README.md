# ApexRacers

Lap time percentile tracking and car recommendations for iRacing weekly series. ApexRacers aggregates iRacing lap time data and shows where you rank by percentile against the full field — so you can pick the car where you are most competitive.

Driver authorization is being integrated. Eligible Demo views remain available, but legacy Real
Driver workflows and telemetry upload/lap routes return unavailable; account, catalog, series and
schedule routes retain independent access. Catalog detail omits private upload overlays. See the
[lifecycle implementation boundary](docs/research/driver-lifecycle-admission-spine.md).

Existing databases must use the forward-only lifecycle migration: it preserves unverified claim
values under a renamed physical column and creates empty authorization tables. Stop old writers
before applying it; an old binary/schema is not a supported recovery path. Ordinary startup has no
usable provider-ownership or independent-journal adapter, and stored claims do not grant access.

## Repo structure

| Path                        | Description                                                                |
| --------------------------- | -------------------------------------------------------------------------- |
| `src/ApexRacers.Core/`      | Domain models shared across all projects                                   |
| `src/ApexRacers.Data/`      | EF Core DbContext, entity configurations, and migrations                   |
| `src/ApexRacers.Api/`       | ASP.NET Core Web API (controllers, services, auth)                         |
| `src/ApexRacers.Ingestion/` | Background worker that pulls data from the iRacing API                     |
| `src/ApexRacers.Seeder/`    | CLI tool that seeds synthetic lap time data (idempotent)                   |
| `src/ApexRacers.Tests/`     | xUnit unit and PostgreSQL integration tests                                |
| `web/`                      | Vite + React + TypeScript frontend                                         |
| `docs/`                     | Public product, roadmap, and documentation-index pages                     |
| `private/` (optional)       | Standalone maintainer companion with sanitized samples and private docs    |
| `infra/`                    | Placeholder for Azure Bicep infrastructure definitions (not yet populated) |
| `.github/workflows/`        | GitHub Actions CI/CD pipelines                                             |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 26+](https://nodejs.org/)
- Docker with Compose: either [Docker Desktop](https://www.docker.com/products/docker-desktop/) or
  [Docker Engine](https://docs.docker.com/engine/install/) plus the
  [Compose plugin](https://docs.docker.com/compose/install/linux/) (the native Engine setup is
  validated on Fedora/Linux). Keep the engine running for the full backend test suite.
- iRacing OAuth credentials are optional and only needed for the ingestion worker.

## Local development setup

### 1. Clone and configure environment

```bash
git clone <repo-url>
cd apexracers
cp .env.example .env
```

Open `.env` and fill in local values before running the API. `JWT_SIGNING_KEY` is
required and must be **at least 32 bytes** — generate one with
`openssl rand -base64 48`. Live iRacing and email credentials are optional for most
local development.

### 2. Start the database

```bash
docker compose up -d
```

PostgreSQL will be available on `localhost:5432`. pgAdmin is available at `http://localhost:5050` (login: `admin@apexracers.gg` / `admin`).

### 3. Apply database migrations

Install the EF Core CLI tool if you haven't already (`dotnet tool install --global dotnet-ef`), then:

```bash
dotnet ef database update --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
```

### 4. Run the API

```bash
dotnet run --project src/ApexRacers.Api
```

The API starts on `http://localhost:5000`. In Development, the Scalar API reference is available at
`http://localhost:5000/scalar/v1`, backed by the OpenAPI document at
`http://localhost:5000/openapi/v1.json`.

Startup admin seeding uses `ADMIN_SEED_EMAILS`, a comma-separated list of addresses. A matching
account must already have a confirmed email before it can be promoted — which now happens as part
of ordinary sign-up, since registration emails a confirmation link and the account cannot sign in
until it is followed. Existing Admin memberships are preserved. Maintainer bootstrap steps live in
the private companion's deployment runbook.

### 5. Run the frontend

```bash
cd web
npm install
npm run dev
```

The dev server starts on `http://localhost:5173`. All `/api` requests are proxied to the API automatically.

`http://localhost:5173/` serves the public marketing landing page (no login required). The authenticated app starts at `/dashboard` — register or log in to access it.

Registering does not sign you in: the API emails a confirmation link and the account stays inactive
until it is followed. Locally the email lands in the mail drop described under
[Transactional email](#transactional-email) rather than a real inbox — open the `verify-email` link
from the JSON file there, then sign in. Registration answers the same way whether or not the address
already has an account, so a stale acknowledgement is not a sign anything went wrong; if you never
receive the link for an address you own, request a password reset instead, which also confirms it.

### 6. Seed the database (optional)

The default and demo seeder modes load the iRacing catalog (tracks, cars, car classes, series,
seasons, and weeks) from sanitized response objects in the optional private companion, then generate
synthetic lap times for all series. Maintainers with access can install that companion at `private/`
from the repository root:

```bash
npm run bootstrap:private
```

Once `private/iracing-api-response-objects/` is available:

```bash
dotnet run --project src/ApexRacers.Seeder            # catalog + synthetic laps (needs the JSON above)
dotnet run --project src/ApexRacers.Seeder -- --demo  # also seed the synthetic demo cache
```

If you don't have the response-object JSON (e.g. in CI), use `--ci` to seed a fully synthetic catalog
instead — no captured shapes required. It also applies any pending migrations first:

```bash
dotnet run --project src/ApexRacers.Seeder -- --ci --demo
```

The seeder is idempotent — safe to run multiple times.

After a completed Demo teardown, ordinary seeders stay fenced against reopening the old preview.
Start a fresh synthetic collection explicitly, then verify it before enabling Demo:

```bash
dotnet run --project src/ApexRacers.Seeder -- --new-preview --ci --demo
```

For captured catalog inputs, omit `--ci`. Keep Demo disabled during purge and reseeding. Controlled
synthetic Driver grants must first be unlinked and their response writers drained through the
lifecycle; the purge refuses to erase an active association directly.

Before enabling the `iracing-demo` feature flag in an environment (or after purging the demo dataset
with `src/ApexRacers.Data/Seeds/purge_demo_data.sql`), run the mechanical verification gate instead of
eyeballing the DB:

```bash
dotnet run --project src/ApexRacers.Seeder -- --verify-demo      # exit 0 iff the demo surface is fully seeded
dotnet run --project src/ApexRacers.Seeder -- --verify-teardown  # exit 0 iff no demo data remains (post-purge)
```

Both print one `[PASS]`/`[FAIL]` line per check (cache-key family, recent-race links, cache freshness, synthetic races,
BoP/weather, the `iracing-demo` flag row) and exit non-zero on any failure — CI/deploy scripts can gate
on the exit code. `--demo` runs `--verify-demo` automatically at the end of seeding and fails the run if
it doesn't pass. Recent Races is built from the Demo Driver's persisted synthetic results; verification
also requires a nonempty recent-race cache whose links resolve to persisted Subsessions. Rerun `--demo`
to replace an older cache containing broken race links.

Demo and Real evidence use explicit provenance namespaces; expiry and numeric IDs do not
identify origin. Demo misses stay offline, and Real acquisition requires Demo teardown.
Existing databases require a controlled Demo reseed after the provenance migration; legacy
copies remain unavailable pending reconciliation. Read the
[migration and verification evidence](docs/research/demo-acquisition-provenance.md) before
changing caches, seeding, teardown or recovery.

Migration `20261005162541_EvidenceCopyFencing` requires Demo disabled and physically purged before
it applies. Stop old API, ingestion and seeder writers before deployment; after migration, deploy
only the new writers, seed a fresh Demo dataset and verify it before restoring Demo. Existing
Real/Unknown copies receive no inferred purpose. Read the
[writer inventory and copy-fence evidence](docs/research/driver-copy-writer-inventory.md) for the
forward-only cutover, expiry/dependency checks and limits on backup/log-retention claims.

### 7. Run the ingestion worker (optional)

```bash
dotnet run --project src/ApexRacers.Ingestion
```

## Ports

All ports used across the project's config files (`docker-compose.yml`, `Dockerfile`, `launchSettings.json`, `vite.config.ts`, and the `.env` files):

| Port   | Service                  | Defined in                                                                                          | Notes                                                                   |
| ------ | ------------------------ | --------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------- |
| `5432` | PostgreSQL               | `docker-compose.yml` (`${POSTGRES_PORT:-5432}:5432`), `launchSettings.json`, `.env`                 | Host port; override with `POSTGRES_PORT`                                |
| `5050` | pgAdmin (host)           | `docker-compose.yml` (`${PGADMIN_PORT:-5050}:80`)                                                   | Host port; override with `PGADMIN_PORT`                                 |
| `8080` | API (Docker)             | `docker-compose.yml` (`${API_PORT:-8080}:8080`), `Dockerfile` (`EXPOSE`), `.env`, `web/.env.docker` | Host port; override with `API_PORT`. Container always listens on `8080` |
| `5000` | API (local `dotnet run`) | `launchSettings.json`, `vite.config.ts` (proxy fallback)                                            | Default when running the API directly                                   |
| `5173` | Vite dev server          | Vite default (not pinned in `vite.config.ts`)                                                       | Auto-increments if the port is taken                                    |
| `443`  | API (cloud deployment)   | `web/.env.cloud`                                                                                    | Configure with the maintainer-provided cloud API base URL.              |

The ingestion worker (`ingestion.Dockerfile`) exposes no port — it is a background worker with no HTTP listener.

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
CLOUD   (npm run dev:cloud)       :5173 --/api--> :443  configured cloud API host
```

## iRacing OAuth credentials

iRacing does not have a self-service developer portal. To obtain OAuth 2.0 credentials (`IRACING_CLIENT_ID`, `IRACING_CLIENT_SECRET`) you must contact iRacing support directly and request API access. The ingestion worker also requires a dedicated iRacing account (`IRACING_USERNAME`, `IRACING_PASSWORD`) for the Password Limited OAuth flow used to pull data.

## Transactional email

Account-confirmation, password-reset, and email-change-verification emails are sent through the
configured email provider when `ACS_CONNECTION_STRING` is set. The API also reads
`ACS_SENDER_ADDRESS` and `APP_BASE_URL` to build account links. When
`ACS_CONNECTION_STRING` is not set, the API logs the subject only and delivers nothing —
account links are never logged, because they carry single-use credentials.

To follow a reset or confirmation link locally, set `DEV_MAIL_DROP_PATH` to a directory and the
API writes each outbound email there as JSON instead of sending it. `docker compose up` does this
already, dropping mail into `TestResults/mail/` in the repo root; the E2E suite reads both links
back out of the same directory. The drop is Development-only — the API refuses to start with
`DEV_MAIL_DROP_PATH` set in any other environment, because the files hold live account links in
cleartext.

The API container runs as a non-root user; on a Linux Docker Engine host (see Prerequisites) the
bind-mounted `TestResults/mail/` keeps the host directory's ownership, so the container's write can
fail silently there and the drop simply stays empty. See the comment on that mount in
`docker-compose.yml` for the fix. Docker Desktop is unaffected.

## Contributing & support

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for the workflow and quality
gates, [docs/](docs/README.md) for public project docs, and [AGENTS.md](AGENTS.md) for the architectural conventions PRs are expected to follow. By
participating you agree to the [Code of Conduct](CODE_OF_CONDUCT.md). For help, bug reports, and
feature requests, see [SUPPORT.md](SUPPORT.md); report vulnerabilities privately per
[SECURITY.md](SECURITY.md).

## License

This project is licensed under the GNU Affero General Public License v3.0. See [LICENSE](LICENSE) for details.
