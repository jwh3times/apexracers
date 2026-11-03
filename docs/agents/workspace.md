# Workspace

## Private companion

`private/` — its own Git repository, `jwh3times/apexracers-private`, ignored by this one. Optional:
its absence is a valid public-contributor state.

## Workspace status command

`node scripts/repository-status.mjs` (also `npm run repo:status`) — branch, dirty state, upstream
and divergence for this repository and, when installed, the companion.

## Safe-to-remove paths

- `coverage*/`, `coverage-report/`, `TestResults/` (repo root)
- `web/coverage/`, `web/dist/`, `web/test-results/`, `web/playwright-report/`, `web/blob-report/`

## Never-remove paths

- `private/`, in any form — especially `private/iracing-api-response-objects/`, the captured
  iRacing payload shapes, which cannot be fetched again without credentials
- `.env`, `*.secrets.json`
- `.claude/settings.local.json`
- `docker-compose.override.yml`

## Dev stack

`docker compose ps` shows the local stack; `docker compose stop` stops it. A running Postgres may be
deliberate: ask first.

- **Never run:** `docker compose down -v` — it deletes the seeded Postgres volume.

## Final portability check

`node scripts/repository-status.mjs --check`
