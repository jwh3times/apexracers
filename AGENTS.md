# AGENTS.md

This file is the canonical guidance for coding agents working in this repository. Claude Code reads it
via a one-line `@AGENTS.md` import in `CLAUDE.md`; other agent tools read it directly. Edit this file —
not `CLAUDE.md` — when project guidance changes.

> **Specialist agents carry the deep detail.** This file is the high-altitude map every session
> reads; the per-domain rules live in `.claude/agents/` and load only when that subagent runs. Pointers
> below say which agent owns what — don't duplicate their content back into this file.

| Work type                                                          | Agent                  |
| ------------------------------------------------------------------ | ---------------------- |
| .NET/EF features, services, auth, ingestion patterns               | `dotnet-api`           |
| React pages/components, design tokens, Vitest/Playwright rules     | `react-frontend`       |
| Schema, indexes, query patterns, migrations                        | `postgres-specialist`  |
| Dockerfiles, Compose, image builds                                 | `docker-containers`    |
| Cloud deployment patterns and runtime configuration                | `azure-infrastructure` |
| Reviewing a diff for correctness/security before merging           | `code-reviewer`        |
| Security testing (JWT/auth flows, data isolation, CORS, admin)     | `penetration-tester`   |

Docs and release metadata are prepared at **ship time** by the
[`/ship` skill](.agents/skills/ship/SKILL.md): before opening a PR it evaluates the cumulative SemVer
impact, runs the [`docs-updater` skill](.agents/skills/docs-updater/SKILL.md) scoped to the branch
diff, then rolls `[Unreleased]` into a CHANGELOG
section dated for the version the merge will mint. Run `/ship` (or say "ship it") when a branch is
ready for review.

A work session is closed out by the [`/end-session` skill](.agents/skills/end-session/SKILL.md):
it captures what the session learned into memory, brings the GitHub issues and project board items
it touched up to date, runs the `docs-updater` skill on the session's work, and cleans the
regenerable build/test leftovers out of the working tree. It is deliberately **not** `/ship` — it never opens a PR, writes a dated CHANGELOG
section, or pushes source branches. It does publish required human-follow-up wiki instructions.
Run `/ship` first if a branch is finished, then `/end-session`.

Switching machines uses `/handoff` (writes the handoff document to the Handoffs folder, registers it
in `handoff_map.json` through `.agents/skills/handoff/handoff-map.mjs`, then runs `/end-session`) and
`/lets-go` on the other machine (resumes from that active handoff and clears its entry). How the
folder reaches each machine — a synced Proton Drive folder, or a local mirror moved by the
`proton-drive` CLI — is that machine's `~/.config/handoffs/config`, which the skills propose on first
run (`.agents/skills/handoff/machine-config.md`).

## Agent config parity (Claude Code ↔ Codex)

Both tools run the same agents and skills from **one** set of sources per row below. Agents are
authored for Claude Code, with Codex generated from them; **skills run the opposite direction** —
authored under `.agents/skills/`, with the Claude Code tree generated from that. Session hooks remain
tool-specific and are not mirrored:

| Source (edit this)         | Generated (never edit)      |
| -------------------------- | --------------------------- |
| `.claude/agents/<name>.md` | `.codex/agents/<name>.toml` |
| `.agents/skills/<name>/**` | `.claude/skills/<name>/**`  |

Skills are authored under `.agents/skills/` — not `.claude/skills/` — because that is where
third-party skill installers write; keeping the install target as the authored source means
installing or updating a skill stays a one-way drop-in with no manual copying afterward. The whole
skill directory is mirrored, not just `SKILL.md` — references, `scripts/*.sh`, `agents/*.yaml`, and
any other files a skill carries are all drift-controlled. Each generated `SKILL.md` gets a
`# GENERATED — do not edit. Source: … — regenerate with 'node scripts/sync-agents.mjs'.` banner
injected as a YAML comment on line 2 (line 1 stays `---`, so the frontmatter still parses); every
other file in the tree is copied byte-for-byte with no banner. `--check` folds CRLF to LF when
comparing any non-binary file, so a tool that rewrites the generated tree with Windows line endings
after checkout does not read as permanent drift.

**Never replace the generated `.claude/skills/` tree with a symlink back to `.agents/skills/`** (or
any generated path with a symlink to its source) — two independent failure modes rule that out, both
hit for real while building this generator:

1. `readdirSync(dir, { withFileTypes: true })` reports a symlink as `isSymbolicLink()`, not
   `isDirectory()`. A directory-walking generator like this one would see zero skill sources through
   the link and, on the next run, delete every real generated file underneath it as "orphaned."
2. Git may not even be able to store the link: when `git config core.symlinks` is `false` (common on
   a Windows checkout), `git add` walks through the symlink and stages the **target file's contents**
   under the link's path instead of recording a link — duplicating every file rather than linking it.
   `git add -n .claude/skills/<name>` listing individual files (instead of one link entry) confirms
   this is happening. Worse, if the generated directory is left as a stale symlink into the source
   tree while the generator still runs against it, every "generated" write actually lands back on the
   authored source file, silently corrupting it.

The generator removes any symlink it finds in a generated tree before it writes anything, so one
`npm run sync:agents` replaces a `.claude/skills/<name>` that `npx skills update` linked back to its
source.

Regenerate with `node scripts/sync-agents.mjs` (or `npm run sync:agents`; `--check` /
`npm run sync:agents:check` verifies without writing) and commit every side that changed; the **Agent Config Sync** CI
check (`.github/workflows/agent-config-sync.yml`) runs on every PR and **fails it** when the
generated tree has drifted or leaves an orphaned generated file behind. Its job, `Verify generated agent config`, is
one of the ruleset's required status checks (since 2026-09-11), alongside `Format`, `Lint`, `Test`,
`Verify changelog version` and `Driver Privacy Contract`, so a drifted PR cannot merge. (The workflow is deliberately **not** path-filtered, and that is what makes
requiring it possible: a path-filtered check never reports at all on a PR touching none of its paths,
and GitHub then holds the merge waiting for a result that never arrives. Do not add a `paths:` filter
back — it would stall every unrelated PR.)

The generator copies prose **verbatim** — it never rewrites wording — so keep agent and skill bodies
**tool-neutral**: don't name one tool's entry-point file where "the project guide" will do, and write
repo-root-relative paths as plain text rather than relative Markdown links (a relative link resolves
differently from the mirrored location). `node scripts/lint-agent-neutrality.mjs` (`npm run
lint:agents`) enforces this: it rejects blind `claude`→`Codex` substitution artifacts (`.Codex/`,
`code.Codex.com`) and relative links that resolve from the source but break from the mirrored copy. It also rejects a
bare `$0`–`$9` in any `.agents/skills/*/SKILL.md`: Claude Code replaces those in a loaded skill body
with the invocation's argument words (issue #451), so use named variables (`read -r num state head
<<< "$pr"`) or braced `${1:-x}`, which is not substituted.

Frontmatter maps as follows: `name`/`description` carry over; a `tools:` list with no `Write`/`Edit`
becomes `sandbox_mode = "read-only"`; `model:` is **dropped** (Claude model names are not Codex model
names, so Codex uses its session default). Each generated TOML opens with a one-line `# GENERATED`
banner and holds the instructions in a `'''` literal string. The generator is canonical in
`jwh3times/skills` (see [Shared skills](#shared-skills)) and is covered by its own node:test suite
(`scripts/sync-agents.test.mjs`); the **Agent Config Sync** job runs that suite and the neutrality
lint before `--check`.

No formatter currently runs over `.agents/skills/` or `.claude/agents/` (Prettier in this repo is
scoped to the `web/` tree only — see Commands below), so there is no format-before-sync ordering
requirement today. If a formatter is ever pointed at those trees, run it before
`sync-agents.mjs`, not after: regenerating first would mirror unformatted content and drift
again on the next format pass. In that case also point the formatter's ignore file at the generated
tree (`.claude/skills/`), not the authored one.

`.claude/settings.json` and `.claude/hooks/session-start.sh` are Claude Code-specific. The repository
does not check in a project-scoped Codex config or lifecycle hooks: Codex sessions use the
configuration supplied by their user or environment, and the sync script does not create
`.codex/config.toml`, `.codex/hooks.json`, or `.codex/hooks/*`.

---

## MCP servers

Everyone working here uses the same three MCP servers. `.mcp.json` checks them in for Claude Code,
which asks each contributor to approve them once, in the first session after they appear. Codex has
no project-scoped config here (see above), so Codex users add them to their own configuration:

```bash
codex mcp add context7 --url https://mcp.context7.com/mcp
codex mcp add microsoft-learn --url https://learn.microsoft.com/api/mcp
codex mcp add playwright -- npx -y @playwright/mcp@0.0.85 --isolated
```

| Server            | Use it for                                                                                                                                                  |
| ----------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `context7`        | Current library docs (EF Core, ASP.NET Core, React, Vite, Tailwind, Playwright, …): check an API before writing against it. Remote, no account needed.      |
| `microsoft-learn` | Microsoft's official .NET, EF Core and Azure docs and code samples. Where it and Context7 disagree, it wins. Remote, no account needed.                     |
| `playwright`      | Driving the running app (`npm run dev` or `docker compose up`) in a real browser to see a UI change. It does not replace the `web/e2e/` suites. Local `npx`. |

- **Docs servers don't replace the Ground Rules' sources.** iRacing payload shapes still come from
  the captured samples and the `Aydsko.iRacingData` SDK's own types, not from a docs index.
- **Context7 keys are optional and personal.** A free key raises the rate limit. In Claude Code add
  a local-scope `context7` with an `Authorization: Bearer` header, which overrides the project entry;
  in Codex add `--bearer-token-env-var CONTEXT7_API_KEY`. A claude.ai Context7 connector may show
  the same tools twice; keep one.
- **Playwright MCP is pinned** like the workflow actions, and `--isolated` keeps its browser profile
  in memory. Dependabot does not read `.mcp.json`, so bump the version deliberately, in both
  `.mcp.json` and the Codex command above.
- **Not shared, on purpose:** a GitHub MCP (`gh` covers it, and the skills are written around it),
  GitKraken, a Postgres MCP (use `docker compose exec … psql`, as under Commands), and an Azure MCP
  (cloud work is maintainer-only). Personal servers such as mail, calendar or a password manager
  stay in your user configuration.
- **`.mcp.json` is public:** never put a key, token or private URL in it.

---

## Development flow

<!-- shared:development-flow -->

The flow skills are this repository's binding process for changing code. Read
`.agents/skills/ship/settings/dev-flow.md` (the routing, exemptions and issue linking) and
`docs/agents/dev-flow.md` (what this repository adds) before the first edit of a change, and whenever
no ready issue exists. `/ask-matt` maps the flow itself.

- **The gate.** Every code edit starts from a ready issue: an open `ready-for-agent` issue whose body
  is a `/to-spec` spec or a `/to-tickets` ticket or that carries a `/triage` brief, or a `bug` issue
  worked through `/diagnosing-bugs`, which needs the number and no spec. With none, stop before the
  first edit and route the user. Reading code, running tests and answering questions need no issue.
- **Private work.** A ready issue in the private tracker counts. Its number stays out of the branch,
  the PR, commits and public issues, and it is closed by hand after the merge.
- **The human starts every phase.** `/wayfinder`, `/grill-with-docs`, `/to-spec`, `/to-tickets`,
  `/implement` and `/triage` stay user-invoked. At a phase boundary, name the next command, say in
  one line why, and wait. Inside a phase, use `/tdd`, `/code-review`, `/diagnosing-bugs`,
  `/domain-modeling`, `/codebase-design`, `/prototype` and `/research` whenever they fit.
- **Exempt from the gate:** dependency bumps, changes touching only docs and agent config, release
  prep, and an explicit user waiver recorded in the PR body as `Flow waived by the user: <reason>`.
- **Linking.** Branches are `<type>/<issue#>-<slug>`; the PR body opens with `Closes #N` or
  `Part of #N`.
- **The flow ends at `/ship`.** `/implement` stops at a commit; `/ship` refreshes the docs, writes the
  changelog and the PR body, and opens the PR. `/ask-matt` also routes to `/pr`, `/retro` and
  `/implement-spec`, which are not installed here.
- **`/handoff` is the shared skill.** It parks the session for another machine or harness, closes it
  out with `/end-session`, and `/lets-go` resumes it. It does not write the portable file `/ask-matt`
  describes, so a `/prototype` detour stays in the session or uses a branch.
- **A wizard is not the record.** A step only a human can take follows the `## Procedure` in
  `.agents/skills/ship/settings/human-follow-up.md`, whose instructions stand without the script.
- **Research goes where it is safe to keep.** `/research` findings that would expose private material
  go to the private companion, where there is one, never to an ignored folder with no repository
  behind it. Details of an unpublished vulnerability are a draft advisory, never a research file.

<!-- /shared:development-flow -->

---

## Ground Rules

- **Don't build on unverified assumptions — ask.** When a task depends on a fact you can't confirm
  from the code, the docs, or a quick check — especially **external or domain facts** (iRacing API
  response shapes, the `Aydsko.iRacingData` SDK's wire types and their version drift / `[Obsolete]`
  fields, the chunked result/lap-data structure, iRacing semantics like percentile / BoP /
  license-category / lap-time fields) — stop and ask before designing against a guess. **Ground truth
  here is usually not a live call:** the iRacing OAuth credentials are unavailable (the standing
  blocker — see the project board), so you typically can't fetch a fresh sample. Verify **before** writing the
  implementation against what _is_ obtainable:

  - the captured field shapes in `private/iracing-api-response-objects/` — the authoritative shape
    reference; read the relevant endpoint before mapping it;
  - the `Aydsko.iRacingData` SDK's own typed models (the SDK is the wire contract — map from it, never
    serialize raw SDK types into the cache);
  - a real local dataset — `docker compose up` + `ApexRacers.Seeder` populates Postgres with catalog
    data and synthetic laps for all 7 series, so query/SQL/aggregation shape can be checked live.

  If the shape isn't in the samples and can't be reached through the SDK types or a local seed, say so
  and ask — don't infer it. Designing a structure to "discover" an unknown shape at runtime is still
  building on an assumption.

- **A sensible default for a genuinely low-stakes choice is fine — state it and proceed.** The bar:
  would being wrong force a rework or ship something incorrect (a wrong percentile, a mis-mapped lap
  time, a bad EF migration, a cache keyed on the wrong shape)? If yes, it's load-bearing — ask.

---

## Project docs

Public docs live in the repo root and `docs/`. Maintainer-only planning, raw API samples, deployment
runbooks and implementation archives live in the optional standalone companion
repository checked out at `private/`. The public repository ignores that entire nested worktree and
must build, test, and run CI when it is absent. When installed, run its Git commands from `private/`;
outer-repository Git history cannot see its changes.

| Doc                                       | What it is                                                                                             |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| `docs/README.md`                          | Public documentation map and ownership rules.                                                          |
| `docs/features.md`                        | Public product capabilities and workflows.                                                             |
| `docs/roadmap.md`                         | Public high-level status and roadmap.                                                                  |
| `CHANGELOG.md` (repo root)                | Public release notes — Keep a Changelog + SemVer; `/ship` writes the dated section each merge mints.  |
| [ApexRacers project board][board]         | **Remaining, blocked, and parked work** — the single tracker, backed by public issues and private companion human-follow-up issues. Replaced `private/ROADMAP.md` on 2026-09-04. |
| `private/archive.md`                      | Maintainer-only completed-work log — **frozen** at 2026-09-03; do not append.                          |
| `private/PRD.md`                          | Maintainer-only full product spec.                                                                     |
| `private/ops/azure-deployment-runbook.md` | Maintainer-only Azure resource names, command targets, and deployment details.                         |
| `private/iracing-api-response-objects/`   | Local captured iRacing API JSON field shapes — read before mapping any endpoint when available.        |

[board]: https://github.com/users/jwh3times/projects/2

**Human follow-ups:** when completed agent work leaves required human action, follow
`docs/agents/human-follow-up.md`: create or update a private companion issue labelled
`ready-for-human`, add it to the private ApexRacers board, and publish step-by-step private wiki
instructions with reciprocal links. This is part of the completion handoff, including before merge
when applicable; report any access blocker instead of silently omitting it.

**After completing a feature/fix:** update public docs when product capabilities, setup, or contribution
workflow changes; close or re-status the project-board item the work belongs to; and
add a bullet under the `CHANGELOG.md` `[Unreleased]` section (correct
`Added`/`Changed`/`Fixed`/`Removed`/`Security` category). Releases are automatic on merges to `main`:
`.github/workflows/version.yml`
creates a standard SemVer tag/GitHub Release in `<major>.<minor>.<build>` form, where
`web/package.json` selects the major/minor release line and the build auto-increments from existing
three-part tags. At ship time, `/ship` evaluates the cumulative release impact: a major or minor
decision advances both `web/package.json` and `web/package-lock.json` to the new `x.y.0` line, while a
build decision leaves them unchanged. If no `vX.Y.0` tag exists yet, build `0` is valid and is not
advanced to `1`. Rolling `[Unreleased]` into a versioned changelog section is a separate, deliberate
step the [`/ship` skill](.agents/skills/ship/SKILL.md) performs when a branch is shipped (dating it for
the version that merge will mint) — not during ordinary feature/fix work. After selecting the release
line, `version.yml` and `/ship` compute the exact target from the same `scripts/next-version.mjs`, and a
`Changelog Version` CI check
(`.github/workflows/changelog-version.yml`) fails a PR whose dated section has drifted from it. (The
`Doc map` in `docs/agents/docs.md` is the full doc-update matrix; the `docs-updater` skill applies it.)

> **iRacing-data gating.** Live iRacing-backed features are gated so environments without service
> credentials can still present a functional app. Two feature flags gate that surface:
>
> - `iracing-live` (M1, shipped) — real creds. When off, iRacing routes render `ComingSoonPage` and nav
>   items are hidden.
> - `iracing-demo` (Alpha opt-in preview) — reveals the same surface backed by clearly-labeled **synthetic** demo
>   data. `SubjectDriverContext` resolves the caller's Subject Driver to the Demo Driver
>   (`DemoData.DriverCustId`) for any User on the demo surface.
>
> **Preview tiers are self-selected.** Non-Admin Users can choose Standard, Beta, or Alpha in
> Settings. `MinimumRole=Alpha` exposes a feature to anyone who opts in; it does not establish an
> approved tester group or protect service-account quota. Plan Alpha previews for that open audience.
>
> `useIracingSurface` owns the `iracing-live` **OR** `iracing-demo` decision used by `RequireFlag`,
> `visibleNav`, and in-page panels. `RequireFlag` renders nothing until the current flag owner is
> ready, then chooses the child or `ComingSoonPage`; `iracing-demo` is fully functional **only** once
> a DB is seeded with `ApexRacers.Seeder --demo`.

---

## Commands

### .NET (run from repo root)

```bash
dotnet build                                          # build the solution
dotnet test                                           # run the full xUnit suite (Docker engine required for PostgreSQL integration tests)
dotnet test --filter-class ApexRacers.Tests.Models.FieldPercentileTests  # run one test class
dotnet test --project src/ApexRacers.Tests/ApexRacers.Tests.csproj \
  --configuration Release \
  --coverage --coverage-output coverage.cobertura.xml --coverage-output-format cobertura \
  --coverage-settings "$PWD/coverage.runsettings" \
  --report-xunit-trx --report-xunit-trx-filename backend-tests.trx \
  --results-directory "$PWD/TestResults"                 # coverage + stable CI-style artifacts
dotnet run --project src/ApexRacers.Api               # run the API (needs DATABASE_CONNECTION_STRING)
dotnet run --project src/ApexRacers.Ingestion         # run the ingestion worker (needs iRacing + DB env vars)
dotnet run --project src/ApexRacers.Seeder            # seed catalog + synthetic laps for 7 series (idempotent)
dotnet run --project src/ApexRacers.Seeder -- --demo  # + seed the synthetic demo cache (Plan 2)
dotnet run --project src/ApexRacers.Seeder -- --ci    # fully synthetic catalog, no response objects (CI/E2E; add --demo for the cache)
dotnet run --project src/ApexRacers.Seeder -- --new-preview --ci --demo  # explicit fresh preview after a post-migration Demo purge
dotnet run --project src/ApexRacers.Seeder -- --verify-demo      # gate: exit 0 iff the demo surface is fully seeded (also auto-runs at the end of --demo)
dotnet run --project src/ApexRacers.Seeder -- --verify-teardown  # gate: exit 0 iff no demo rows remain (M2 purge check)

# EF Core migrations — always target Data, startup project Api
dotnet ef migrations add <Name> --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
dotnet ef database update      --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
```

**Test filters go to the Microsoft Testing Platform, not VSTest.** `global.json` selects the MTP
runner, so `dotnet test` forwards every option it doesn't own straight through to the test
executable. Use `--project` when selecting a project explicitly; the 10.0.1xx SDK rejects a positional
project path in this mode. Two other consequences bite in practice:

- `--filter-class` takes a **fully qualified** type name — `ApexRacers.Tests.Models.FieldPercentileTests`,
  not `FieldPercentileTests`. A bare name matches nothing and reports `Zero tests ran` (exit code 8).
- **Don't add `--nologo`.** MTP has no such option, so it is forwarded, the runner rejects it and
  prints its help, and the run ends `Zero tests ran` / `error: 1` / exit code 5 — which reads as a
  broken filter rather than a bad flag. `--configuration`, `--no-build`, and `--verbosity` are
  `dotnet test`'s own options and stay safe.

Coverage gates require a non-empty product report with positive line and branch denominators.
Use the explicit SDK coverage command above for collection; an `N/A`/empty report does not
establish the 85% gates.

`dotnet test` is only a thin wrapper here: the test executable itself takes the same arguments, and
`src/ApexRacers.Tests/bin/Debug/net10.0/ApexRacers.Tests.exe --help` lists every filter option the
runner actually supports (`--filter-method`, `--filter-namespace`, `--filter-trait`, `--filter-query`, …).

Seeder's default/`--demo` modes need `private/iracing-api-response-objects/` populated first (gitignored —
see README); `--ci` mode does **not** (it fabricates a fully synthetic catalog, so CI/E2E can seed without
the captured shapes — see `CiCatalogSeeder`). All modes read `DATABASE_CONNECTION_STRING` (else fall back
to the local Docker default) and auto-apply pending migrations on start. `dotnet-ef` must be
installed globally and match the EF Core version in `Directory.Packages.props`. SQL cleanup scripts live in
`src/ApexRacers.Data/Seeds/` (`truncate_seed_data.sql`, `purge_demo_data.sql`),
piped in via `Get-Content … | docker compose exec -T postgres psql -U apexracers -d apexracers`.
(The old GT3 seed scripts were deleted 2026-07 — they targeted the pre-June-2026 `LapTimeEntries`
schema; the Seeder's `--ci` mode replaces them.)

### Frontend (run from `web/`)

```bash
npm install
npm run dev          # Vite :5173, proxies /api → http://localhost:5000 (local dotnet API)
npm run dev:all      # starts dotnet API + Vite together
npm run dev:docker   # proxies /api → http://localhost:8080 (API in Docker)
npm run dev:cloud    # proxies /api to the configured cloud API host
npm run build        # tsc + Vite production build
npm run lint         # Oxlint recommended correctness rules + TypeScript 7 type-aware rules
npm run test         # Vitest one-shot   (test:watch for watch mode)
npm run test:e2e     # Playwright E2E — requires app at http://localhost:8080 (e.g. docker compose up)
npm run test:e2e:ui  # Playwright UI mode (interactive)
npx prettier --check .   # CI runs this exact check — fix with: npx prettier --write .
```

Note: `npm run format` / `npm run format:check` exist but only cover `src/**` — CI's prettier check
covers the whole `web/` tree, so prefer the `npx prettier … .` forms above.

`npm run lint` also runs in CI, as a separate **Lint** job in `.github/workflows/deploy.yml` alongside
**Format** and **Test** — deliberately in that unfiltered workflow rather than a new `web/`-scoped one
with a `paths:` filter, for the same reason given under
[Agent config parity](#agent-config-parity-claude-code--codex): a path-filtered check never reports on
a PR touching none of its paths, and a required check that never reports holds the merge forever. That
unfiltered placement is what made it eligible, and **Lint became a required status check on
2026-09-19** (private #21), joining `Format`, `Test`, `Verify changelog version`, and
`Verify generated agent config` in the ruleset. Do not add a `paths:` filter to `deploy.yml` — it
would stall every PR that touches nothing under it, for all three of its jobs at once.

`Lint` is still **not** in `deploy-api`'s/`deploy-ingestion`'s `needs:`, so it gates the merge rather
than the deploy. `npm run lint` fails on Oxlint *errors* only; warnings do not affect its exit code.

Proxy target is `API_TARGET` in the relevant `web/.env.*` file; the default falls back to
`http://localhost:5000`.

The unfiltered **Driver Privacy Contract** workflow exercises the 43-row controlled synthetic
acceptance matrix through PostgreSQL, two-host HTTP, migration/restore and built-SPA cases. Read
`docs/research/driver-privacy-contract.md` before changing its collector, matrix mapping, admitted
catalog receipt or workflow. Missing/skipped cases, retries and material source/build changes fail
the gate. Its exact context is required by the active main/release ruleset with strict up-to-date
checking. A green synthetic result retains `liveEligible=false`; separate live prerequisites remain
required before any live eligibility claim.

### Infrastructure

```bash
docker compose up -d                       # postgres + pgadmin + api
docker compose up -d --build               # …with rebuild
docker compose --profile ingestion up -d   # …include the ingestion worker
```

Copy `.env.example` to `.env` and set `JWT_SIGNING_KEY` first; `DATABASE_CONNECTION_STRING` is
pre-filled for the Docker network. See the **Ports** table in [README.md](README.md#ports) (Postgres
5432, pgAdmin 5050, API 8080/5000, Vite 5173). For Dockerfile/Compose work see the `docker-containers`
agent.

### Agent config (run from repo root)

```bash
node scripts/sync-agents.mjs                  # regenerate the generated trees from the authored sources
node scripts/sync-agents.mjs --check          # what CI runs; exits 1 on drift or orphaned files
npm run sync:agents                           # same as the plain command above
npm run sync:agents:check                     # same, check mode
node scripts/lint-agent-neutrality.mjs        # tool-neutrality lint (also: npm run lint:agents)
node --test scripts/sync-agents.test.mjs      # the generator's own tests
```

Needs only Node (no install step — the script has no dependencies; the root `package.json` also
exposes repository-coordination helpers). Run it after editing anything under `.claude/agents/` or
`.agents/skills/`, and commit every side that changed. See
[Agent config parity](#agent-config-parity-claude-code--codex) for what maps to what — note skills run
the opposite direction from agents.

### Public/private repository coordination

```bash
npm run repo:status          # report branch, dirty state, upstream/base, and divergence for both repos
npm run repo:status:check    # exit nonzero if an installed worktree is dirty or unsynchronized
npm run bootstrap:private    # clone the optional companion into private/ using its 1Password URL
npm run sync:main            # synchronize both clean worktrees with origin/main
npm run sync:main -- --skip-private  # synchronize only the public worktree
npm run test:repo            # run the dependency-free repository coordination tests
```

`bootstrap:private` reads the clone URL from the `ApexRacers Repository Access` 1Password item unless
`--url <credential-free-url>` is supplied. It uses the current CLI identity when that identity can
read `ApexRacers`; coding-agent sessions may supply a private 1Password service-account reference
through `--service-account-reference` or `APEXRACERS_OP_SERVICE_ACCOUNT_REFERENCE`. The token is held
only in memory for the child command. The helper refuses to overwrite a non-empty `private/`
directory. Absence of `private/.git` is a valid public-contributor state. Before calling work
portable, inspect both repositories; commit and push each history from its own root.

`repo:status` compares an ordinary branch with its upstream. A linked-worktree branch without an
upstream may use its recorded remote base instead; it passes `--check` only when clean and exactly at
that base, so local-only, behind, and diverged work still fails. `sync:main` normally switches to and
fast-forwards `main`. When another linked worktree already holds `main`, it keeps the current clean
branch and fast-forwards it only when that branch contains no commits outside `origin/main`.

### Cloud Deployment

The API is deployed as a containerized web app that serves the React SPA from `wwwroot`; the ingestion
worker is deployed as a separate background container. Runtime secrets are provided through the cloud
secret store and mapped into app configuration. Secret names use hyphens in the store and are mapped to
underscore env vars by `HyphenToUnderscoreSecretManager` in both `Program.cs` files. Public docs should
describe the pattern, not exact maintainer resource names or private deployment runbooks.

---

## Architecture

### Projects

```text
Core  ← no deps          Data  ← EF Core, Npgsql, Azure.Storage.Blobs (references Core)
Api / Ingestion / Seeder ← reference Core + Data (Api and Ingestion never reference each other)
Tests ← xUnit on Microsoft Testing Platform v2 (references Api + Ingestion + Seeder + Core + Data)
```

The test executable uses `xunit.v3.mtp-v2` on Microsoft Testing Platform v2, selected repo-wide by
`global.json`. Use the current Visual Studio MTP Test Explorer or the current VS Code C# Dev Kit for
IDE discovery/debugging; older VSTest-only environments are unsupported. The coverage command above
writes `TestResults/backend-tests.trx` and `TestResults/coverage.cobertura.xml`.

> **Coverage note:** because Tests references Ingestion and Seeder, both assemblies are in the coverage
> denominator. Microsoft code coverage reads `coverage.runsettings`, which excludes the ingestion
> `Worker` / `Program` I/O shells while their
> decision, mapping, and persistence modules stay covered; it also excludes seeder orchestration/data
> (`Program`, `CiCatalogSeeder`, `CiCatalog`, `Demo.DemoCacheSeeder`) while pure logic like
> `SyntheticLaps` and `Verification.DemoSeedVerifier` stays covered and tested.

Package versions are centrally managed in `Directory.Packages.props` — **never** add `Version="…"` to a
`.csproj`; use `dotnet add package`. `CentralPackageTransitivePinningEnabled=true` is intentional. Full
.NET/EF/auth patterns are in the `dotnet-api` agent; schema/index/query patterns in `postgres-specialist`.

### Backend architecture — `src/AGENTS.md`

The API request flow, Driver lifecycle/privacy/publication boundaries, error handling and middleware,
controllers, services, Core models, iRacing ingestion, and the persist-vs-cache data-source strategy
live in `src/AGENTS.md` (Claude Code loads it through `src/CLAUDE.md` when work touches `src/`).
**Read it before changing anything under `src/`.**

### Frontend architecture — `web/AGENTS.md`

The typed API client, design system, routing guards, and shared components/contexts/utilities live
in `web/AGENTS.md` (loaded through `web/CLAUDE.md`). **Read it before changing anything under `web/`.**

---

## Testing & coverage

Both stacks enforce **85%** coverage; changes aren't done until it passes. The `dotnet-api` and
`react-frontend` agents carry the per-stack test rules — the load-bearing facts:

- **Frontend (Vitest):** thresholds (statements/branches/functions/lines) in `vite.config.ts`; CI also
  runs `npx prettier --check .` (the `Format` job — unformatted files block deploy) and `npm run lint`
  (the `Lint` job, Oxlint correctness rules — a required status check since 2026-09-19) from `web/`.
  Run: `cd web && npx vitest run --coverage`.
- **Backend (.NET, xUnit in `src/ApexRacers.Tests/`):** 85% **line and branch** (CI gates both —
  `irongut/CodeCoverageSummary` for line, a `branch-rate` step for branch). Test services + `Core`
  helpers directly; controllers are excluded. Use the native Microsoft Testing Platform test,
  class-filter, and Cobertura commands under [.NET](#net-run-from-repo-root); inspect the report with
  `reportgenerator` when needed.
  For Driver publication/drain transport work, read `docs/research/driver-publication-drain-rehearsal.md`
  before extending the dedicated two-process synthetic rehearsal or interpreting its evidence.
- **E2E + accessibility (Playwright):** tests live in `web/e2e/`; run with `npm run test:e2e` against the
  full stack at `http://localhost:8080`. The suite includes axe-core WCAG 2.1 A/AA audits across public
  and authenticated pages (zero-violation gate, `web/e2e/a11y.spec.ts`). A non-blocking per-PR CI workflow
  (`.github/workflows/e2e.yml`) runs the suite. E2E tests are excluded from Vitest coverage. Full detail
  in the `react-frontend` agent.
- **Browser privacy and restore rehearsals:** `web/playwright.privacy.config.ts` runs the built SPA
  against dedicated synthetic hosts after ordinary E2E; build the solution in Release first. Actual PostgreSQL snapshot/legacy-binary
  tests live in `src/ApexRacers.Tests/Restore/` and require the pinned legacy Git commit documented in
  `docs/research/driver-migration-restore.md`; CI fetches full history. These establish local
  implementation evidence, not production restore or backup certification.
- **Workflow supply chain:** every `uses:` step across `.github/workflows/` (including
  `irongut/CodeCoverageSummary` above) is pinned to a full commit SHA with a trailing `# vX.Y.Z`
  comment rather than a mutable tag — GHSA-j6j2-8f7p-qv9r; the deploy jobs hold `id-token: write`
  against production. `.github/dependabot.yml`'s `github-actions` ecosystem reads that trailing
  comment to keep pins current; see the `code-reviewer` agent for the review rule.
- **Test DB providers:** `Helpers/DbContextFactory.Create()` gives the fast tests a fresh **in-memory
  SQLite** database. It validates relational SQL translation and column constraints; `Foreign Keys=False`
  deliberately permits minimal partial fixtures. SQLite does **not** validate production foreign keys,
  schemas, max-length enforcement, or migration fidelity. Tests that depend on Npgsql translation,
  PostgreSQL transactions, or production relational constraints join `PostgreSqlCollection` and use
  `PostgreSqlFixture`: one shared pinned `postgres:18.0-alpine` container, a unique database per test,
  and `EnsureCreated` against the current EF model. That mandatory collection covers the production
  `DateTimeOffset` queries — including Uploaded Lap Race Week scoping (`RecordedAt >= start && RecordedAt
  < end`), which SQLite fails to translate at runtime rather than at compile time — and refresh-token/auth
  persistence invariants, so a running Docker engine is
  required for the full `dotnet test` / coverage commands. The independent-journal tests also need
  it: `IClassFixture<AzuriteFixture>` starts one pinned `azurite:3.37.0` blob emulator per test class
  and a fresh container pair per test (`docs/research/independent-journal-adapter.md`). **Order/project by entity columns before
  constructing a DTO** — ordering by a positional-record DTO property doesn't translate on Npgsql or
  SQLite.

---

## Agent skills

### Issue tracker

Issues live in GitHub Issues (jwh3times/apexracers), via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Canonical label vocabulary (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`) — all five exist as real labels in the tracker — plus the repository's extra `blocked` label for
work that cannot progress yet. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context — one `CONTEXT.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.

### Shared skills

<!-- shared:shared-skills -->

`end-session`, `handoff`, `lets-go`, `ship`, `docs-updater` and `what-next` install from
`jwh3times/skills`, and are never edited here: reinstalling overwrites a local edit. A fix goes
upstream, and what they need to know about this repository goes in the settings files under
`docs/agents/`, which each skill proposes on first run.

The other shared skills come from one `mattpocock/skills` snapshot (v1.3.2, pinned by commit), the
same in every repository, and are never edited here either: what they need to know goes in this
file or `docs/agents/`. Moving to a newer snapshot is a change in `jwh3times/skills`. Keep
`.agents/skills/` out of every formatter: `scripts/check-skills.mjs` fails when an installed copy
differs from `skills-lock.json`, or the lock from these commands.

Re-running the install is the update; never run `npx skills update`, which leaves `.claude/skills/`
linked to its source instead of copied:

```bash
npx skills add jwh3times/skills -s end-session -s handoff -s lets-go -s ship -s docs-updater -s what-next -a codex --copy -y
npx skills add mattpocock/skills#d1478fcb140db0b93c0c8df17bcfd311844749d0 \
  -s ask-matt -s code-review -s codebase-design -s diagnosing-bugs -s domain-modeling -s grill-me \
  -s grill-with-docs -s grilling -s implement -s improve-codebase-architecture -s prototype \
  -s research -s setup-matt-pocock-skills -s tdd -s teach -s to-questionnaire -s to-spec \
  -s to-tickets -s triage -s wait-what -s wayfinder -s wizard -s writing-for-agents \
  -a codex --copy -y
node scripts/sync-agents.mjs
```

`scripts/sync-agents.mjs`, `lint-agent-neutrality.mjs`, `check-docs.mjs`, `check-skills.mjs`, and
`sync-main.mjs`, `next-version.mjs` and `check-changelog.mjs` where this repository carries them, are
canonical in `jwh3times/skills` with their tests: fix them there, then copy them here.
`sync-agents.mjs` also writes the marked blocks in this file: change them in `jwh3times/skills`, not
here.

<!-- /shared:shared-skills -->

### Development flow

[Development flow](#development-flow) above is the summary, `.agents/skills/ship/settings/dev-flow.md` the whole flow, and
`docs/agents/dev-flow.md` what this repository adds.

---

## General principles

- Prefer clarity over cleverness.
- Introduce patterns when complexity demands them, not preemptively.
- Each class has one clear responsibility.
- `// TODO:` stubs are fine during scaffolding — always describe what needs implementing.
