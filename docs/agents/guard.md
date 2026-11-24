# Guard

The commands and paths `scripts/guard.mjs` stops in this repository, on top of the rules it applies
everywhere. Each heading is a setting defined in `.agents/skills/open-pr/settings/guard.md`.

## Deny commands

- `^(docker compose|docker-compose)\b.*\sdown\b.*\s(-[a-zA-Z]*v[a-zA-Z]*|--volumes)\b` — `docker compose down -v`
  deletes the seeded Postgres volume. Stop the stack with `docker compose stop` (ask first), or
  clear seed data with the SQL scripts in `src/ApexRacers.Data/Seeds/`.
- `^((npx|bunx|npm exec|pnpm (dlx|exec))( -y| --yes)? )?skills(@\S+)? update\b` — `npx skills update`
  leaves `.claude/skills/` linked to its source instead of copied. Re-run the `npx skills add`
  commands in `AGENTS.md`, then `node scripts/sync-agents.mjs`.
- `^((npx|pnpm exec) )?playwright test\b.*\s(--update-snapshots|-u)\b` — a local
  `--update-snapshots` writes baselines CI never reads and leaves the Linux ones stale. Refresh them
  with `e2e.yml`'s `workflow_dispatch` (`update_snapshots=true`) and commit the `visual-baselines`
  artifact.
- `^npm (run|run-script) test:e2e\S*\s.*\s(--update-snapshots|-u)\b` — the same snapshot update
  through the npm script. Refresh baselines with `e2e.yml`'s `workflow_dispatch` instead.
- `^node\b.*(^|\s|[\\/])deploy-ingestion\.mjs\b` — `scripts/deploy-ingestion.mjs` updates the
  production ingestion container app, and only the deploy workflow runs it. Test it with
  `node --test scripts/deploy-ingestion.test.mjs`.

## Ask commands

- `^(docker compose|docker-compose)\b.*\s(stop|down|kill)\b` — a running local Postgres may be
  deliberate; stop the stack only on the user's word.
- `^docker (volume (rm|prune)|system prune\b.*\s--volumes)\b` — removing or pruning Docker volumes
  can delete the seeded Postgres volume.

## Protected paths

- `^private/` — the companion repository, including `private/iracing-api-response-objects/`, the
  captured iRacing payload shapes, which cannot be fetched again without credentials.
- `^\.env$` — local secrets, not regenerable from the repository; `.env.example` is the template.
- `(^|/)[^/]*\.secrets\.json$` — local secrets, not regenerable from the repository.
- `^\.claude/settings\.local\.json$` — the contributor's own Claude Code settings.
- `^docker-compose\.override\.yml$` — the contributor's local Compose overrides.
