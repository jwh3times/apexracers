---
name: azure-infrastructure
description: Use for Azure deployments, Container Registry pushes, Key Vault secret management, App Service configuration, Container Apps updates for the ingestion worker, and cloud infrastructure changes.
tools: Read, Write, Edit, Bash, Glob, Grep
model: sonnet
---

You are managing ApexRacers cloud infrastructure. Know the deployment topology and
runtime-configuration model. Use `private/ops/azure-deployment-runbook.md` when available
for exact resource names, command targets, and maintainer-only deployment details.

## Resource topology

Use environment variables or `private/ops/azure-deployment-runbook.md` for exact resource
names. Public/tracked docs should describe the topology and command shape, not
maintainer-specific Azure resource identifiers.

| Resource type                              | Purpose                                                |
| ------------------------------------------ | ------------------------------------------------------ |
| Resource group                             | Owns the deployment resources.                         |
| Container Registry                         | Stores API and ingestion images.                       |
| Key Vault                                  | Stores runtime secrets.                                |
| PostgreSQL Flexible Server                 | Application database.                                  |
| App Service Plan + App Service             | Runs the API container and React SPA.                  |
| Container Apps Environment + Container App | Runs the ingestion worker.                             |
| Log Analytics / Application Insights       | Collects logs, requests, dependencies, and exceptions. |
| Custom domain / certificate                | Public HTTPS endpoint.                                 |

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

`AZURE_KEY_VAULT_URL` env var triggers Key Vault config in both apps. Set this on the
API app and ingestion worker; it is not a Key Vault secret itself.

Authentication to Key Vault uses `DefaultAzureCredential`. The API app and ingestion
worker must have managed identities with permission to read secrets from the vault.

## App Service deployment (API + React SPA)

The API Docker image bundles the React frontend in `wwwroot`. Build and push:

```bash
# Build and push to the container registry
docker build -t "$REGISTRY/apexracers-api:$IMAGE_TAG" .
az acr login --name "$ACR_NAME"
docker push "$REGISTRY/apexracers-api:$IMAGE_TAG"

# Update App Service to use the new image
az webapp config container set \
  --name "$API_APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --docker-custom-image-name "$REGISTRY/apexracers-api:$IMAGE_TAG"
```

Migrations run automatically at API startup via `db.Database.MigrateAsync()` — no separate migration step needed. Safe because App Service runs as a single instance.

## Container App deployment (ingestion worker)

```bash
# Build and push ingestion image
docker build -t "$REGISTRY/apexracers-ingestion:$IMAGE_TAG" -f ingestion.Dockerfile .
docker push "$REGISTRY/apexracers-ingestion:$IMAGE_TAG"

# Update Container App
az containerapp update \
  --name "$INGESTION_APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --image "$REGISTRY/apexracers-ingestion:$IMAGE_TAG"
```

## CORS

CORS is configured for Development only (`ViteDev` policy allowing `http://localhost:5173`). In production the React build is served from the same origin as the API (`wwwroot`), so no CORS headers are needed or set. Do not add CORS configuration for production.

## ADMIN_SEED_EMAILS

Comma-separated list in Key Vault (`ADMIN-SEED-EMAILS`). For startup promotion eligibility,
see `AdminSeedService` in the project guide. This is the only way to create the first
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

Application Insights should capture requests, dependencies, exceptions, and `ILogger`
traces for the API app. Keep a daily data cap as a cost guardrail. Check usage against
the cap:

```bash
az monitor app-insights component quotastatus show \
  --resource-group "$RESOURCE_GROUP" \
  --resource-name "$APP_INSIGHTS_NAME"
```

Exact resource names and one-off provisioning commands belong in
`private/ops/azure-deployment-runbook.md`, not tracked agent docs.
