---
name: azure-infrastructure
description: Use for Azure deployments, GHCR image publishing, Key Vault secret and key management, Container Apps configuration for the API and the ingestion worker, and cloud infrastructure changes.
tools: Read, Write, Edit, Bash, Glob, Grep
model: inherit
---

You are managing ApexRacers cloud infrastructure. Know the deployment topology and
runtime-configuration model. Use `private/ops/azure-deployment-runbook.md` when available
for exact resource names, command targets, and maintainer-only deployment details.

## Resource topology

Use environment variables or `private/ops/azure-deployment-runbook.md` for exact resource
names. Public/tracked docs should describe the topology and command shape, not
maintainer-specific Azure resource identifiers.

| Resource type                        | Purpose                                                                  |
| ------------------------------------ | ------------------------------------------------------------------------ |
| Resource group                       | Owns the deployment resources.                                           |
| GitHub Container Registry (GHCR)     | Public `apexracers-api` and `apexracers-ingestion` packages; not Azure.  |
| Key Vault                            | Stores runtime secrets and the key that wraps the Data Protection ring.  |
| PostgreSQL Flexible Server           | Application database (also holds the Data Protection key ring).          |
| Container Apps Environment           | Hosts both container apps.                                               |
| Container App (API)                  | Runs the API container and React SPA; scales 0–1 replicas.               |
| Container App (ingestion)            | Runs the ingestion worker.                                               |
| Log Analytics / Application Insights | Container console logs; the API's in-process log (and opt-in span) export. |
| Custom domain / certificate          | Public HTTPS endpoint.                                                   |

## Key Vault secrets

Secret names use **hyphens** in Key Vault. `HyphenToUnderscoreSecretManager` in both `Program.cs` files maps them to the underscore names the app reads via `IConfiguration`.

| Key Vault name               | App env var                  | Used by                    |
| ---------------------------- | ---------------------------- | -------------------------- |
| `JWT-SIGNING-KEY`            | `JWT_SIGNING_KEY`            | Api                        |
| `DATABASE-CONNECTION-STRING` | `DATABASE_CONNECTION_STRING` | Api, Ingestion             |
| `ADMIN-SEED-EMAILS`          | `ADMIN_SEED_EMAILS`          | Api (startup role seeding) |
| `IRACING-USERNAME`           | `IRACING_USERNAME`           | Ingestion                  |
| `IRACING-PASSWORD`           | `IRACING_PASSWORD`           | Ingestion                  |
| `IRACING-CLIENT-ID`          | `IRACING_CLIENT_ID`          | Ingestion                  |
| `IRACING-CLIENT-SECRET`      | `IRACING_CLIENT_SECRET`      | Ingestion                  |

`JWT-SIGNING-KEY` must be **at least 32 bytes** (`JwtSettings.MinimumSigningKeyBytes`): the API
signs with HMAC-SHA256 and refuses to start on a shorter key. Verify a vault's value before
rolling out a release that enforces it — `az keyvault secret show --vault-name <vault> --name
JWT-SIGNING-KEY --query "length(value)" -o tsv` reports the character count, which is a lower
bound on the byte count. Generate a replacement with `openssl rand -base64 48`; rotating it
invalidates every outstanding access token.

`AZURE_KEY_VAULT_URL` env var triggers Key Vault config in both apps. Set this on the
API app and ingestion worker; it is not a Key Vault secret itself.

Authentication to Key Vault uses `DefaultAzureCredential`. The API app and ingestion
worker must have managed identities with permission to read secrets from the vault.

## Data Protection key ring (API)

Identity's email-confirmation and password-reset links are protected by the ASP.NET Core Data
Protection key ring. A container app loses its filesystem whenever its replica is replaced (a
restart, a deploy, platform maintenance), so the API persists the ring in PostgreSQL (`identity.DataProtectionKeys`) under the fixed application
name `ApexRacers` (`DataProtectionRegistration`). Nothing about it depends on the container's `/home`
or any mounted storage.

Because a database copy (backup, restore, leaked dump) would otherwise hold both the ring and every
User's `SecurityStamp` — enough to forge reset links — a host that reads Key Vault must wrap the
ring with a Key Vault key:

- `DATA_PROTECTION_KEY_ID` is **required** wherever `AZURE_KEY_VAULT_URL` is set; the API refuses
  to start without it, or with a value that is not an absolute https key identifier. Like
  `AZURE_KEY_VAULT_URL` it is not secret: an app setting on the API container app (or a vault
  secret `DATA-PROTECTION-KEY-ID`), never a value checked into the repository.
- Use the **versionless** key identifier (`https://<vault>.vault.azure.net/keys/<name>`), so rotating
  the key needs no configuration change. Each ring entry records the key version that wrapped it,
  so keep earlier versions enabled: disabling or purging one makes the entries it wrapped unreadable.
- The API's managed identity needs key permissions `get`, `wrapKey` and `unwrapKey` on that key (under
  Azure RBAC, the Key Vault Crypto Service Encryption User role) in addition to reading secrets.

Local Compose and CI read no Key Vault and keep the ring unencrypted. Changing the application name
or deleting `DataProtectionKeys` rows invalidates every outstanding link.

## The container runs as UID 1654

Both runtime images run as the non-root `app` user (UID 1654) since GHSA-4whp-7hv6-jvv6, which moves
`$HOME` from `/root` to `/home/app`. The API writes nothing under it at runtime (the Data Protection
ring is in the database), so the container needs no persistent volume.

## Container registry (GHCR)

Images live in public GitHub Container Registry packages under the repository owner
(`ghcr.io/<owner>/apexracers-api`, `ghcr.io/<owner>/apexracers-ingestion`). `deploy.yml` logs in to
`ghcr.io` with the job's own `GITHUB_TOKEN` (`permissions: packages: write`) and labels each image
with `org.opencontainers.image.source` so the package links back to this repository. The packages are
public — they hold only compiled public source and the built SPA, and every secret arrives from Key
Vault at runtime — so the container apps pull them anonymously, with no registry credential to rotate.
Keep both properties: never bake a secret into an image, and do not make a package private without
first giving the container apps a registry credential.

## Container App deployment (API + React SPA)

The API Docker image bundles the React frontend in `wwwroot`. `deploy.yml`'s `deploy-api` job builds
and pushes it, then updates the API container app (named by the `API_CONTAINER_APP_NAME` secret):

```bash
docker build --label "org.opencontainers.image.source=https://github.com/<owner>/<repo>" \
  -t "$REGISTRY/apexracers-api:$IMAGE_TAG" .
docker push "$REGISTRY/apexracers-api:$IMAGE_TAG"

az containerapp update \
  --name "$API_CONTAINER_APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --image "$REGISTRY/apexracers-api:$IMAGE_TAG" \
  --revision-suffix "deploy-$DEPLOY_RUN_ID-$DEPLOY_ATTEMPT"
```

The API container app runs in single-revision mode: the new revision takes traffic once ready and the
old one is deactivated. Its startup and liveness probes target `/healthz` (no dependency checks) and
its readiness probe `/ready` (database reachable), so a database outage holds traffic back without
restarting the replica. The image listens on port 8080, which is the ingress target port.

Migrations run automatically at API startup via `db.Database.MigrateAsync()` — no separate migration
step needed. That assumes one writer, so **max replicas must stay 1**.

**Min replicas is 0: the API scales to zero, and anything that polls it defeats that.** The HTTP
scaler removes the replica after 300 seconds without a request. Any request wakes it: an uptime
monitor, an open browser tab polling the API, or scanners. A replica running above its minimum also
bills at the *active* rate, so a min-0 app kept awake costs more than a min-1 app sitting idle.

What happened on 2026-11-16 is worth knowing before changing this:
- The app never reached zero, and that was blamed on scanners, so the minimum went to 1.
- The real cause was an external uptime monitor checking the home page from several regions about
  every 3 minutes. Scanner traffic was only a burst after the domain moved (certificate-transparency
  discovery), then quiet.
- Min 1 measured about $8.70/month. A replica held by its minimum bills at the much lower *idle*
  rate only while under 0.01 vCPU, and the API was under that for only about half the minutes.
- With the monitor paused, the minimum went back to 0. The free grant then covers about 200 awake
  hours a month at 0.25 vCPU / 0.5 GiB.

Rules that follow from it:
- If uptime monitoring comes back, use one region at a 30-minute or longer interval, with a timeout
  of 30 seconds or more. A cold start takes about 23 seconds from scheduling to `Application
  started`.
- Before raising the minimum, find what keeps the replica awake. Split the request log by client
  address, and pass `--offset` to `az monitor app-insights query`, whose default window is one hour.
- Peak working set measured 416 MiB, close to the 0.5 GiB limit.

A single-revision deploy can
briefly overlap the outgoing and incoming revisions, so a release whose migration needs stopped
writers still goes through a manual cutover.

## Container App deployment (ingestion worker)

```bash
# Build and push ingestion image (deploy.yml logs in to ghcr.io first)
docker build --label "org.opencontainers.image.source=https://github.com/<owner>/<repo>" \
  -t "$REGISTRY/apexracers-ingestion:$IMAGE_TAG" -f ingestion.Dockerfile .
docker push "$REGISTRY/apexracers-ingestion:$IMAGE_TAG"

# Update the image and restore an observed pause. Use a unique numeric run/attempt suffix.
IMAGE="$REGISTRY/apexracers-ingestion:$IMAGE_TAG" \
REVISION_SUFFIX="deploy-$DEPLOY_RUN_ID-$DEPLOY_ATTEMPT" \
node scripts/deploy-ingestion.mjs
```

Image updates and pause/enable changes must follow docs/ingestion-deployment.md. The helper
requires INGESTION_APP_NAME, RESOURCE_GROUP and AZURE_SUBSCRIPTION_ID in the environment;
private/ops/azure-deployment-runbook.md supplies the operator procedure when installed.
Verify the final complete revision inventory, including the requested image and all baseline
identities. Azure can transiently activate workers before deactivation, so independent acquisition
fences remain necessary. Serialize operator changes with the deployment; reconcile failed or
interrupted runs before claiming the pause is restored.

## Deploy authentication and workflow supply chain

`deploy.yml`'s API and ingestion deploy jobs hold `permissions: id-token: write` and exchange that
token for an Azure session via OIDC — no stored client secret or registry password. Every `uses:`
step across `.github/workflows/` is pinned to a full commit SHA with a trailing `# vX.Y.Z` comment
rather than a mutable tag (GHSA-j6j2-8f7p-qv9r): a retagged or compromised third-party action would
otherwise run inside the same job that mints that Azure token. `.github/dependabot.yml`'s
`github-actions` ecosystem keeps the pins current; see the `code-reviewer` agent for the review rule.

An OIDC federated credential is **subject-matched**. Both deploy jobs declare
`environment: production`, so the subject GitHub presents is
`repo:<owner>/<repo>:environment:production` — the environment, not the ref.

Two rules govern that scoping, and neither is optional:

- **The environment must carry a deployment branch policy allowlisting only `main`.** The credential
  matches the environment name and ignores the ref, so an environment with no branch policy lets a
  workflow job on *any* branch declare it and mint the Azure token. Scoping to an environment without
  that policy widens the deploy surface instead of narrowing it. Note the policy must be an explicit
  custom branch allowlist: this repository protects `main` with a **ruleset**, not classic branch
  protection, so the environment's "protected branches" option has nothing to match on.
- **Add the subject to the credential before the workflow names the environment**, never after, or the
  deploy jobs lose Azure authentication mid-migration. Retire a superseded subject only once a deploy
  on the new one has gone green; until then it is the rollback path, paired with dropping the
  `environment:` lines from `deploy.yml`.

Be precise about what the scoping buys. The ref-scoped subject it replaced was matched by *any* job in
*any* workflow running with `id-token: write` on `main`, whether or not it had anything to do with
deploying; a job must now deliberately declare the environment to attempt the match. It does **not**
defend against someone who can already land a commit on `main` — they would add the `environment:` line
too. Closing that gap needs a protection rule (required reviewers, or a wait timer) on the environment;
none is configured, and enabling one is a maintainer decision because it makes every deploy block on a
human.

Exact federated-credential and subscription identifiers stay in
`private/ops/azure-deployment-runbook.md`.

## Forwarded-header trust (`ASPNETCORE_FORWARDEDHEADERS_ENABLED`)

`ASPNETCORE_FORWARDEDHEADERS_ENABLED` must be `true` as an environment variable on the API container
app — not a Key Vault secret, so it isn't in the table above. It is what registers ASP.NET Core's
forwarded-headers middleware at all; the trust settings it then applies (which headers, `ForwardLimit
= 1`, cleared `KnownNetworks`/`KnownProxies`) are owned in code by `ForwardedHeadersPolicy` (see the
`dotnet-api` agent — never add `app.UseForwardedHeaders()` alongside this setting). With it `true`,
per-IP rate limiting and HSTS's HTTPS check see the real client behind the platform front end. The
one accepted exception is the client address (not the scheme) of a request that wakes the API from
zero replicas; `ForwardedHeadersPolicy` owns what it is and why it is accepted. That request takes a
separate route to the app, so a forged-address probe of this setting must include a request sent
while the app is at zero replicas as well as a warm one.

Removing or unsetting it is **safe but degrading, never spoofable**: forwarded headers stop being
processed, `Connection.RemoteIpAddress` reverts to the front end's own address for every request, so
per-IP rate limiting collapses onto one shared partition (over-restrictive) rather than opening any
enumeration or bypass. The API logs a `LogWarning` at startup identifying exactly this state, so a
dropped setting is diagnosable from the first restart's logs rather than from a rate-limit anomaly
days later.

## CORS

CORS is configured for Development only (`ViteDev` policy allowing `http://localhost:5173`). In production the React build is served from the same origin as the API (`wwwroot`), so no CORS headers are needed or set. Do not add CORS configuration for production.

## ADMIN_SEED_EMAILS

Comma-separated list in Key Vault (`ADMIN-SEED-EMAILS`). For startup promotion eligibility,
see `AdminSeedService` in `src/AGENTS.md`. This is the only way to create the first
admin; there is no admin bootstrap API. Changes take effect on next API restart or
deployment.

## Database connection

Azure PostgreSQL Flexible Server requires SSL. The database connection secret must
include `Ssl Mode=Require` or `VerifyFull`. Do not commit full connection strings;
keep exact hosts, users, and credentials in the private deployment runbook or the
environment-specific secret store.

## Logging

Both the API and ingestion worker write structured logs. View via:

```bash
az monitor log-analytics query \
  --workspace "$LOG_ANALYTICS_WORKSPACE" \
  --analytics-query "ContainerAppConsoleLogs_CL | order by TimeGenerated desc | limit 50"
```

The API exports to Application Insights in process (OpenTelemetry with the Azure Monitor exporter)
when `APPLICATIONINSIGHTS_CONNECTION_STRING` is configured; unset, it exports nothing, and no
platform agent collects telemetry for it.
App logs always export — including `RequestLoggingMiddleware`'s one line per request, which is the
request log. Automatic request, outgoing-HTTP and database spans export only while an operator
raises the matching log category to `Debug`, e.g. `Logging__LogLevel__Npgsql=Debug` (categories
`Microsoft.AspNetCore`, `System.Net.Http`, `Npgsql`; raising `Default` never turns them on — see
`SpanExportSources`). Those spans carry raw paths, upstream URLs and SQL text, so lower the category
again after the investigation. Health probe requests are never exported. Keep a daily data cap as a
cost guardrail. Check usage against the cap:

```bash
az monitor app-insights component quotastatus show \
  --resource-group "$RESOURCE_GROUP" \
  --resource-name "$APP_INSIGHTS_NAME"
```

Exact resource names and one-off provisioning commands belong in
`private/ops/azure-deployment-runbook.md`, not tracked agent docs.

## Verifying production

Claims about the deployed environment can usually be tested rather than reasoned about.

- **The request log answers "what did production actually do".** `RequestLoggingMiddleware` writes
  one `traces` line per request: `HTTP GET <path> responded <status> in <ms> from <ip>`. Query it with
  `az monitor app-insights query --app <appId> --analytics-query "traces | where timestamp > ago(15m) | …"`.
  Ingestion lags about two minutes. Pass `--offset` or `--start-time`: the default window is one
  hour, and it silently caps a larger `ago(…)`.
- **Don't correlate a probe by a marker in its URL.** The SPA fallback logs any unmatched path as
  `/index.html`, so the marker never appears and telemetry looks broken. Correlate on the client
  address, or use a path that really routes.
- **The logs can't confirm a migration.** EF Core's migration messages sit below the production log
  level. Confirm the running revision's image tag equals the merge SHA
  (`az containerapp show … --query "properties.template.containers[0].image"`). Since the API runs
  `MigrateAsync()` before serving, that proves the migration applied. Healthy probes alone don't,
  because a failed new revision can leave the old one serving.
- **Request-log retention is set per table.** The workspace default (30 days) isn't the governing
  value. The `AppRequests` / `AppTraces` / `AppExceptions` / `AppDependencies` tables hold the client
  addresses, and the public Privacy Policy promises their retention (90 days). Check with
  `az monitor log-analytics workspace table list`, and change the policy in the same change as any
  retention edit.
- **A refused write can still partly apply.** Enabling point-in-time restore on the immutable journal
  storage account was refused, but the blob soft delete it enabled as a prerequisite stayed on.
  Snapshot the relevant properties before any write that may be refused, and read them back
  afterwards, whether or not it succeeded.

Exact resource names for these commands are in `private/ops/azure-deployment-runbook.md`.
