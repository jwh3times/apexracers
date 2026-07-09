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

## Private docs

Maintainer-only planning, deployment runbooks, raw API samples, security audit
details, and archived implementation notes live under `private/`. That directory is
intentionally gitignored and must not be required for normal external contribution.

## Agent docs

`AGENTS.md`, `CLAUDE.md`, and `.claude/agents/` contain coding-agent guidance. Keep
them focused on repo behavior and implementation conventions. Do not add secrets,
live credentials, personal account data, or private deployment runbooks to tracked
agent docs.
