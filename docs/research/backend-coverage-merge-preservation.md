# Backend coverage preservation investigation (#408)

## Result and scope

One live Microsoft `dotnet-coverage` 18.12.0 collection containing two independent
MTP test processes preserved the current full-suite coverage report in a local Linux
comparison. All 1,318 cases passed exactly once across whole-class partitions of
664 and 654 cases. This supplies a supported alternative for further investigation
in [#405](https://github.com/jwh3times/apexracers/issues/405); it does not validate
the rejected prototype's separate-runner, post-hoc merge topology.

Production CI, its exact `Test` context, both 85% coverage gates, and the Driver
Privacy Contract workflow remain unchanged. The privacy collector, 43-row matrix,
admitted receipt and `liveEligible=false` are unchanged. This investigation makes
no Live eligibility or hosted performance claim.

The successful coverage comparison and the failed cleanup assessment are distinct
results. The original harness reported `preservationDemonstrated=false` because an
additional Docker container ID was observed immediately after collection. That
original outcome remains intact. Later explicit resource cleanup does not turn it
into a successful automatic-cleanup rehearsal.

## Why collect together

The [#405 preflight](backend-coverage-shard-prototype.md) rejected native, Microsoft
XML and Cobertura export/merge alternatives whose branch models differed from the
existing report. Further controlled probes retained those failures. Changing the
gate denominator or accepting a similar percentage would conceal the problem.

Microsoft documents the [`collect` command](https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage#dotnet-coverage-collect).
Its maintainer explains that exported Cobertura condition percentages cannot
identify which edge was covered, while collecting all executions together retains
additional internal data needed for their union in
[codecoverage issue #104](https://github.com/microsoft/codecoverage/issues/104#issuecomment-1991051175).
This is the supported approach tested here. No custom reconstruction, averaging,
or summing of branch counters is used.

A controlled five-edge switch separately exercises overlapping and complementary
observations: two inputs each cover two edges, while their union covers three.
The shared collection preserves the direct MTP control's model and observations.
This small fixture establishes the mechanism; it does not substitute for product
exclusions or a full-suite comparison.

## Full-suite evidence

Compiled product source is bound to commit
`18e2dc74f3cd2fa227fcbbfa49ab07e98d5d9b37`. One original full-suite MTP coverage
control is compared with one shared collection using identical copied Release
bytes. Discovery runs in each copy, because its test UIDs differ from canonical
discovery. The mapping rejects changed metadata or ambiguous display names and
uses the copied discovery's UIDs for execution. Whole classes remain together;
the partition covers the complete discovered inventory without overlap.

| Measurement | Original full-suite MTP | Shared collection |
| --- | ---: | ---: |
| Passed executions | 1,318 | 664 + 654 |
| Product lines valid / covered | 7,581 / 7,266 | 7,581 / 7,266 |
| Product branches valid / covered | 4,127 / 3,600 | 4,127 / 3,600 |
| Line / branch coverage | 95.8449% / 87.2304% | 95.8449% / 87.2304% |
| Local command duration | 1,370.492 s | 731.962 s |

Comparison includes source paths, classes, method names/signatures, line numbers,
branch flags, condition numbers/types and denominators. It also compares Boolean
line hits, condition observations and root valid/covered counters. All match.
This demonstrates preservation of the repository's observable report contract in
this comparison, not newly portable edge identities in exported reports.

Source/build, SDK files and host, runtime files, pinned tool files, research scripts
and original control artifact bindings remain unchanged across the shared run.
Product coverage retains the repository's exclusions. The shared collector
explicitly supplies the documented MTP instrumentation defaults: non-verifiable
dynamic managed instrumentation, no static managed or native instrumentation,
child-process collection, test-assembly exclusion and source filtering. These
defaults are described in Microsoft's
[configuration reference](https://github.com/microsoft/codecoverage/blob/9360f52ded3127f33f8558c37e12e54c1c1a0b28/docs/configuration.md).

Both test processes run on one VM with separate build copies, evidence, phase
output, CLI/NuGet directories and short producer-owned `TMPDIR` paths. Short paths
avoid the Unix IPC socket path limit. They retain real PostgreSQL fixtures,
independent lifecycle hosts and physical dump/restore cases. They share the VM,
Docker engine and collector; separate hosted runners are not demonstrated.

The roughly 22m50s and 12m12s durations are one local pair. They do not establish
CI savings, repeatability, combined runner-minutes or satisfaction of #405's
targets. Hosted comparisons remain required.

## Original failures and cleanup limits

Earlier native/export/configuration probes, zero-case invocations caused by
using canonical UIDs in copied executables, and an IPC startup failure remain
recorded as original failures. Corrected invocations have new evidence roots;
they do not replace those originals.

The shared run's immediate Docker delta contained one additional container ID.
That container was absent before later inspection, so its ownership and disappearance
time could not be established. Later inspection found no remaining owned API hosts
or PostgreSQL containers. A lingering `dotnet` diagnostic process was attributed
through the producer-owned `TMPDIR` socket and matching PID/start-time identity.
It received an explicit SIGTERM; the subsequent owned-process check was empty.

These follow-ups establish the recorded local cleanup state only. They do not
prove bounded automatic cleanup, failure cleanup or hosted cancellation. The
research runner's later bounded observation code has not been demonstrated by
this retained invocation. #405 must add resource ownership, disposal and
failure/cancellation evidence before adopting this topology.

## Evidence and reproduction

The public summary and original-file SHA-256 bindings are in
`docs/testing/coverage-preservation-408.json`. Raw originals remain under:

- `TestResults/coverage-preservation-408-mtp-11/`: controlled switch, direct MTP
  controls, original exports, rejected merges and shared collections.
- `TestResults/coverage-preservation-408-product-2/`: successful full-suite control,
  discovery, partition and the original failed subsequent candidate.
- `TestResults/coverage-preservation-408-shared-product-1/`: successful collection
  reports and per-producer TRX/discovery, original overall failed context and
  separately recorded cleanup follow-ups.
- The additional original roots listed in the summary's `earlierOriginalsRetained`.

Reproduction requires Docker, the repository SDK and a separately installed,
pinned Microsoft `dotnet-coverage` 18.12.0 tool. The research scripts reject an
existing output directory, retain originals and leave CI unchanged. For a fresh
control and candidate, run from the repository root:

```bash
python3 scripts/probe-product-coverage-preservation.py \
  --tool /path/to/pinned/dotnet-coverage \
  --output TestResults/coverage-preservation-new-control
```

`scripts/probe-shared-product-coverage.py` can compare a new candidate with an
immutable original control through `--baseline-context`, `--tool` and `--output`.
The control must still match source, compiled bytes, SDK, runtime and tool files;
a changed checkout requires a fresh control. Original runner snapshots and command
records identify what actually produced the retained evidence. Current scripts
are research tools, not an accepted required-check aggregator.

## Remaining #405 obligations

The coverage prerequisite has a demonstrated local route, pending review and
merge of #408. Resume #405 by revising its topology to one shared collector with
two independently isolated processes on one runner, or by separately proving
another topology. Preserve fail-closed inventory, artifact and coverage validation
and the exact required `Test` context. Rehearse failed/cancelled producers and
owned cleanup on hosted runners, then collect at least three matched
baseline/prototype CI comparisons. The original provisional targets remain
median general `Test` <=20 minutes and combined general plus privacy <=65
runner-minutes. Adopt only after reliability and measured savings are established.

No required human action remains: no credential setup, operator action or license
purchase is needed for this result. Remaining work is agent implementation and
verification. Product documentation and private feature status do not change.
