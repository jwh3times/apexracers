# Async coverage-observation diagnosis (#411)

> **Interpretation correction from #416:** canonical PDB mapping places the reported
> conditions at the `Select(ToDto)` projections (lines 58 and 64), whose generated
> code shares a delegate cache. Independent and actual-service prewarming controls
> attribute the current order-sensitive observations to that cache. The actual awaits are lines 57 and 63, with hidden
> sequence points for their completion branches. Earlier “await-site” labels did
> not establish condition identity. Original reports, hashes and outcomes remain
> unchanged; see the [follow-up attribution](backend-async-coverage-attribution.md).

The bounded investigation reproduced the two-site observation drift in ordinary
MTP collection, without sharding. A controlled comparison also inverted the
reported observations when the diagnostic method-call order was reversed, while
actual SQLite queries completed synchronously under a queued-context observer.
**At #411 closeout the observations were order-sensitive, while their underlying
mechanism remained unproven. The #416 follow-up attributes the current focused
controls to shared compiler-generated delegate-cache execution.** Production `Test`, exact
coverage comparisons, both 85% gates and the Driver Privacy Contract remain unchanged.

[#405](backend-shared-collector-experiment.md) remains rejected. Resolving this
question would not resolve its independent 23m13s median general-gate timing miss.
No new full-suite campaign or adoption claim is made.

## Tight original replay and focused controls

The retained #405 pair 3 reports remain unchanged. A readonly replay returns exit 1:
identical source/class/method/line/condition models and root counters, but four
normalized class/method rows differ at `AdminService` lines 58 and 64. Each reported source
site occurs in both class and method views; this is two observations, not four
identified lost edges. The historical source was `2b5e509` with xUnit 4.0.1. That
failed candidate did not capture after-source/build/tool/runtime hashes; current
experiments do not repair that missing historical proof.

Current controls use one fresh Release build at
`1fa1664c604e0b848bc1c4ab4ae36a47c0a6d56e`, SDK 10.0.112, .NET runtime 10.0.12,
xUnit MTP 4.0.2 and Microsoft coverage 18.12.0. Identical copied product/test bytes,
SDK, runtime and collector bytes were verified before/after the successful runs.
The existing `AdminServiceTests` class uses actual SQLite through
`DbContextFactory.Create`; it is not a PostgreSQL class. All 39 cases passed per
producer, with complete copied-UID mappings and original reports retained.

| Original diagnostic workload | Role projection line 64 | User projection line 58 |
| --- | ---: | ---: |
| First ordinary MTP, 39 cases | 100% (2/2) | 50% (1/2) |
| One live collector, one process, 39 cases | 50% (1/2) | 100% (2/2) |
| One collector, two sequential processes, 78 declared cases | 100% (2/2) | 100% (2/2) |
| One collector, two parallel processes, 78 declared cases | 100% (2/2) | 100% (2/2) |
| Second ordinary MTP, 39 cases | 50% (1/2) | 100% (2/2) |

The second ordinary MTP result differs from the first. This refutes an explanation
that requires a different collector/configuration alone. These are declared
focused diagnostic workloads, not retries of a failed gate or replacement full-suite
measurements.

## Falsifiable completion and call-order controls

A separate research executable calls the actual unchanged `AdminService` and
`FeatureFlagEligibility` against SQLite and real disposable PostgreSQL. It uses
no substitute service or mocked query result. A PostgreSQL command interceptor
pauses the first real reader operation, observes the product task still pending,
then releases the actual SQL operation. The small diagnostic deadline controls
only that probe; lifecycle and privacy windows are untouched.

An outer task being completed when it returns is insufficient to infer the
internal await decision: a continuation could race. The stronger SQLite control
installs a queued `SynchronizationContext` before calling the product and does not
pump callbacks until the call returns. All 32 calls completed with zero posted
callbacks. The two service awaits use ordinary context capture. Real PostgreSQL
natural calls were pending, and all 32 gated calls were proven pending while their
real query was paused. Every call returned the same single enabled Standard flag.

| Controlled original workload | Completed on return | Queued posts | Pending while SQL paused | Role / User condition observations |
| --- | ---: | ---: | ---: | --- |
| Role-first strict SQLite | 32 / 32 | 0 | Not applicable | 100% / 50% |
| Role-first natural PostgreSQL | 0 / 32 | Not applicable | Not paused | 100% / 50% |
| Role-first gated PostgreSQL | 0 / 32 | Not applicable | 32 / 32 | 100% / 50% |
| User-first strict SQLite | 32 / 32 | 0 | Not applicable | 50% / 100% |

The role-first and user-first fixtures differ only in their declared call-order
array. Their diagnostic DLLs therefore differ; that change is explicitly bound.
The referenced product/test DLLs, coverage settings, SDK, runtime and collector
remain the same. The observations invert even under the stronger completed-query
control, so attributing the report difference directly to natural await scheduling
would exceed this evidence. At #411 closeout the lower-level mechanism
had not been identified or confirmed by tool maintainers; #416 subsequently
identified the shared cache through canonical mapping and causal controls, without
establishing a collector defect.

The retained IL disassembly identifies `TaskAwaiter.get_IsCompleted` at IL offset
49 followed by `brtrue.s` at offset 54 in each generated `MoveNext`. Reports retain
the full condition identity: `ApexRacers.Api`, the named generated class, source
path, `MoveNext`/class view, line 58 or 64, denominator 2, condition number `0`,
type `jump`, Boolean line hit and condition coverage. These observations do not
provide portable identities for the individual underlying edges. Microsoft's
[coverage discussion](https://github.com/microsoft/codecoverage/issues/104#issuecomment-1991051175)
describes that exported condition percentages do not identify the covered side.

## Failures, cleanup repair and provenance limits

The first research build failed because implicit usings were absent. The first
controlled PostgreSQL command later exited 134 because the diagnostic fixture read
`container.Id` after disposing it. Both failures remain original failures, with
logs/source snapshots; later fresh outputs do not replace them. The fixture now
captures the ID before disposal. Owned process/container cleanup was verified for
the failed and successful runs.

The shared harness separately places follow-up journals and consumed source-context
snapshots in an exclusive `cleanup-followup/` subtree. Original context and every
context-bound artifact hash remained unchanged after actual cleanup follow-ups.
Independent local PostgreSQL normal baseline/prototype, producer-failure and SIGTERM
rehearsals retained rejected original outcomes where appropriate and successful
bounded owned cleanup. These are local signals, not a new hosted Actions-cancellation
claim. The reproduction verifier and summaries are retained under
`TestResults/async-411-cleanup-summary-1/` and `TestResults/async-411-cleanup-*`.

The earlier diagnostic records verified canonical/fixture/SDK/runtime/tool byte
snapshots but did not record an after snapshot of every helper/settings/diagnostic
source file. Do not upgrade those records into blanket source immutability claims.
The final runner adds helper/settings/source before/after guards, derives settings
from the current runsettings instead of a previous evidence path, and makes generic
replay metadata report-hash-only. These guard changes were syntax/derivation checked;
no additional causal campaign was run to make earlier records pass.

## Evidence, reproduction and bounded next step

The [machine-readable summary](../testing/backend-async-411.json) contains original
commands, exact observations/IL, failures, hash bindings and cleanup evidence.
Release closeout prepares the [raw evidence archive](https://github.com/jwh3times/apexracers/releases/download/v14.0.13/backend-async-411-evidence.tar.gz)
and [its SHA-256 file](https://github.com/jwh3times/apexracers/releases/download/v14.0.13/backend-async-411-evidence.tar.gz.sha256).
Publication and upload-digest verification are pending that closeout.
Raw originals use separate `TestResults/async-411-*` roots. Build/cache bytes are
bound by their hashes; original source snapshots and reports are retained separately.
The [upstream-ready reproduction draft](../../scripts/async-coverage-probe/UPSTREAM.md)
gives focused commands and an explicit interpretation limit. It has not been posted
as a tool-maintainer finding or treated as a confirmed defect.

The supported next step is [#416: independently minimize and map the order-sensitive counterexample](https://github.com/jwh3times/apexracers/issues/416),
with the original branch model and exact comparison preserved. This is a separate
agent-owned investigation, not authorization to adopt sharding.
Do not exclude async product code, tolerate differing observations, shorten
privacy scenarios or rerun the full campaign to obtain a passing sample. Any
adoption would still require fresh preservation, reliability and timing evidence.
Synthetic evidence retains `liveEligible=false`.
