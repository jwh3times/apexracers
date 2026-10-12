# Shared live backend coverage experiment (#405)

## Status

This experiment evaluates the one-runner, two-process alternative demonstrated in
[#408](backend-coverage-merge-preservation.md). Production `Test` and the dedicated
Driver Privacy Contract remain unchanged while reliability and timing are evaluated.
No topology adoption or hosted performance result has been established yet.

## Experiment design

The branch-only `backend-shared-experiment.yml` builds one canonical Release tree
and transfers its exact bytes to original baseline and prototype jobs. The jobs
use pinned .NET SDK 10.0.112 and Microsoft dotnet-coverage 18.12.0. Baseline uses
original MTP collection; prototype executes two whole-class copied test applications
inside one live collector. The SDK is installed into an isolated root and its
selected version is checked explicitly, avoiding `latestMinor` selecting a newer
preinstalled SDK. The pinned [setup action supports the isolated installation](https://github.com/actions/setup-dotnet/blob/a98b56852c35b8e3190ac28c8c2271da59106c68/src/installer.ts).
Both retain source/build, SDK, runtime and tool bindings,
original discovery, commands, outcomes and raw reports. Location-specific copied
UIDs are mapped through complete unchanged case metadata.

The [resource audit](backend-shared-shard-isolation.md) describes isolation and
ownership. Cleanup observes PID/start-time identities and producer-owned diagnostic
sockets, records Testcontainers sessions before initialization, and inspects owned
Docker objects by concrete IDs and creation metadata. Unknown objects are never
removed based solely on a Docker delta. Missing or invalid ownership evidence
invalidates acceptance. Every Docker operation shares the cleanup deadline. Final session/resource
enumeration checks for late creation. Networks are bound to immutable ID and
creation metadata; volumes have reusable names, so residual volumes cause a
failed assessment rather than deletion by an unsafe identity.

Three matched original pairs must preserve the complete inventory and exact
source/class/method/line/condition coverage model and observations. General gate
measurements include setup, artifact handling, cleanup, repository tests and
frontend coverage. Common canonical-build and aggregator costs are conservatively
included in each pair. The unchanged dedicated privacy workflow supplies separately
retained same-head job runtimes. Targets remain median general gate <=20 minutes
and combined general plus privacy <=65 runner-minutes.

## Reliability prerequisites

Focused real-PostgreSQL smoke checks precede the full campaign. Producer failure,
producer interruption, missing/duplicate inventory and actual hosted Actions
cancellation must fail closed while retaining original evidence. An always-running
experimental prerequisite gate rejects non-success or absent producers; cancellation
must never yield a green result.

GitHub documents its [cancellation sequence](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-cancellation),
including escalation from SIGINT to SIGTERM and process-tree termination. The
runner records ownership before interruption; a separate always-step observes
cleanup after the original outcome without rewriting it. Actual hosted evidence
is required because `always()` alone does not prove that retention completed.

The initial branch push runs focused smoke. Once registered, the workflow exposes
smoke, three-pair comparison and fault campaigns through dispatch; GitHub describes
[dispatching a registered workflow against branches](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_dispatch).

## Original outcomes

Results will be added from the retained originals. No failed invocation will be
replaced by a later passing result. Research smoke and revised-harness checks do
not establish full-suite adoption or Live eligibility.
