### Added

- `docs/agents/dependabot.md`, the settings the `dependabot` skill reads when it reviews a
  Dependabot pull request here: its review label, the installs CI runs, where each dependency is
  pinned, the stack facts a verdict must not get wrong (no PR job builds a Docker image), and the
  gates for an image bump, a PostgreSQL major, a .NET major and a build-critical npm major.
