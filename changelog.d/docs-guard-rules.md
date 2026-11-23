### Added

- `docs/agents/guard.md` holds this repository's own rules for the shared guard hook
  (`scripts/guard.mjs`), on top of the rules it applies everywhere. It denies
  `docker compose down -v` (which deletes the seeded Postgres volume), `npx skills update`, a local
  Playwright `--update-snapshots` run (directly or through the `test:e2e` npm scripts) and running
  `scripts/deploy-ingestion.mjs` by hand. It asks before stopping the Compose stack or removing or
  pruning Docker volumes, and protects `private/`, `.env`, `*.secrets.json`,
  `.claude/settings.local.json` and `docker-compose.override.yml`. The docs map lists the new file.
  No product behavior changes.
