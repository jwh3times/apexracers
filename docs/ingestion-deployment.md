# Ingestion deployment and pause preservation

The ingestion deploy job in `.github/workflows/deploy.yml` uses
`scripts/deploy-ingestion.mjs`. A pause means **zero active revisions** in a complete
Container App revision inventory. An active revision with zero running replicas is still
enabled, including between scheduled runs.

Before changing the image, the helper checks the configured application identity and image,
requires completed provisioning, reads every revision with `--all`, and repeats the baseline
reads to detect changes. The configured worker must have one container. Each workflow run
uses a unique `deploy-<run-id>-<attempt>` revision suffix.

For a paused baseline, the helper restores inactivity after the image update. It can deactivate
only a revision whose application/revision ID, container name and image match the pre-update
inventory, or the new revision with this run's exact suffix and image. Every cleanup target is
reread immediately before deactivation. Success requires the requested configured image and
revision plus two consecutive complete inventories containing zero active revisions. Enabled
deployments perform no deactivation and require the requested revision to be active.
Every final inventory must contain the requested revision and all preobserved identities.
An empty or truncated inventory fails verification. Retention pruning that removes a baseline
revision also requires operator reconciliation rather than being accepted as a verified pause.

An uncertain update exit still attempts cleanup of known identities, then reports failure even
if inactivity is restored. Missing or changed identities, unexpected revisions, concurrent
application changes, incomplete provisioning and failed final reads also fail the deployment.
The helper never stops an unknown revision. Workflow concurrency queues ingestion deployments;
operators must also serialize manual updates and pause/enable changes with that job. A manual
activation of an existing known revision is indistinguishable from Azure reactivation during
an image update. Read/validate/deactivate is not an Azure atomic compare-and-set, so external
changes between reads cannot be excluded by this helper.

**This is final-state pause restoration.** Azure can activate both old and new revisions during
an image update. Deactivation stops their replicas afterward; there can be a transient worker
start before cleanup. Existing independent acquisition fences must remain in place when live
acquisition is prohibited. Cancellation, process termination or an unavailable Azure control
plane can prevent cleanup; a failed/interrupted job requires operator inventory verification.
The helper does not change credentials, feature flags, revision mode or scale rules, and does
not authorize a Live rollout.

Run the deterministic workflow/helper tests without Azure access:

```bash
node --test scripts/deploy-ingestion.test.mjs
```

The harness executes the checked-in image-update step against an Azure CLI process double,
including older-plus-new activation, enabled scale-to-zero, unknown identities, concurrent
changes, uncertain command outcomes and independent final-state reads. It verifies local
orchestration; it does not establish production platform behavior or a live deployment result.
Actual deployed evidence belongs in the private operator runbook and closeout records.

Platform contract checked against [Azure CLI revision commands](https://learn.microsoft.com/en-us/cli/azure/containerapp/revision?view=azure-cli-latest),
[image update and revision suffix commands](https://learn.microsoft.com/en-us/cli/azure/containerapp?view=azure-cli-latest#az-containerapp-update),
[revision activation and deactivation](https://learn.microsoft.com/en-us/azure/container-apps/revisions-manage),
and [Azure CLI's revision implementation](https://github.com/Azure/azure-cli/blob/001d881c090dda7aedda98d1793d72793c588bd7/src/azure-cli/azure/cli/command_modules/containerapp/custom.py).
The CLI's revision-list default is active-only; `--all` requests the complete retained inventory.
