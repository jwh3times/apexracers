# Contributing to ApexRacers

Thanks for your interest in contributing! ApexRacers is lap time percentile
tracking and car recommendations for iRacing weekly series. This guide explains
how to set up the project, the quality gates your change must pass, and the
workflow for getting it merged.

By participating in this project you agree to abide by our
[Code of Conduct](CODE_OF_CONDUCT.md).

> **Security issues:** Do **not** open a public issue or pull request for a
> vulnerability. Follow the private process in [SECURITY.md](SECURITY.md).

---

## Table of contents

- [Ways to contribute](#ways-to-contribute)
- [Prerequisites](#prerequisites)
- [Local setup](#local-setup)
- [Project layout](#project-layout)
- [Development workflow](#development-workflow)
- [Quality gates (must pass before a PR is reviewed)](#quality-gates-must-pass-before-a-pr-is-reviewed)
  - [Backend (.NET)](#backend-net)
  - [Frontend (React + Vite)](#frontend-react--vite)
- [Database migrations](#database-migrations)
- [Coding conventions](#coding-conventions)
- [Commit messages](#commit-messages)
- [Branches and pull requests](#branches-and-pull-requests)
- [Reporting bugs and requesting features](#reporting-bugs-and-requesting-features)

---

## Ways to contribute

- **Report a bug** — open an issue using the bug report template.
- **Request a feature** — open an issue using the feature request template.
- **Fix or build something** — comment on the relevant issue first (or open one)
  so we can agree on the approach before you invest time. For anything beyond a
  small fix, a quick design discussion saves everyone effort.
- **Improve documentation** — typo fixes, clarifications, and new docs are all
  welcome.

This is a side project maintained by a single developer, so please be patient
with review turnaround.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 26+](https://nodejs.org/)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- The EF Core CLI: `dotnet tool install --global dotnet-ef` (version must match
  EF Core — currently **10.0.7**)
- iRacing OAuth credentials are only needed for the ingestion worker — see the
  [README](README.md#iracing-oauth-credentials). You do **not** need them for most
  contributions.

## Local setup

```bash
git clone https://github.com/jwh3times/apexracers.git
cd apexracers
cp .env.example .env        # then fill in JWT_SIGNING_KEY

docker compose up -d        # Postgres on :5432, pgAdmin on :5050

# Apply migrations
dotnet ef database update --project src/ApexRacers.Data --startup-project src/ApexRacers.Api

# Run the API (http://localhost:5000, Swagger at /swagger)
dotnet run --project src/ApexRacers.Api

# In another terminal, run the frontend (http://localhost:5173)
cd src/web
npm install
npm run dev
```

The full setup — including seeding synthetic data and running the ingestion
worker — is documented in the [README](README.md#local-development-setup).

## Project layout

| Path | Description |
| --- | --- |
| `src/ApexRacers.Core/` | Domain models shared across all projects (no dependencies) |
| `src/ApexRacers.Data/` | EF Core `DbContext`, entity configurations, migrations |
| `src/ApexRacers.Api/` | ASP.NET Core Web API — controllers, services, auth |
| `src/ApexRacers.Ingestion/` | Background worker that pulls data from the iRacing API |
| `src/ApexRacers.Seeder/` | CLI tool that seeds synthetic lap time data (idempotent) |
| `src/ApexRacers.Tests/` | xUnit tests |
| `src/web/` | Vite + React + TypeScript frontend |

Architectural conventions (use-case-oriented controllers, services hold all
logic, no generic repositories, the fluid design system on the frontend, etc.)
are documented in [CLAUDE.md](CLAUDE.md). Please read it before making
structural changes — PRs are expected to follow these patterns.

## Development workflow

1. Open or comment on an issue describing what you want to change.
2. Fork the repo (external contributors) or create a branch (collaborators).
3. Make your change in small, focused commits.
4. Add or update tests — see the coverage gates below.
5. Run the full local checks listed in [Quality gates](#quality-gates-must-pass-before-a-pr-is-reviewed).
6. Open a pull request and fill out the template.

## Quality gates (must pass before a PR is reviewed)

CI runs on every pull request and **both deploy jobs are blocked** until these
pass. Run them locally first.

### Backend (.NET)

```bash
# Build the whole solution
dotnet build

# Run tests
dotnet test

# Measure coverage (line AND branch must stay above 80%)
dotnet-coverage collect "dotnet test" -f xml -o coverage.xml
reportgenerator -reports:coverage.xml -targetdir:coverage-report -reporttypes:TextSummary
```

- **Line and branch coverage must each remain above 80%.** CI gates line
  coverage via `irongut/CodeCoverageSummary` and branch coverage via a follow-up
  step that reads `branch-rate` from the Cobertura report.
- New service logic needs matching xUnit tests in `src/ApexRacers.Tests/`.
- Controllers are excluded from coverage (they contain no logic) — put logic in
  a service and test the service.

### Frontend (React + Vite)

Run from `src/web/`:

```bash
npm run lint                 # ESLint
npx prettier --check .       # Formatting — CI runs this exact check
npx vitest run --coverage    # Tests + 80% coverage thresholds
npm run build                # tsc + production build
```

- Coverage thresholds are enforced at **80%** for statements, branches,
  functions, and lines (`vite.config.ts`). Add tests for any new source file.
- **Formatting is a hard gate.** Run `npx prettier --write .` before pushing —
  an unformatted file blocks the deploy.
- Reuse the shared utilities and design-system classes (e.g.
  `utils/lapTime.ts`, the fluid `text-*`/`card-*` classes) rather than
  introducing one-off equivalents. See [CLAUDE.md](CLAUDE.md) for the design
  system.

## Database migrations

Always target the `Data` project with `Api` as the startup project:

```bash
dotnet ef migrations add <MigrationName> --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
dotnet ef database update --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
```

Commit the generated migration files. Do **not** add `Version="..."` to
`<PackageReference>` elements — package versions are centrally managed in
`Directory.Packages.props`. Add packages with `dotnet add package`.

## Coding conventions

- **Prefer clarity over cleverness.** Introduce abstractions when complexity
  demands them, not preemptively.
- One clear responsibility per class.
- Keep API response shapes in sync between `ResponseDtos.cs` (API) and
  `src/web/src/services/api.ts` (frontend).
- `// TODO:` comments are acceptable as scaffolding stubs — always describe what
  needs implementing.

## Commit messages

This repository follows [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>: <short summary>

[optional body]
```

Common types: `feat`, `fix`, `test`, `ci`, `docs`, `refactor`, `chore`. Examples
from this project's history:

```
test: fix flaky RecommendationsPage auto-select assertion
ci: gate backend branch coverage at 80% and add AuthService tests
```

## Branches and pull requests

- Branch off `main`. Use a descriptive branch name such as
  `feat/<short-desc>`, `fix/<short-desc>`, or `ci/<short-desc>`.
- Keep PRs focused — one logical change per PR is much easier to review.
- Fill out the pull request template, link the issue it closes
  (`Closes #123`), and confirm the quality gates pass.
- All status checks must be green before merge. Dependency updates are handled
  automatically by Dependabot.

## Reporting bugs and requesting features

Use the issue templates:

- **Bug report** — include reproduction steps, expected vs. actual behavior, and
  environment details.
- **Feature request** — describe the problem you're trying to solve, not just the
  solution.

For questions and general discussion, see [SUPPORT.md](SUPPORT.md).

Thank you for contributing to ApexRacers! 🏁
