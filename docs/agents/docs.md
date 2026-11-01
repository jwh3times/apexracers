# Docs

## Doc map

- `AGENTS.md` — the canonical guide every coding agent reads: architecture, patterns, commands,
  the Core models, controllers, services and routes tables. It describes what is implemented, never
  what is planned. **When:** a controller or endpoint is added (`src/ApexRacers.Api/Controllers/`:
  the controllers and routes tables); a Core model is added (`src/ApexRacers.Core/Models/`: the Core
  models table); the auth mechanism changes (the AuthController, AuthContext and AuthService
  descriptions); a command, workflow or durable rule changes.
- `CLAUDE.md` — a thin entry point: the `@AGENTS.md` import plus Claude Code-only notes. **When:**
  only a Claude Code-specific note changes; content goes in `AGENTS.md`.
- `README.md` — public prerequisites, local setup, seed and ingestion instructions. **When:** a
  prerequisite, setup command or port actually changes.
- `docs/README.md` — the public documentation map and ownership rules. **When:** a public document
  or ADR is added, moved or removed; it keeps listing `CONTEXT.md` and `docs/adr/`.
- `docs/features.md` — public product capabilities and workflows. **When:** a user-facing
  capability changes.
- `docs/roadmap.md` — public high-level status and roadmap. **When:** high-level status changes,
  once the work has merged.
- `web/README.md` — frontend stack versions, commands, structure, API client, auth pattern,
  contexts and design system. **When:** a context, shared component or service is added under
  `web/src/` (the project structure and contexts tables); the frontend auth flow or 401 interceptor
  changes; design tokens or a UI pattern change (the design system section).
- `docs/content-security-policy.md` — the browser resource policy, the built-SPA asset audit and the
  Development-only API-reference exceptions. **When:** `ContentSecurityPolicy` or
  `SecurityHeadersMiddleware` (`src/ApexRacers.Api/Middleware/`) changes, or the SPA gains a
  resource dependency (a font, script or outbound connection).
- `docs/ingestion-deployment.md` — the ingestion image deploy and its pause preservation. **When:**
  `scripts/deploy-ingestion.mjs` or the ingestion job in `.github/workflows/deploy.yml` changes.
- `web/public/README.md`, `web/public/licenses/README.md` — the icon pack's files and sizes; the
  bundled fonts and their licenses. **When:** an icon file is added or removed; a Fontsource package
  is added, removed or re-versioned.
- `SECURITY.md` — supported versions, the reporting route, scope and safe harbor. **When:** the
  reporting route or the in-scope surface (the API's auth, the frontend, ingestion) changes.
- `SUPPORT.md` — where to ask for help. **When:** the issue templates or the support route change.
- `CODE_OF_CONDUCT.md`, `LICENSE` — `owner-only`.
- `THIRD_PARTY_NOTICES.md` — the license notices of third-party files kept here. `owner-only`: report
  the change when a skill's source changes.
- `.github/PULL_REQUEST_TEMPLATE.md`, `.github/ISSUE_TEMPLATE/*.md` — the PR body's sections; the
  bug and feature forms. **When:** the development flow's linking rule changes; the triage labels
  the forms apply change.
- `CHANGELOG.md` — public release notes, Keep a Changelog and SemVer. `ship-owned`.
- `CONTEXT.md`, `docs/adr/*.md` — the domain glossary and decisions, written by the
  `domain-modeling` skill. `frozen` for this skill. **When:** a term or ADR lands, keep
  `docs/README.md` listing them, and reword another document only where it uses a synonym the
  term's _Avoid_ list rules out.
- `.claude/agents/dotnet-api.md` — .NET patterns, JWT and auth configuration, EF Core and test
  rules. **When:** a new auth pattern; an auth mechanism change (the Auth and RBAC section).
- `.claude/agents/react-frontend.md` — API client patterns, auth flow, design tokens, test rules.
  **When:** a frontend auth change (the Authentication and 401 interceptor sections); new design
  tokens or a UI pattern (the Styling section); a new context, component or service (file structure).
- `.claude/agents/postgres-specialist.md` — the full schema (both schemas, every table, PKs,
  indexes) and query patterns. **When:** a Core model, table or identity-schema table is added (the
  schema table and critical indexes).
- `.claude/agents/penetration-tester.md` — the attack surface and known security controls.
  **When:** an endpoint is added (its attack-surface section); the auth mechanism changes (the Auth
  surface section: JWT configuration and new endpoint test cases).
- `.claude/agents/code-reviewer.md` — what to flag, and what is intentionally correct. **When:** a
  new intentional exception to the auth or pattern rules (an endpoint that legitimately lacks
  `[Authorize]`).
- `.claude/agents/azure-infrastructure.md` — public-safe deployment patterns and runtime
  configuration invariants. **When:** a deployment pattern change is confirmed, never from a local
  change alone.
- `.claude/agents/docker-containers.md` — Dockerfile, Compose and image-build patterns. **When:**
  `Dockerfile`, `ingestion.Dockerfile` or `docker-compose.yml` change.
- `.agents/skills/*/**` — installed skills, mirrored to `.claude/skills/`: `end-session`, `handoff`,
  `lets-go`, `ship` and `docs-updater` from `jwh3times/skills`, and the `mattpocock/skills`
  snapshot. `frozen` here: fixed upstream, and what they need to know goes in `AGENTS.md` or
  `docs/agents/`. **When:** a skill is added, removed or replaced.
- `docs/agents/*.md` — conventions and settings the installed skills read. **When:** a convention
  or setting changes.
- `CONTRIBUTING.md` — how outside contributors branch, link issues and open PRs. **When:** the
  development flow, branch naming or PR template changes.
- `docs/design/*.md`, `docs/research/*.md` — design and research records mixing dated findings with
  current-state claims. **When:** a file, procedure or contract they cite is renamed or changes;
  update the current-state claims and leave dated findings as written.
- `scripts/async-coverage-minimal/README.md`, `scripts/async-coverage-probe/UPSTREAM.md` — the
  research-only harnesses behind `docs/research/backend-async-coverage-attribution.md` (#411,
  #416). **When:** a harness's commands or files change; their findings are the research's.
- `docs/design/driver-acceptance-and-rollout.md` — the Driver Privacy Contract's accepted matrix
  source. `pinned: matrixSourceSha256 in docs/testing/driver-privacy-matrix.json` — any edit, even
  a link, fails that required check until it is reviewed against the matrix and the hash re-pinned
  in the same PR. **When:** a file or procedure it cites is renamed, or the accepted requirements
  change.
- `private/AGENTS.md`, `private/README.md` — the companion's agent guide and index. **When:** a
  public procedure or settings file they cite is renamed, or a companion document moves.
- `private/PRD.md` — the full product spec. **When:** a feature-level change only: a capability
  added, a planned feature cancelled, a user story revised. Never implementation detail.
- `private/ops/azure-deployment-runbook.md`, `private/ops/iracing-rollout.md` — exact resource
  names, secret names and provisioning commands; rollout gates. **When:** a cloud resource or secret
  is added, deployment commands change, or a rollout gate advances.
- `private/reviews/architecture-findings.md` — the record of the 2026-08-08 architecture review, not
  a live backlog. **When:** one of its dispositions is actioned or overturned.
- `private/iracing-api-response-objects/` — captured iRacing payload shapes. `append-only`.
  **When:** a new payload shape is captured.
- `private/archive.md` — the completed-work log. `frozen` since 2026-09-03: closed issues and
  `CHANGELOG.md` record completed work.
- `private/archive/security-audit-2026-06-23.md` — the retired audit record. `frozen`.

**Never in public docs:**

- live cloud resource names, secret values, connection strings, subscription IDs
- personal email addresses or account identifiers
- links to private runbooks, beyond tracked agent indexes naming their paths
- links to `docs/superpowers/` or `.superpowers/` plans (ignored local planning workspaces)

## Drift probes

- Controllers: `src/ApexRacers.Api/Controllers/*.cs`
- Services: `src/ApexRacers.Api/Services/*.cs`
- Core models: `src/ApexRacers.Core/Models/*.cs`
- Tables: `DbSet<` in `src/ApexRacers.Data/AppDbContext.cs`
- Identity-schema tables: `ToTable.*identity` in `src/ApexRacers.Data/AppDbContext.cs`
- JWT expiry: `AccessTokenMinutes` in `src/ApexRacers.Api/Services/AuthService.cs`
- Refresh-token lifetime, cap and active predicate: `RefreshTokenDays|MaxActiveTokensPerUser|ActiveAt`
  in `src/ApexRacers.Api/Services/RefreshTokenStore.cs`
- Design tokens: `@layer components` in `web/src/index.css`
- Glossary and ADRs: `CONTEXT.md`, `docs/adr/*.md`
- Required status checks: `gh api repos/jwh3times/apexracers/rules/branches/main`
