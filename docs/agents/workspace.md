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

## Worktrees

- **Use `origin/main` as the base, after `git fetch origin`.** In a linked worktree, local `main` is
  held by another checkout and is often behind. `git merge-base main HEAD` then returns an old base
  and silently widens the branch diff to already-merged work, which `/open-pr` reads for the release
  decision. Tell any subagent computing a diff to do the same.
- **A persisted working directory retargets later commands.** After a `cd` into another worktree or
  into `private/`, later `git` and `gh` calls act on that checkout. An unexpectedly empty `gh` result
  is the tell.

## Local runs

- **Stopping a local API:** Ctrl+C to `dotnet run` can end the launcher but leave `ApexRacers.Api.exe`
  running, and the next build fails with MSB3026 (apphost locked). Stop only the PID that owns that
  exact port, after checking its path is this worktree's build. Other worktrees may be running.
- **Full .NET suite runs** take 25–35 minutes on Windows, and the Docker VM grows towards its memory
  cap. Run them in an ordinary terminal, not an agent's background shell, which can be killed under
  memory pressure. Check `docker version` first: a run with Docker unavailable finishes in seconds and
  can still look complete. Single-class runs (`--filter-class`) are fine in-session.
