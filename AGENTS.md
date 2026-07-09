# AGENTS.md

This file provides guidance to coding agents working in this repository. It is based on
`CLAUDE.md`, which remains the canonical, most detailed project guide. When this file
and `CLAUDE.md` disagree, follow `CLAUDE.md` and update this file as part of the same
documentation cleanup.

Specialist rules live in `.claude/agents/`. Read the relevant specialist file before
working in that area instead of duplicating its detailed instructions here.

| Work type                                                      | Specialist reference                     |
| -------------------------------------------------------------- | ---------------------------------------- |
| .NET/EF features, services, auth, ingestion patterns           | `.claude/agents/dotnet-api.md`           |
| React pages/components, design tokens, Vitest/Playwright rules | `.claude/agents/react-frontend.md`       |
| Schema, indexes, query patterns, migrations                    | `.claude/agents/postgres-specialist.md`  |
| Dockerfiles, Compose, image builds                             | `.claude/agents/docker-containers.md`    |
| Azure resources, Key Vault map, deploy commands                | `.claude/agents/azure-infrastructure.md` |
| Reviewing a diff for correctness/security before merging       | `.claude/agents/code-reviewer.md`        |
| Security testing: JWT/auth flows, data isolation, CORS, admin  | `.claude/agents/penetration-tester.md`   |
| Documentation sync after changes                               | `.claude/agents/docs-updater.md`         |

## Ground Rules

- Do not build on unverified assumptions. If a task depends on facts that cannot be
  confirmed from code, docs, captured samples, SDK types, or a quick local check, ask
  before implementing.
- This especially applies to iRacing API response shapes, `Aydsko.iRacingData` SDK wire
  types, chunked result/lap-data structures, percentile semantics, BoP fields, license
  categories, and lap-time fields.
- Live iRacing OAuth credentials are unavailable. Ground truth is usually one of:
  `private/iracing-api-response-objects/`, the `Aydsko.iRacingData` typed models, or a
  local seeded database from `docker compose up` plus `ApexRacers.Seeder`.
- A sensible default is fine for genuinely low-stakes choices. State the assumption and
  proceed. If being wrong would cause incorrect shipped behavior or meaningful rework,
  stop and ask.
- Prefer clarity over cleverness. Add patterns only when complexity demands them. Keep
  each class focused on one responsibility.
- `// TODO:` stubs are acceptable during scaffolding, but they must state what needs to
  be implemented.

## Planning And Project Docs

Planning and status docs live in `private/` and are gitignored. The shipped exception is
the root `CHANGELOG.md`.

| Doc                                     | Purpose                                                                                |
| --------------------------------------- | -------------------------------------------------------------------------------------- |
| `private/ROADMAP.md`                    | Canonical remaining work: blockers, milestones, backlog. Read first for "what's next". |
| `private/archive.md`                    | Canonical completed work log, newest first.                                            |
| `CHANGELOG.md`                          | Public release notes using Keep a Changelog and SemVer.                                |
| `private/PRD.md`                        | Product spec, feature definitions, screens, API and data-model summaries.              |
| `private/deployTODO.md`                 | Azure deployment runbook.                                                              |
| `private/iracing-api-response-objects/` | Authoritative iRacing API JSON field shapes. Read before mapping endpoints.            |

After completing a feature or fix:

- Remove the shipped item from `private/ROADMAP.md`.
- Prepend its summary to `private/archive.md`.
- Add an entry under the correct `[Unreleased]` category in `CHANGELOG.md`.
- Update `private/PRD.md`, `CLAUDE.md`, this `AGENTS.md`, and `README.md` when relevant.

Merges to `main` automatically create a standard SemVer tag and GitHub Release in
`<major>.<minor>.<build>` form. `web/package.json` selects the major/minor release
line; the workflow increments the build from existing three-part tags. For an
intentional major/minor bump, set `web/package.json` to `x.y.0`; if no `vX.Y.0` tag
exists yet, build `0` is valid and is not advanced to `1`. Rolling `[Unreleased]` into
a versioned changelog section remains a separate docs step.

## iRacing Feature Flags

The deployed app lacks iRacing OAuth credentials, so iRacing-data features are
non-functional in production unless gated appropriately.

- `iracing-live`: real credentials. When off, iRacing routes render `ComingSoonPage` and
  nav items are hidden.
- `iracing-demo`: Alpha-gated synthetic demo data. It is fully functional only after a DB
  is seeded with `ApexRacers.Seeder --demo`; do not enable it in prod before then.

`RequireFlag` and `visibleNav` gate on `iracing-live || iracing-demo`. `MemberContext`
resolves demo users to `DemoData.DriverCustId`.

## Commands

Run .NET commands from the repo root:

```bash
dotnet build
dotnet run --project src/ApexRacers.Api
dotnet run --project src/ApexRacers.Ingestion
dotnet run --project src/ApexRacers.Seeder
dotnet run --project src/ApexRacers.Seeder -- --demo
dotnet run --project src/ApexRacers.Seeder -- --ci
dotnet run --project src/ApexRacers.Seeder -- --verify-demo
dotnet run --project src/ApexRacers.Seeder -- --verify-teardown

dotnet ef migrations add <Name> --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
dotnet ef database update --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
```

Seeder default and `--demo` modes need `private/iracing-api-response-objects/` populated.
`--ci` fabricates a synthetic catalog and does not need captured shapes. All modes read
`DATABASE_CONNECTION_STRING` or fall back to the local Docker default, and they
auto-apply pending migrations on start.

Run frontend commands from `web/`:

```bash
npm install
npm run dev
npm run dev:all
npm run dev:docker
npm run dev:cloud
npm run build
npm run lint
npm run test
npm run test:e2e
npm run test:e2e:ui
npx prettier --check .
```

Prefer `npx prettier --check .` / `npx prettier --write .` from `web/`; CI checks the
whole `web/` tree. `npm run format` and `npm run format:check` only cover `src/**`.

Infrastructure:

```bash
docker compose up -d
docker compose up -d --build
docker compose --profile ingestion up -d
```

Copy `.env.example` to `.env` and set `JWT_SIGNING_KEY` first.

## Architecture

Project dependency shape:

```text
Core  <- no deps
Data  <- EF Core, Npgsql; references Core
Api / Ingestion / Seeder <- reference Core + Data
Tests <- xUnit; references Api + Ingestion + Seeder + Core + Data
```

Package versions are centrally managed in `Directory.Packages.props`. Do not add
`Version="..."` to `.csproj` files; use `dotnet add package`.

API request flow:

```text
HTTP request -> Controller -> Service -> AppDbContext -> PostgreSQL
```

Controllers should bind inputs and return results. Put logic in services under
`src/ApexRacers.Api/Services/`. Response shapes are records in `Dtos/ResponseDtos.cs`.
Do not introduce MediatR, command/query handlers, or `IRepository<T>`; use
`AppDbContext` directly.

Unhandled exceptions are converted by `ExceptionHandlingMiddleware` into RFC-7807
`application/problem+json`. Services should throw; do not catch and convert to
`BadRequest(string)` unless the controller has an explicit non-exception outcome.

## Controllers And Services

Controllers are use-case-oriented, not entity CRUD. `[Authorize]` is the default unless
the endpoint is explicitly public. iRacing-linked endpoints return typed `409`
`IRACING_NOT_LINKED` when the user is unlinked.

Service classes should have one responsibility. Pure heuristics, mappers, and parsers
should be extracted and unit-tested directly. Before adding or changing services, check
`CLAUDE.md` and `.claude/agents/dotnet-api.md` for the current service inventory and
patterns.

## Data Strategy

iRacing data reaches read paths in three ways:

1. Persisted typed entities for canonical/shared data that must be queried, filtered,
   aggregated, joined, or preserved as history.
2. `CachedIRacingClient` plus `ExternalDataCache` for read-mostly, staleness-tolerant
   per-user or per-query stats displayed roughly as-is.
3. Persisted user-owned data such as `PersonalLap`, `Rival`, `CarPercentileResult`, and
   Identity data.

Cache mapped DTOs, never raw Aydsko SDK types. Their wire shape drifts and may include
obsolete fields. Use TTL-only eviction; `ExternalDataCacheCleanupService` purges
long-expired rows.

## Frontend

All frontend work lives in `web/`.

- The typed API client is `web/src/services/api.ts`.
- All fetches must route through its private `request<T>(path, init)` helper.
- Keep `api.ts` response types in sync with `ResponseDtos.cs`.
- Do not reintroduce per-verb API helpers.
- Use shared utilities such as `formatLapTime`, `topPercentLabel`, `deriveAlerts`, and
  `breadcrumbs` instead of inlining equivalents in pages.

Design system rules:

- Use fluid `clamp()` utility classes from `web/src/index.css`, not Tailwind breakpoints,
  for new UI sizing.
- Primary accent is cyan: `text/bg/border-primary-container`.
- Do not hardcode old green accents such as `#00FF88`, `#00e479`, or green RGBA glows.
- `primary-fixed-dim` is the allowed dim-accent token.
- For detailed component, card, token, Vitest, and Playwright patterns, read
  `.claude/agents/react-frontend.md`.

Routing, auth, feature-flag behavior, and the current screen inventory are documented in
`CLAUDE.md`. Check it before adding routes or changing navigation.

## Testing And Coverage

Both stacks enforce 85% coverage. Changes are not done until the relevant checks pass or
you clearly report why they could not be run.

Frontend:

```bash
cd web
npx vitest run --coverage
npx prettier --check .
```

Backend:

```bash
dotnet build
dotnet-coverage collect "dotnet test" -f xml
```

E2E and accessibility:

```bash
cd web
npm run test:e2e
```

Playwright E2E expects the full stack at `http://localhost:8080`, for example from
`docker compose up`.

Test database guidance:

- `Helpers/DbContextFactory.Create()` uses in-memory SQLite with `Foreign Keys=False`.
- Use `CreateInMemory()` only for production queries valid on Npgsql but not translatable
  by SQLite, currently `DateTimeOffset` range filters/ordering in the documented services.
- Order/project by entity columns before constructing DTOs; ordering by positional-record
  DTO properties does not translate reliably.

## Documentation Discipline

If behavior, commands, architecture, feature flags, routes, or workflows change, update
the docs in the same task. At minimum, consider `CHANGELOG.md`, `private/ROADMAP.md`,
`private/archive.md`, `private/PRD.md`, `README.md`, `CLAUDE.md`, and this file.

Keep this file concise. Put detailed, domain-specific guidance in the existing specialist
files under `.claude/agents/` and keep `CLAUDE.md` as the full-session map.
