# Shared live backend coverage experiment (#405)

## Status

This experiment evaluates the one-runner, two-process alternative demonstrated in
[#408](backend-coverage-merge-preservation.md). Production `Test` and the dedicated
Driver Privacy Contract remain unchanged. The hosted three-pair evaluation rejected
this topology: its median general gate exceeded 20 minutes and one exact coverage
observation comparison failed. The research harness and original results are retained
for diagnosis; no production adoption is selected.

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

## Hosted failure and cancellation rehearsals

The final cleanup revision `85ddc02d53bf2e355b3962daad742b9989a2a15f`
passed its focused PostgreSQL smoke and expected-failure rehearsals:

| Original run | Trigger | Original outcome |
| --- | --- | --- |
| [38314380675](https://github.com/jwh3times/apexracers/actions/runs/38314380675) | Focused smoke | Passed; 47 cases and exact coverage preservation |
| [38314383455](https://github.com/jwh3times/apexracers/actions/runs/38314383455) | Owned PostgreSQL producer failure | Rejected; deliberate fault confirmed |
| [38314386664](https://github.com/jwh3times/apexracers/actions/runs/38314386664) | SIGTERM after owned PostgreSQL readiness | Rejected; deliberate interruption confirmed |
| [38314380288](https://github.com/jwh3times/apexracers/actions/runs/38314380288) | Actual Actions cancellation during PostgreSQL work | Cancelled prerequisite; experimental aggregate failed |

All four retained successful original and follow-up cleanup assessments. Actual
Actions cancellation interrupted an active PostgreSQL producer with exit `-15`
and no TRX; the other producer's original 50 passing cases and exit 0 remain retained.
The collector itself returned 0, but its partial coverage did not make the candidate
acceptable. Original cleanup took 25.306 seconds and follow-up cleanup 25.756 seconds,
within the 30-second bound; each required two consecutive observations with no owned
processes or Docker resources remaining.

Earlier rehearsals remain separate originals. The first Actions cancellation at
`2b5e509` occurred after producer success and therefore did not prove interruption.
The active cancellation at `f8ef2fb` rejected the candidate, but its follow-up cleanup
assessment failed during concurrent removal. That failure was preserved; the later
revision handles verified concurrent removal and requires stable absence. These
focused checks do not replace or relabel the full comparison campaign at `2b5e509`.
Missing/duplicate cases, missing/nonpassing TRXs, nonzero exits and resource identity
changes also have negative validator tests; they are distinct from the hosted
interruption evidence.

## Original outcomes

The original [three-pair hosted campaign](https://github.com/jwh3times/apexracers/actions/runs/38310951392)
completed with a failed experimental gate at source
`2b5e50920fbad7e426d64d3236859faee6bf4b75`. **Reject this topology.**
Its median observed general-gate cost was **23 minutes 13 seconds**, above the unchanged
20-minute target; one original exact coverage-observation comparison also failed.
Production `Test`, its coverage contract and the Driver Privacy Contract remain
unchanged. No additional full pairs were run to replace these originals.

The [machine-readable campaign summary](../testing/backend-shared-405-campaign.json)
records original outcomes, coverage comparisons, job costs, producer/case phases,
source and tool bindings, GitHub artifact digests and retained-file hashes.
All six baseline/prototype executions completed all **1,318 cases** without a
skip or failure. Each prototype produced **664 + 654** passing cases in a complete,
disjoint partition of whole classes. The experimental acceptance result remains
separate from these passing test outcomes.

| Original pair | Baseline general gate | Prototype general gate | Dedicated privacy | Prototype + privacy | Original candidate |
| --- | ---: | ---: | ---: | ---: | --- |
| 1 | 27m 52s | 23m 19s | 30m 46s | 54m 05s | Accepted |
| 2 | 34m 31s | 23m 13s | 30m 03s | 53m 16s | Accepted |
| 3 | 26m 30s | >=18m 33s | 20m 41s | >=39m 14s | Rejected |

General costs include the original job's setup, retained-artifact handling,
cleanup and, for accepted candidates, repository/frontend tests and frontend
coverage. Each pair also includes the common **59-second canonical build** and
**2-second aggregate gate**. Pair 3 rejected the coverage comparison before
repository/frontend testing, so its observed general and combined costs are
**lower bounds**, not completed successful gate measurements. Both accepted
candidates stayed below 65 combined runner-minutes. The incomplete third candidate
cannot establish that budget; the general-gate median independently exceeds
20 minutes even using its lower bound.

The three unchanged, same-head privacy originals passed:
[38310962897](https://github.com/jwh3times/apexracers/actions/runs/38310962897),
[38310965790](https://github.com/jwh3times/apexracers/actions/runs/38310965790) and
[38310968409](https://github.com/jwh3times/apexracers/actions/runs/38310968409).
Measured jobs shared Ubuntu image `20261004.327.1`, kernel
`6.17.0-1022-azure` and four logical CPUs. Exact physical host, processor vendor and
hardware-counter profiles were not captured.

### Coverage and provenance

Every pair retained the same source/class/method/line/condition model and the same
root counts: **7,581 valid / 7,266 covered lines** and **4,127 valid / 3,600 covered
branches**. All original reports contained exactly the five product assemblies
and satisfied the existing positive-denominator 85% gates.

Pairs 1 and 2 also matched every normalized covered observation and verified
unchanged source/build, SDK, runtime and collector bytes before and after execution.
Pair 3 differed in **four normalized rows**, representing two await sites repeated
in the class and method views of the report:

| Product source observation | Baseline | Shared collector |
| --- | ---: | ---: |
| `AdminService.GetFlagsForUserAsync`, line 58 | 100% (2/2) | 50% (1/2) |
| `AdminService.GetFlagsForRoleAsync`, line 64 | 50% (1/2) | 100% (2/2) |

These rows belong to the generated `MoveNext` methods. The observation difference
does not establish its cause or identify lost collector branches. Passing cases,
equal aggregate counters and an unchanged model do not satisfy the required exact
observation comparison. The original rejected context remains rejected.

Pair 3 stopped before taking its after-source/build/SDK/runtime/tool snapshot.
Its before bindings matched the control, but **post-run immutability is unverified**
for that candidate. Do not infer that check passed from the equal coverage counters.
All three original cleanup assessments completed successfully. Later cleanup
revisions and cancellation rehearsals are separate source-bound experiments and
do not upgrade the rejected campaign into three passing pairs.

### Measured work remaining inside the producers

Retained TRXs and phase events show where a bounded performance investigation can
start without changing acceptance cases. In accepted pair 2, the 664-case producer
recorded 699.5 seconds of inclusive `reference.topology` spans across 59 operations,
including 402.9 seconds of `reference.setup`. The 654-case producer recorded
436.9 seconds of `topology.exercise` and 403.9 seconds of `publication.topology`.
These spans nest and overlap; their sums are **not additive wall-time attribution**.

The slowest original case in those producers was respectively
`DemoTransitionTests.IdempotentDemoSeedAndSqlTeardownPrecedeRealAcquisition`
(**76.893 seconds**) and
`EvidenceCopyLifecycleTests.MigratedSyntheticSeederRoundTripTeardownAndExplicitFreshSession`
(**89.869 seconds**). Collector elapsed times were 1,203.179, 1,186.801 and
995.248 seconds. Whole-job costs, rather than these partial backend times, determine
the timing decision. Existing measurements do not prove a specific optimization
will meet the target.

### Retained evidence and next bounded investigation

The always-cleanup step appended to the shared
`cleanup-command-original.jsonl` after each original context recorded its artifact
hashes. The complete journal is retained unchanged. For each of the six contexts,
a separately retained exact prefix matches its original recorded SHA-256. This
recovers the context-bound journal bytes without rewriting the original outcome;
it does **not** justify a blanket claim that every retained whole artifact stayed
immutable. The summary records both hashes and prefix lengths.

The release closeout prepares
[the hosted evidence archive](https://github.com/jwh3times/apexracers/releases/download/v14.0.9/backend-shared-405-hosted-evidence.tar.gz)
and [its SHA-256 file](https://github.com/jwh3times/apexracers/releases/download/v14.0.9/backend-shared-405-hosted-evidence.tar.gz.sha256)
for publication with this report; upload verification is part of that closeout.
The archive retains original contexts, case inventories, reports, TRXs, commands,
full journals, verified prefixes and source versions. Compiled/cache bytes are
excluded; their original hash bindings remain in the evidence.

The bounded next investigation is
[#411: diagnose the two async coverage observations](https://github.com/jwh3times/apexracers/issues/411).
It must reproduce and explain the difference through focused original evidence,
without assuming a cause, weakening the coverage contract, excluding product code
or replaying the full campaign to obtain a passing sample. It must also address the
shared-journal provenance limitation before any reconsideration. Resolving that
question would leave the independent 20-minute timing miss in place and would not
authorize topology adoption. Synthetic evidence retains `liveEligible=false`.
