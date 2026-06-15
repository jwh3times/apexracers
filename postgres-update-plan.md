# PostgreSQL 16 → 18 Migration Plan

> **Status:** Planning — not yet executed.
> **Trigger:** Dependabot PR bumping the `docker-compose.yml` Postgres image from `postgres:16-alpine` to `postgres:18-alpine`.
> **Goal:** Move both environments from PostgreSQL 16 to 18 — the **local Docker dev** database *and* the **Azure production** database — keeping dev/prod version parity.

This document is written so a subagent (or a future you) can pick it up cold and execute every step. Read the **Critical Background** section first — the single most important fact is that a Postgres **major** version bump is **not** a drop-in image swap: the on-disk data format is incompatible between major versions, so the data must be either discarded and rebuilt (dev) or formally upgraded (prod).

---

## 0. Critical background (read before doing anything)

### What actually changes

| Environment | Postgres host | Tracked by Dependabot? | Upgrade mechanism |
| --- | --- | --- | --- |
| **Local dev** | `postgres:16-alpine` container in `docker-compose.yml`, data in named volume `postgres_data` | ✅ Yes (the PR that triggered this) | Swap image tag + rebuild data dir (volume is incompatible across majors) |
| **Production** | `apexracers-pg` — Azure Database for PostgreSQL **Flexible Server** (resource group `apexracers-rg`, region `westus3`) | ❌ **No** — managed Azure resource, Dependabot never sees it | Azure in-place **Major Version Upgrade (MVU)** — a separate, manual, irreversible operation |

**Key implication:** Merging the Dependabot PR only updates *local dev*. It has **zero effect** on production. The Azure database stays on its current version until someone manually runs the upgrade in Part B. Do **not** assume merging the PR "ships" the upgrade.

### Why this is not a simple tag bump

- PostgreSQL stores data in an on-disk format that is **incompatible between major versions** (16 → 18). If you just change the image tag and restart, Postgres 18 will refuse to start against a 16 data directory with:
  `FATAL: database files are incompatible with server. The data directory was initialized by PostgreSQL version 16, which is not compatible with this version 18.x`
- Major upgrades therefore require either: (a) dump → fresh init → restore (logical), or (b) `pg_upgrade` (in-place binary). Azure Flexible Server wraps `pg_upgrade` behind its MVU feature.

### Compatibility notes for this stack

- **Npgsql / EF Core:** `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2` (see `Directory.Packages.props`) is fully compatible with PostgreSQL 18. No code change required. There is no `SetPostgresVersion(...)` pin in any `UseNpgsql(...)` call (verified in `Program.cs` for Api/Ingestion/Seeder and `DesignTimeDbContextFactory`), so Npgsql negotiates the server version at runtime — nothing to update there.
- **EF Core migrations:** schema is reproducible from `src/ApexRacers.Data/Migrations/`. Re-running `dotnet ef database update` against a fresh PG18 database recreates the full schema. This is what makes the dev "wipe and rebuild" path safe.
- **CI:** the `test` job in `.github/workflows/deploy.yml` does **not** spin up a Postgres service container (tests use EF in-memory/mocked contexts), so **no CI change is required**. Confirm this is still true before finishing (search the workflow for `services:` / `postgres`).
- **pgAdmin:** `dpage/pgadmin4:latest` connects fine to PG18 — no change needed.

### PG18-specific gotchas to account for

1. **⚠️ Docker image `PGDATA` / mount-point change (the most important footgun).** The official Postgres 18 image **moved the default data directory to a version-namespaced subdirectory**. Verified against the actual images:

   | Image | Default `PGDATA` | Recommended volume mount |
   | --- | --- | --- |
   | `postgres:16-alpine` | `/var/lib/postgresql/data` | `… :/var/lib/postgresql/data` |
   | `postgres:18-alpine` | `/var/lib/postgresql/18/docker` | `… :/var/lib/postgresql` (parent) |

   (Reproduce with `docker run --rm postgres:18-alpine printenv PGDATA` → `/var/lib/postgresql/18/docker`.)

   **Consequence if you only bump the tag and leave the existing mount as-is:** the volume stays mounted at `/var/lib/postgresql/data`, but PG18 now writes its cluster to `/var/lib/postgresql/18/docker` — a path **inside the container, outside the volume**. The DB will appear to work, but **all data is ephemeral** and is lost on every `docker compose down`/container recreate. There is no error; it just silently stops persisting. The volume mount **must** be updated alongside the tag. See Part A, Step A3 for the two valid fixes.
2. **`data_checksums`.** PG18 enables data checksums by default on fresh `initdb`. This only affects newly initialized clusters (our dev rebuild) and is harmless/beneficial. No action needed.
3. **Azure target-version availability.** PostgreSQL 18 went GA upstream in late 2025; Azure Flexible Server adds new major versions on a lag. **You must verify Azure actually offers 18 as a supported version/upgrade target before committing to Part B** (Step B1). If 18 is not yet available on Azure, see the divergence decision in Part B, Step B1.

---

## 1. Scope & success criteria

**In scope**
- `docker-compose.yml` image tag + `PGDATA` hardening.
- Local dev data rebuild (migrations + seed).
- Azure `apexracers-pg` major version upgrade (or a documented decision to defer it).
- Documentation/agent-file updates that hardcode "16".

**Out of scope**
- Schema changes / new EF migrations (none required).
- Application code changes (none required).

**Success criteria**
- [ ] `docker compose up -d` brings up a healthy `postgres:18-alpine` container (healthcheck passing).
- [ ] `dotnet ef database update` succeeds against the new local DB; app + seeder run clean.
- [ ] `SELECT version();` on local returns 18.x.
- [ ] Azure `apexracers-pg` reports major version 18 (`az postgres flexible-server show`), **or** a tracking note records why it was deferred.
- [ ] API in Azure (App Service `apexracers-api`) and the ingestion Container App both connect and function post-upgrade.
- [ ] All repo references to Postgres "16" updated to "18".
- [ ] `dotnet build` and both test suites pass; CI green on the PR.

---

## Part A — Local Docker dev migration

> Two paths. **Path A1 (wipe & rebuild)** is recommended — local data is synthetic seed/migration output and fully reproducible, so a clean re-init is the lowest-risk option. **Path A2 (dump & restore)** is only needed if there is local data worth keeping (e.g. manually entered test accounts you don't want to recreate).

### Step A0 — Pre-flight

```bash
# From repo root. Confirm current state.
docker compose ps
docker compose exec postgres psql -U apexracers -d apexracers -c "SELECT version();"   # expect 16.x
```

If you want a throwaway safety copy even on the wipe path, run the dump from Step A2.1 first and keep the file.

### Path A1 — Wipe & rebuild (RECOMMENDED for dev)

**A1.1 — Stop the stack and remove the Postgres volume**
```bash
# -v removes named volumes, including postgres_data (this deletes local DB data).
docker compose down -v
```
> If you have other named volumes you want to keep, instead remove only the Postgres volume:
> `docker compose down` then `docker volume rm apexracers_postgres_data` (confirm the exact name with `docker volume ls`).

**A1.2 — Update the image tag + harden PGDATA** — see Part A, Step A3 (apply the `docker-compose.yml` edits now).

**A1.3 — Bring up the new database**
```bash
docker compose up -d postgres
# Wait for healthy:
docker compose ps
docker compose exec postgres pg_isready -U apexracers
```

**A1.4 — Apply migrations**
```bash
dotnet ef database update --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
```

**A1.5 — Reseed (optional but expected for a usable UI)**
```bash
# Requires iracing-api-response-objects/ populated (gitignored — see README §6).
dotnet run --project src/ApexRacers.Seeder
```

**A1.6 — Verify** — see Part A, Step A4.

### Path A2 — Dump & restore (preserve local data)

**A2.1 — Dump from the running PG16 container** (PG16 client dumping PG16 server — safe)
```bash
# Plain-SQL logical dump of the single app database.
docker compose exec -T postgres pg_dump -U apexracers -d apexracers --no-owner --no-privileges > backup_pg16.sql
```
> Roles/globals are recreated automatically by the new container from `POSTGRES_USER`/`POSTGRES_DB` env vars, so a single-db `pg_dump` is sufficient — no `pg_dumpall` needed.

**A2.2 — Tear down and remove the volume**
```bash
docker compose down -v
```

**A2.3 — Update image tag + PGDATA** — Part A, Step A3.

**A2.4 — Start fresh PG18 (auto-creates `apexracers` db + user)**
```bash
docker compose up -d postgres
docker compose exec postgres pg_isready -U apexracers
```

**A2.5 — Restore the dump (PG18 server accepts a forward-compatible 16 logical dump)**
```bash
cat backup_pg16.sql | docker compose exec -T postgres psql -U apexracers -d apexracers
```

**A2.6 — Reconcile EF migration history**
The restored dump already contains the schema **and** the `iracing.__EFMigrationsHistory` table, so the database is at the latest migration. Confirm with:
```bash
dotnet ef migrations list --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
# All migrations should show as applied; running `database update` again should be a no-op.
```

**A2.7 — Verify** — Part A, Step A4.

### Step A3 — `docker-compose.yml` edits (both paths)

In `docker-compose.yml`, the `postgres` service:

1. Change the image tag:
   ```yaml
   # before
   image: postgres:16-alpine
   # after
   image: postgres:18-alpine
   ```
2. **Fix the volume mount for PG18's new data-dir location** (see PG18 gotcha #1 — this is mandatory, not optional). Pick **one** of the two valid approaches:

   **Option A (recommended) — follow the new PG18 convention: mount the parent directory.** Let the image keep its default `PGDATA=/var/lib/postgresql/18/docker` and move the mount up one level so the versioned subdir is covered:
   ```yaml
   # postgres service
   image: postgres:18-alpine
   environment:
     POSTGRES_DB: apexracers
     POSTGRES_USER: apexracers
     POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-devpassword}
   volumes:
     - postgres_data:/var/lib/postgresql        # was: /var/lib/postgresql/data
   ```
   This is what the image maintainers recommend for 18, and it makes future major upgrades cleaner (each major lives in its own `…/<major>/docker` subdir under the one mount).

   **Option B — pin `PGDATA` explicitly to a path under the current mount.** Keeps the mount path identical to today by overriding the image default:
   ```yaml
   # postgres service
   image: postgres:18-alpine
   environment:
     POSTGRES_DB: apexracers
     POSTGRES_USER: apexracers
     POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-devpassword}
     PGDATA: /var/lib/postgresql/data/pgdata    # override 18's default; subdir of the mount
   volumes:
     - postgres_data:/var/lib/postgresql/data
   ```
   The official image requires `PGDATA` to be a **subdirectory** of the mount (here `…/data/pgdata`), not the mount root, when you pin it this way.

   > Either way, because Path A1/A2 start from an **empty** volume (`down -v`), the new layout initializes cleanly — there is no in-volume data to relocate. **Do not** mix the old mount (`/var/lib/postgresql/data`) with the unmodified PG18 default `PGDATA` — that is the exact silent-data-loss trap from gotcha #1.
   >
   > **Note on the named volume:** if the existing `postgres_data` volume previously held PG16 files at `/var/lib/postgresql/data`, those files become irrelevant after `down -v` removes the volume. If for any reason you did *not* remove the volume, switching to Option A's parent mount would expose the old `data/` dir as a stray sibling of `18/` — clean-rebuild (`down -v`) avoids this entirely.
3. Leave the healthcheck (`pg_isready -U apexracers`), ports, and `depends_on` untouched.

> **Dependabot PR reconciliation:** The Dependabot PR only changes the image tag line. Either (a) check out that branch and add the `PGDATA` hardening on top before merging, or (b) close the Dependabot PR and make all edits on your own branch. Recommended: option (a) so the dependency-update history stays intact.

### Step A4 — Local verification checklist

```bash
docker compose exec postgres psql -U apexracers -d apexracers -c "SELECT version();"        # expect 18.x
docker compose exec postgres psql -U apexracers -d apexracers -c "\dn"                       # schemas: iracing, identity, public
docker compose exec postgres psql -U apexracers -d apexracers -c "SELECT count(*) FROM iracing.\"__EFMigrationsHistory\";"
dotnet build
dotnet run --project src/ApexRacers.Api      # hits DB on startup; Swagger at http://localhost:5000/swagger
```
- [ ] Container healthy, `version()` = 18.x
- [ ] Migrations applied; app starts and serves requests
- [ ] (If seeded) series/cars/laps visible in the UI via `npm run dev`

---

## Part B — Azure production migration (`apexracers-pg`)

> This is the **higher-risk, irreversible** half. Azure Flexible Server MVU runs `pg_upgrade` in place and **cannot be rolled back** except by restoring from a pre-upgrade backup/snapshot. Do **not** skip the backup and pre-checks.
>
> Prereqs: `az login` with an account that has Contributor on `apexracers-rg`. (User runs interactive auth via `! az login` if needed — a subagent cannot complete interactive browser auth.)

### Step B1 — Verify current version AND that 18 is a supported target

```bash
# Current engine version + key metadata. Note ha.mode and replica.role —
# these are MVU blockers (see preconditions below).
az postgres flexible-server show \
  --resource-group apexracers-rg \
  --name apexracers-pg \
  --query "{version:version, sku:sku.name, tier:sku.tier, storageGB:storage.storageSizeGb, ha:highAvailability.mode, replicaRole:replica.role, state:state, location:location}" -o table

# Is 18 an available major version / upgrade target?
# NOTE: `list-server-versions` does NOT exist on the current az CLI. Use list-skus,
# which exposes supportedVersionsToUpgrade per current major (VERIFIED working):
az postgres flexible-server list-skus --location westus3 \
  --query "[0].supportedServerVersions[].{version:name, upgradeTargets:supportedVersionsToUpgrade}" -o json
# Confirm the current major (16) lists 18 in its upgradeTargets. (Verified 2026-06-15: 16 → [17, 18].)
```

**MVU preconditions to clear before upgrading (Azure will block the upgrade otherwise):**
- **High availability:** if `ha` (`highAvailability.mode`) is `ZoneRedundant` or `SameZone`, MVU is blocked. Disable HA before the upgrade and re-enable it after Step B4:
  ```bash
  az postgres flexible-server update --resource-group apexracers-rg --name apexracers-pg --high-availability Disabled
  ```
- **Read replicas:** if `replicaRole` is non-null (this server has replicas, or is itself a replica), MVU is blocked. Replicas must be removed and recreated after the upgrade.
- **Storage headroom:** `pg_upgrade` needs free space for the new cluster. Confirm the server is not near its `storageGB` limit (check used % in the Portal Overview or metrics). If it's close to full, grow storage first — note that storage grow on Flexible Server is **irreversible** (can't shrink later):
  ```bash
  az postgres flexible-server update --resource-group apexracers-rg --name apexracers-pg --storage-size <newGB>
  ```

**Decision branch:**
- **If 18 is offered as an upgrade target →** proceed with Part B.
- **If 18 is NOT yet available on Azure Flexible Server →** do **not** force prod. Options:
  1. **(Recommended) Temporary divergence:** take local dev to 18 now (Part A), upgrade Azure to the highest currently supported version (e.g. 17) for partial parity, and add a tracking item to revisit Azure→18 when Azure GA's it. Note the dev/prod version gap in `README.md`.
  2. **Hold both at 16/17:** if strict parity is required, pin the Dependabot PR back (or target 17) until Azure supports 18.
  Record whichever decision is made in the PR description and in the doc-update step (Part C).

Also enumerate installed extensions and confirm each is supported on the target major version:
```bash
az postgres flexible-server parameter show \
  --resource-group apexracers-rg --server-name apexracers-pg \
  --name shared_preload_libraries --query "value" -o tsv
# And, connected to the DB:
#   SELECT extname, extversion FROM pg_extension;
```
If any extension is unsupported on 18, drop/disable it before upgrading (Azure pre-upgrade validation will also flag this).

> **This app declares zero custom Postgres extensions** — verified: no `HasPostgresExtension` in any entity configuration, no `CREATE EXTENSION` in any migration, and none of postgis/citext/pg_trgm/uuid-ossp anywhere in the repo. Extension incompatibility is the most common cause of MVU failure, so this check should come back trivial (only Azure's built-in defaults, if any). Don't over-index on it — but still run the query to confirm nothing was added out-of-band on the server.

### Step B2 — Backup / safety net (mandatory)

Azure Flexible Server keeps automated backups, but for an irreversible MVU take an explicit on-demand restore point and/or a server-level backup:

```bash
# On-demand server backup. CORRECT FLAGS: -s = server, -n = BACKUP name
# (the plan originally had --name/--backup-name, which the CLI rejects).
#
# ⚠️ VERIFIED 2026-06-15: on-demand backups are NOT allowed on Burstable-tier
# servers (apexracers-pg is Standard_B1ms / Burstable) — this errors with
# "CustomerOnDemandBackupCannotBePerformedOnBurstableServer". On Burstable,
# SKIP this and rely on the automated daily backups + PITR below (sufficient).
az postgres flexible-server backup create \
  -g apexracers-rg -s apexracers-pg -n pre-pg18-$(date +%Y%m%d) \
  || echo "on-demand backup unavailable (e.g. Burstable tier) — automated backups + PITR are the safety net."

# List existing backups (works on all tiers — confirms a recent automated full backup exists).
# NOTE: backup list uses -s/--server-name, not --name.
az postgres flexible-server backup list -g apexracers-rg -s apexracers-pg -o table
```
Also note the **earliest restore time** (for point-in-time restore) so you have a known-good timestamp:
```bash
az postgres flexible-server show \
  --resource-group apexracers-rg --name apexracers-pg \
  --query "backup.earliestRestoreDate" -o tsv
```
> **Optional but strongly recommended dry run:** point-in-time restore to a **temporary** server, run the MVU on the copy first, validate the app against it, then delete it. This proves the upgrade and app compatibility with zero risk to prod.
> ```bash
> az postgres flexible-server restore \
>   --resource-group apexracers-rg \
>   --name apexracers-pg-upgradetest \
>   --source-server apexracers-pg \
>   --restore-time <ISO8601-timestamp>
> # then run Step B3's upgrade against apexracers-pg-upgradetest, validate, and:
> # az postgres flexible-server delete --resource-group apexracers-rg --name apexracers-pg-upgradetest --yes
> ```

### Step B3 — Run the major version upgrade

> Schedule a maintenance window — MVU restarts the server and incurs **downtime** (minutes for a small DB; proportional to size). Announce/expect the API and ingestion worker to error during the window.

**First, quiesce the ingestion worker** so it isn't mid-write when the server restarts (avoids error noise and partial writes):
```bash
# Check whether a replica is actually running right now.
az containerapp revision list -g apexracers-rg -n apexracers-ingestion \
  --query "[?properties.active].{name:name, replicas:properties.replicas}" -o table
```
> ⚠️ VERIFIED 2026-06-15: `--max-replicas 0` is **rejected** ("must be in the range [1,1000]"), so you cannot scale a Container App to zero via min/max. Two cases:
> - **If `replicas` is already 0** (the worker idles at min=0/max=1 between cycles, as observed here) — nothing to do; it's already quiesced. The scale rule is unchanged, so there's nothing to restore in B4.
> - **If a replica is running** and you want a hard stop, deactivate the active revision and reactivate it after Step B4:
>   ```bash
>   az containerapp revision deactivate -g apexracers-rg -n apexracers-ingestion --revision <active-revision>
>   # …after the upgrade:
>   az containerapp revision activate   -g apexracers-rg -n apexracers-ingestion --revision <active-revision>
>   ```

```bash
# Optional: pre-upgrade validation if supported by your CLI/extension
az postgres flexible-server upgrade --help   # confirm flags (e.g. --version, any --dry-run/validate option)

# Perform the upgrade (confirm exact target version string from Step B1, e.g. 18)
az postgres flexible-server upgrade \
  --resource-group apexracers-rg \
  --name apexracers-pg \
  --version 18 \
  --yes
```
Alternatively perform via **Azure Portal → apexracers-pg → Overview → Upgrade** (runs built-in pre-upgrade checks with a clear UI).

### Step B4 — Post-upgrade verification (Azure)

```bash
az postgres flexible-server show \
  --resource-group apexracers-rg --name apexracers-pg \
  --query "{version:version, state:state}" -o table       # expect 18, Ready
```
- [ ] Server `version` = 18, `state` = Ready.
- [ ] App connectivity: hit the deployed API health/Swagger and a DB-backed endpoint (e.g. `GET /api/series`) — confirm 200s.
- [ ] Ingestion Container App (`apexracers-ingestion`) logs show successful DB access (check Log Analytics `workspace-apexracersrg0n6Q`).
- [ ] **`ANALYZE` after upgrade:** `pg_upgrade` does not carry over optimizer statistics. Connected to `apexracers`, run `ANALYZE;` (or `VACUUM ANALYZE;`) to restore query performance. Azure may do this automatically, but run it explicitly to be safe.
- [ ] Spot-check key tables row counts vs. a pre-upgrade note.
- [ ] **Restore the ingestion worker** if you stopped it in Step B3 — only needed if you deactivated the active revision (reactivate it). If the worker was already at 0 replicas (the common case here), nothing to restore; confirm it spins back up on its next cycle and reaches the DB.
- [ ] **Re-enable HA** if it was disabled in Step B1 (`--high-availability ZoneRedundant` or `SameZone`, matching the original `ha` mode), and **recreate any read replicas** that were removed.

> **No connection-string change is required:** the host/FQDN, port, db name, and credentials are unchanged by an in-place MVU, so `DATABASE-CONNECTION-STRING` in Key Vault (`apexracers-kv`) stays as-is. Confirm the App Service and Container App did **not** need restarting; if they hold stale broken connections after the server restart, restart them:
> ```bash
> az webapp restart --resource-group apexracers-rg --name apexracers-api
> az containerapp revision restart --resource-group apexracers-rg --name apexracers-ingestion --revision <active-revision>
> ```

### Step B5 — Azure rollback (only if upgrade fails or app breaks)

MVU is irreversible in place. To roll back you restore the pre-upgrade state to a **new** server, then repoint the app:
```bash
# Restore to just before the upgrade
az postgres flexible-server restore \
  --resource-group apexracers-rg \
  --name apexracers-pg-rollback \
  --source-server apexracers-pg \
  --restore-time <pre-upgrade-timestamp>
```
Then update `DATABASE-CONNECTION-STRING` in Key Vault to point at `apexracers-pg-rollback` (FQDN changes), and restart the API + ingestion. Treat this as a last resort and capture failure details for a retry.

---

## Part C — Repository & documentation updates

Update every hardcoded "16" reference found in the repo. Confirmed locations (re-grep before finishing: `rg -n "16-alpine|postgres:16|PostgreSQL 16"`):

| File | Change |
| --- | --- |
| `docker-compose.yml` | `postgres:16-alpine` → `postgres:18-alpine` **and** fix the volume mount for PG18's new data dir — mount `/var/lib/postgresql` (Option A) or pin `PGDATA` (Option B) per Part A, Step A3 |
| `.claude/agents/postgres-specialist.md` (line ~12) | "PostgreSQL 16 (`postgres:16-alpine` …)" → 18 |
| `.claude/agents/docker-containers.md` (lines ~50, ~112) | `postgres:16-alpine` / "PostgreSQL: `16-alpine`" → 18 |
| `README.md` | No version string today, but if Part B is **deferred**, add a note documenting the temporary dev(18)/prod(17 or 16) version gap |

> Use the `docs-updater` agent to sweep `CLAUDE.md`, `README.md`, and `.claude/agents/*` for any remaining "16" once the change is made.

No application code or EF migration changes are required (confirmed: no `SetPostgresVersion` pin, Npgsql 10.0.2 supports PG18).

---

## Part D — End-to-end validation & landing

1. [ ] **Local:** Part A, Step A4 checklist passes.
2. [ ] **Build/tests:**
   ```bash
   dotnet build
   dotnet test src/ApexRacers.Tests/ApexRacers.Tests.csproj
   cd src/web && npx vitest run --coverage && npx prettier --check .
   ```
3. [ ] **CI:** push the branch; confirm the `test` job is green and both deploy jobs succeed (they build images, not the Postgres container — unaffected, but confirm).
4. [ ] **Azure:** Part B, Step B4 checklist passes (or deferral documented).
5. [ ] **Docs:** Part C complete; `rg -n "16-alpine|postgres:16|PostgreSQL 16"` returns nothing stale.
6. [ ] **Commit/PR:** branch off `main`, reference the Dependabot PR, and summarize that local dev is on PG18 and the state of the Azure upgrade (done / deferred-with-reason). Do not push or merge without the user's go-ahead.

---

## Quick reference — execution order

```
PART A (local, low risk)               PART B (Azure, high risk, irreversible)
─────────────────────────              ────────────────────────────────────────
A0  pre-flight / note versions         B1  verify version + 18 avail + extensions + HA/replica/disk preconditions
A3  edit docker-compose (tag+PGDATA)   B2  backup (+ optional restore-to-temp dry run)
A1  down -v → up → migrate → seed       B3  quiesce ingestion (scale→0) → az ... flexible-server upgrade --version 18
A4  verify SELECT version() = 18        B4  verify 18 + ANALYZE + app connectivity → restore ingestion scale + HA
                                        B5  rollback path ready (restore to new server)
PART C  update docs/agent files for "16" → "18"
PART D  build, tests, CI, PR (no merge without user OK)
```

**Risk ranking:** Part A is reversible and cheap (rebuild again if needed). Part B is irreversible in place — the backup in B2 and the optional temp-server dry run are the safety net. If Azure does not yet support 18, take the documented divergence path in B1 rather than forcing prod.
