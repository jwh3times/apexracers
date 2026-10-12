# Two backend coverage shards: rejected feasibility prototype (#405)

## Decision

Keep the current general `Test` topology. The two-shard proposal failed the coverage
preservation preflight. The rejected implementation is retained at
[commit 097ae736](https://github.com/jwh3times/apexracers/tree/097ae7364f221853367ee5f1626db3c7db21e34f)
on `prototype/405-backend-coverage-shards`. This report and its preflight evidence can ship
independently; the proposed scripts and workflow are excluded from `main`.
No shard timing improvement has been established.

The subsequent [#408 preservation investigation](backend-coverage-merge-preservation.md)
demonstrates matching full-suite coverage through one live collector containing two
independent processes on one local VM. It preserves this rejected prototype and its
original failures; the separate-runner post-hoc merge remains rejected. Hosted
cleanup, cancellation and timing acceptance remain unverified.

The proposed workflow is designed to build once, discover every case, allocate exactly two
whole-class bins and supply identical compiled bytes to independent hosted runners. Its `Test`
aggregator checks for failed/cancelled/missing prerequisites, incomplete original artifacts,
non-passing or duplicate executions, changed source/build/SDK bindings, resource leftovers,
and changed product coverage models. These checks have synthetic test evidence; the proposed
hosted topology has not been exercised. The Driver Privacy Contract workflow, collector,
43-row matrix, receipt and `liveEligible=false` are unchanged.

## Coverage feasibility results

| Probe | Lines valid | Branches valid | Branch rate | Result |
| --- | ---: | ---: | ---: | --- |
| Original #404 full-suite Cobertura | 7,581 | 4,127 | 87.2304% | Existing model |
| Microsoft 18.12.0 merges two retained full-suite XML reports | 7,581 | 2,800 | 97.7857% | Reject: missing branches |
| ReportGenerator 5.5.11 merges the same two reports | 7,818 | 4,417 | 86.7331% | Reject: changed model |
| Direct MTP Cobertura, 25 FieldPercentile cases | 4,991 | 2,637 | 0.07584% | Representative control |
| Native `.coverage` exported by Microsoft 18.12.0 | 4,991 | Absent | 100% | Reject: no branch denominator |
| Microsoft XML exported by Microsoft 18.12.0 | 4,991 | Absent | 100% | Reject: no branch denominator |

The representative cases all passed in their original invocations; their very low coverage
is expected because they exercise one small class. They test format/export feasibility,
not whole-suite coverage gates or shard speed. Those runs used the existing compiled Release
assembly without a rebuild between formats. Closeout hashes identify the available bytes;
these probes do not claim a new full-suite source/build measurement binding.

The complete #404 originals retain their original provenance in
`docs/testing/backend-fixture-cost.json`. Probe paths, report attributes and original
hashes are recorded in `docs/testing/backend-shard-405-preflight.json`; raw files remain
in `TestResults/backend-shard-405-merge-probe/`, the #404 evidence tree, and the recorded
scratch merge path. Failed alternatives were retained rather than replaced by later results.

Microsoft documents support for native, XML and Cobertura inputs in its
[merge command](https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage#dotnet-coverage-merge).
Its maintainer explains why aggregate switch information in Cobertura cannot identify
which individual branches were covered and why unmergeable conditions are removed in
[codecoverage issue 11](https://github.com/microsoft/codecoverage/issues/11).
Support for an input format therefore does not establish preservation of this repository's
coverage contract. Native and Microsoft XML alternatives were tested directly and did not
produce a positive branch denominator in these exports.

The second tool was pinned to
[ReportGenerator 5.5.11](https://www.nuget.org/packages/dotnet-reportgenerator-globaltool/5.5.11).
Its documented [raw mode](https://github.com/danielpalme/ReportGenerator/wiki/Settings)
preserves class structure but requires a PRO license; no license was assumed or purchased.
The tested default merge changed both denominators and cannot establish the existing gate.
Even preserved counters would need branch-identity fidelity, not just matching percentages.

## Isolation and failure handling review

- Separate GitHub runners isolate process-local static state, Docker engines, ports and files.
  Existing GUID databases, `Pooling=false`, dynamic Kestrel ports, primary/history separation,
  `pg_dump`/`psql` restore and pinned legacy Git binary remain intact.
- Canonical build and producers must use the same absolute checkout path. Two SQL locators
  embed `[CallerFilePath]`; copying binaries into nested checkouts would violate this boundary.
  Full matching source and Git history are required in each producer.
- Evidence, publication rehearsal artifacts, phase probes, temporary files, CLI and NuGet
  directories are configured separately per producer. The runner's cancellation handler
  sends TERM then bounded KILL to the owned process group and records post-run
  process/container cleanup. Its behavior under actual hosted cancellation is unverified.
- Discovery includes theory rows and verifies stable UIDs. TRX uses different execution IDs;
  this prototype requires unique discovered display names for a one-to-one join and rejects
  ambiguous names rather than inventing an identity mapping.
- Thirteen synthetic rejection tests pass, including failure/cancellation/missing status,
  missing/duplicate/skipped/inconclusive outcomes, replaced originals, stale bindings,
  invalid coverage and cleanup leftovers. These tests do not establish a real hosted
  cancellation/cleanup rehearsal.

## Reproduction and remaining work

The retained [prototype script](https://github.com/jwh3times/apexracers/blob/097ae7364f221853367ee5f1626db3c7db21e34f/scripts/backend-shard-prototype.py),
[rejection tests](https://github.com/jwh3times/apexracers/blob/097ae7364f221853367ee5f1626db3c7db21e34f/scripts/backend_shard_prototype_tests.py)
and [proposed workflow](https://github.com/jwh3times/apexracers/blob/097ae7364f221853367ee5f1626db3c7db21e34f/.github/workflows/deploy.yml)
exist in that captured commit. In an isolated checkout of that commit, run
`python3 scripts/backend_shard_prototype_tests.py` for the rejection tests. These files
are not installed by the findings-only change on `main`.
The workflow's native export currently fails the strict coverage
preflight and must not be adopted or have its branch validation relaxed.
The script can compare accepted coverage with a reference baseline through
`--reference-baseline`; the proposed workflow does not yet supply that argument. A resumed
experiment must make the full-suite baseline comparison mandatory before accepting a shard
result.

Original Microsoft full-report merge command:

```bash
dotnet-coverage merge \
  TestResults/fixture-cost-404-matched/2-coverage/coverage.cobertura.xml \
  TestResults/fixture-cost-404-matched/4-coverage/coverage.cobertura.xml \
  -o /tmp/apex405-coverage-merge-check.xml -f cobertura
```

The original prerequisite was to resolve supported coverage preservation before resuming #405.
The subsequent #408 report supplies a local shared-collector route, pending review and merge.
Revise the proposed topology, bind new original runs to one unchanged commit/build/SDK, verify the collected source,
line and branch model against full-suite baseline originals, rehearse hosted cancellation,
and collect at least three matched baseline/prototype CI comparisons. The original targets
remain median general Test <=20 minutes and combined general plus privacy <=65 runner-minutes.
Those comparisons were not run because coverage preservation already failed; there is no
speed claim or topology adoption. Publishing the findings does not deploy the proposed topology.

[Issue #405](https://github.com/jwh3times/apexracers/issues/405) remains open. Its original
coverage blocker is tracked in [issue #408](https://github.com/jwh3times/apexracers/issues/408);
the demonstrated shared-collector route requires review and merge before resuming #405.
This is agent investigation,
not a required human action. The ReportGenerator PRO alternative has not been selected, so
this result creates no current license-purchase requirement or private human follow-up.
