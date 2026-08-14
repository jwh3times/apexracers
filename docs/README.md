# ApexRacers Documentation

This directory contains public, contributor-safe project documentation.

## Public docs

| File | Purpose |
| --- | --- |
| [features.md](features.md) | Product capabilities and user-facing workflows. |
| [roadmap.md](roadmap.md) | High-level project status and planned work. |
| [../README.md](../README.md) | Local setup and common development commands. |
| [../CONTRIBUTING.md](../CONTRIBUTING.md) | Contribution workflow and quality gates. |
| [../SECURITY.md](../SECURITY.md) | Vulnerability reporting policy. |
| [../CHANGELOG.md](../CHANGELOG.md) | Public release notes. |
| [../CONTEXT.md](../CONTEXT.md) | Domain-language glossary — canonical terms (e.g. Series/Season, the Race Session/Split/Subsession hierarchy, User/Driver identity, Venue/Track, Race Lap/Uploaded Lap evidence, Percentile Rank/Top Share) shared across the product and its docs. |
| [adr/](adr/) | Architecture decision records — why a structural decision was made and what alternative was rejected, not just what shipped. |

## Private docs

Maintainer-only planning, deployment runbooks, raw API samples, security audit
details, and archived implementation notes live under `private/`. That directory is
intentionally gitignored and must not be required for normal external contribution.

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

Agents and hooks are authored for Claude Code, with the Codex equivalents
(`.codex/agents/*.toml`, `.codex/hooks/*`) **generated** from them. Skills run the
opposite direction: `.agents/skills/<name>/**` is authored (that's where third-party
skill installers write), and the whole tree is **generated** into
`.claude/skills/<name>/**` for Claude Code. Either way, `node scripts/sync-agent-configs.mjs`
(or `npm run sync:agents`) is the one generator, and generated files must not be
hand-edited — an `Agent Config Sync` CI check fails a PR whose generated tree has
drifted. Edit the authored side (`.claude/agents/`, `.agents/skills/`, or
`.claude/hooks/`), re-run the script, and commit every side that changed. Never
replace a generated directory with a symlink back to its source — see the **Agent
config parity** section in `AGENTS.md` for why, and for the full mapping.
