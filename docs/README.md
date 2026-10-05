# ApexRacers Documentation

This directory contains public, contributor-safe project documentation.

## Public docs

| File | Purpose |
| --- | --- |
| [features.md](features.md) | Product capabilities and user-facing workflows. |
| [roadmap.md](roadmap.md) | High-level project status and planned work. |
| [content-security-policy.md](content-security-policy.md) | Browser resource policy, asset audit, and Development API-reference exceptions. |
| [research/iracing-terms-2026-09-30.md](research/iracing-terms-2026-09-30.md) | Dated research on iRacing's third-party terms and an audit of ApexRacers' Driver identity exposure. |
| [research/iracing-data-permissions-2026-10-01.md](research/iracing-data-permissions-2026-10-01.md) | Historical public-source review separating access, identity proof, disclosure consent and processing permission; its open questions record the 2026-10-01 planning frontier. |
| [DriverDisclosure.prototype.html](../web/src/features/racing/DriverDisclosure.prototype.html) | Historical 2026-10-01 synthetic disclosure exploration; open the standalone HTML locally. Its illustrative row model is superseded by the accepted architecture and is not deployed enforcement. |
| [design/driver-disclosure-ownership.md](design/driver-disclosure-ownership.md) | Accepted Driver authorization, publication, and copy-lifecycle architecture, with interfaces and implementation proof obligations; distinct from implemented behavior. |
| [design/driver-acceptance-and-rollout.md](design/driver-acceptance-and-rollout.md) | Accepted scenario/evidence matrix, implementation readiness, migration safeguards, and staged pilot/expansion gates; validation remains to be executed. |
| [design/driver-implementation-handoff.md](design/driver-implementation-handoff.md) | Bounded implementation slices, evidence prerequisites, and links to their authoritative work briefs. |
| [research/driver-publication-coordination-2026-10-02.md](research/driver-publication-coordination-2026-10-02.md) | Primary-source technical review of cross-instance withdrawal and HTTP response-dispatch coordination. |
| [research/driver-publication-drain-rehearsal.md](research/driver-publication-drain-rehearsal.md) | Executable two-process Kestrel/PostgreSQL transport rehearsal, failure outcomes, reproduction commands and limits of its synthetic evidence. |
| [research/driver-lifecycle-admission-spine.md](research/driver-lifecycle-admission-spine.md) | Implemented journal-first lifecycle, protected-writer admission, legacy route fences and forward-only schema boundary, with synthetic evidence and remaining integration limits. |
| [research/driver-copy-writer-inventory.md](research/driver-copy-writer-inventory.md) | Participating evidence writers, durable purpose/generation/source fences, historical collection and synthetic cleanup evidence, with controlled cutover and deployed retention limits. |
| [research/driver-publication-safety-2026-10-02.md](research/driver-publication-safety-2026-10-02.md) | Primary-source review of coarsening, linkage, and repeated-release risks, with candidate publication algorithms. |
| [../README.md](../README.md) | Local setup and common development commands. |
| [../CONTRIBUTING.md](../CONTRIBUTING.md) | Contribution workflow and quality gates. |
| [../SECURITY.md](../SECURITY.md) | Vulnerability reporting policy. |
| [../CHANGELOG.md](../CHANGELOG.md) | Public release notes. |
| [../CONTEXT.md](../CONTEXT.md) | Domain-language glossary — canonical terms (e.g. Series/Season, the Race Session/Split/Subsession hierarchy, User/Driver identity, Venue/Track, Race Lap/Uploaded Lap evidence, Percentile Rank/Top Share) shared across the product and its docs. |
| [adr/](adr/) | Architecture decision records — why a structural decision was made and what alternative was rejected, not just what shipped. |

## Private docs

For acquisition, mapped caches, Demo seeding, teardown or provenance cutover, read
[Demo acquisition evidence](research/demo-acquisition-provenance.md). It records implemented
namespace boundaries and migration checks, with explicit limits on lifecycle and rollout claims.

Deployment runbooks, the full product spec, sanitized API samples, and archived implementation notes
live in a standalone private companion repository checked out at `private/`. The public repository
intentionally ignores the nested worktree and must not require it for builds, tests, CI, or normal
external contribution.

Remaining, blocked, and parked work is tracked on the [ApexRacers project board][board] instead,
backed by public implementation issues and private companion human-follow-up issues — see
`AGENTS.md` for how work moves through it. Open, unfixed
security findings are held as draft GitHub security advisories on this repository rather than in a
private planning doc, so nothing is disclosed publicly before a fix ships.

[board]: https://github.com/users/jwh3times/projects/2

Maintainers can install the companion with `npm run bootstrap:private`; the helper retrieves its
credential-free clone URL through the current 1Password identity or an explicitly supplied private
service-account reference, and refuses to overwrite a non-empty directory. Run `npm run repo:status`
to inspect both histories; a linked-worktree branch without an upstream is compared with its
recorded remote base without treating that base as a push destination. Run `npm run sync:main` to
synchronize both clean worktrees with `origin/main`. When another linked worktree already holds
`main`, the helper keeps and safely fast-forwards the current branch only if it has no local-only
commits; that linked-worktree case is refused otherwise. In an ordinary worktree it switches to and
fast-forwards `main`. Dirty or diverged worktrees are refused, and an uninstalled companion is
skipped. Pass `-- --skip-private` to synchronize only the public worktree. Absence of `private/.git`
is a supported state. `npm run test:repo` runs the dependency-free tests for these helpers.

## Agent docs

`AGENTS.md` is the canonical coding-agent guide, and `.claude/agents/` contains
specialist guidance. `CLAUDE.md` is only a thin `@AGENTS.md` import shim for Claude
Code; edit shared guidance in `AGENTS.md`, not the shim. Keep tracked agent docs
focused on repo behavior and implementation conventions. Do not add secrets, live
credentials, personal account data, or private deployment runbooks to them.

`docs/agents/` holds the tracker/label/domain-doc conventions that this repo's
installed engineering skills (`triage`, `to-tickets`, `domain-modeling`,
`wayfinder`, and related flows) read before acting — issue-tracker conventions,
the triage label vocabulary, and how those skills should consume this repo's
domain docs (`CONTEXT.md` and `docs/adr/`, listed above under Public docs).
`AGENTS.md`'s "Agent skills" section links each one; edit the
`docs/agents/` file itself when the underlying convention (tracker, label
strings, doc layout) changes, not `AGENTS.md`.

Agents are authored for Claude Code, with `.codex/agents/*.toml` **generated** from
them. Skills run the opposite direction: `.agents/skills/<name>/**` is authored
(that's where third-party skill installers write), and the whole tree is
**generated** into `.claude/skills/<name>/**` for Claude Code. Session hooks remain
tool-specific: the Claude Code hook is not mirrored into `.codex/`, and the repo
does not check in project-scoped Codex config or lifecycle-hook files. For the
generated trees, `node scripts/sync-agents.mjs` (or `npm run sync:agents`)
is the one generator, and generated files must not be hand-edited — an **Agent
Config Sync** CI check runs on every PR and fails it when the generated tree has
drifted (it is one of the ruleset's required status checks, so a drifted PR
cannot merge). Edit the authored side (`.claude/agents/` or
`.agents/skills/`), re-run the script, and commit every side that changed. Never
replace a generated directory with a symlink back to its source — see the **Agent
config parity** section in `AGENTS.md` for why, and for the full mapping.

Required human actions arising from completed agent work follow
[Human follow-ups](agents/human-actions.md): private issues and board tracking paired with
step-by-step private wiki instructions. Docs-updater, ship, end-session, and wizard use that procedure.
