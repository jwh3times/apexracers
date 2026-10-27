# Development flow

The **Development flow** section of `AGENTS.md` states the gate, the user-invoked rule and the
exemptions. This doc holds what applying them needs: how to confirm a ready issue, where to route
when there is none, which paths count as code, the exemption details, and how a branch and PR link
to their issue. The flow map itself — what each skill does and how the phases connect — belongs to
the `/ask-matt` router (`.agents/skills/ask-matt/SKILL.md`); read it there rather than here.

## Confirming a ready issue

Read the issue before the first edit:

```bash
gh issue view <n> --json state,labels,body,comments
```

It is ready when it is open and one of:

- **`ready-for-agent`**, with a body that is a `/to-spec` spec or a `/to-tickets` ticket, or with a
  `/triage` agent brief. `/triage` posts its brief as a **comment**, so look in `comments` as well as
  the body.
- **`bug`** (the label the bug report template applies), worked through `/diagnosing-bugs`. The issue
  number is the requirement here; a spec is not.

A `ready-for-human`, `needs-triage`, `needs-info` or `wontfix` issue is not ready. Route it instead.

## Routing when there is no ready issue

Stop before the first edit, tell the user which route fits and why in one line, and wait for them to
start it.

| Situation                         | Route                                                                         |
| --------------------------------- | ----------------------------------------------------------------------------- |
| An idea one session can hold      | `/grill-with-docs`, then `/to-spec` (then `/to-tickets` if it spans sessions) |
| Huge, foggy, multi-session effort | `/wayfinder`, then `/to-spec` once the map clears                             |
| A raw issue someone else filed    | `/triage`                                                                     |
| A ready issue nobody has started  | `/implement`, in a fresh context                                              |
| Something broken                  | File the bug issue (bug report template), then `/diagnosing-bugs`             |

## What counts as code

Code is every tracked path except **docs and agent config**, which are:

- Markdown files (`*.md`) anywhere in the repository — `AGENTS.md`, `CLAUDE.md`, `CHANGELOG.md`,
  `CONTEXT.md`, the other root guides, `docs/**/*.md` (including `docs/adr/` and `docs/agents/`),
  `web/README.md`, and the `.github/` issue and PR templates.
- Agent config: `.claude/agents/`, `.agents/skills/`, `.claude/skills/`, `.codex/agents/`,
  `.claude/settings.json`, `.claude/hooks/` and `skills-lock.json`.

Everything else is code, including the non-Markdown files under `docs/`: `docs/testing/*.json` and
`docs/research/*.json` are fixtures and receipts that tests and `scripts/driver-privacy-contract.py`
read. The main code paths are `src/`, `web/`, `scripts/` (which includes the agent-sync generator
`scripts/sync-agents.mjs`), `.github/workflows/`, `.github/dependabot.yml`, `infra/`, `Dockerfile`,
`ingestion.Dockerfile`, `docker-compose.yml`, `.env.example`, and the build files
`ApexRacers.sln`, `Directory.Packages.props`, `global.json`, `package.json` and
`coverage.runsettings`. A change that touches both sides is a code change.

## Exemptions

- **Dependency bumps** — Dependabot PRs (`dependabot/…` branches) and `chore(deps):` /
  `chore(deps-dev):` changes.
- **Docs and agent config only** — every changed path is on the docs-and-agent-config list above.
- **Release prep** — `chore(release): prepare vX.Y.Z` commits, and the release metadata `/ship`
  writes: the dated `CHANGELOG.md` section and a release-line change to `web/package.json` and
  `web/package-lock.json`.
- **User waiver** — the user waives the flow for this change in their own words. Record it in the PR
  body as `Flow waived by the user: <reason>`, quoting their reason.

## Linking work to its issue

**Branches** are `<type>/<issue#>-<slug>`, with the types this repo already uses — `feat`, `fix`,
`refactor`, `test`, `docs`, `ci`, `research`, `investigate`, `chore`:

- `feat/428-percentile-history-readers`
- `fix/451-end-session-positional-args`
- `docs/445-router-version`

Exempt work with no issue drops the number: `docs/end-session-squash-branch-cleanup`.
Dependabot keeps its own branch names, and `/prototype` keeps its `prototype/<name>` branches.

**The PR body opens with the link line**, above `## Summary`:

- `Closes #N` — the PR finishes ticket, spec or bug `N`.
- `Part of #N` — the PR is one slice of spec `N`, which stays open.

A number in the PR title alone does not link the issue; only the body line does. Put `Refs #N` for
other related issues lower in the body. `.github/PULL_REQUEST_TEMPLATE.md` opens with a `Closes #`
line: complete it, or change it to `Part of #N`. A waived PR replaces it with its
`Flow waived by the user:` line, and other exempt PRs delete it.
