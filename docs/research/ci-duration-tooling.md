# CI duration: tooling and evidence options

Public-safe supporting research for [#403](https://github.com/jwh3times/apexracers/issues/403), checked
2026-10-11. This note records tool capabilities and implementation tradeoffs, not measured savings or
an approved workflow change. See the issue's timing report for measurements. No workflows, test
inventory, privacy collector or coverage settings were changed for this research.

## Current repository boundaries

`Directory.Packages.props` pins `xunit.v3.mtp-v2` 4.0.1 and
`Microsoft.Testing.Extensions.CodeCoverage` 18.12.0. `global.json` selects MTP; the test project is an
executable. `Helpers/PostgreSqlFixture.cs` defines one PostgreSQL collection, one container shared by
that collection, and fresh GUID-named databases using `EnsureCreated` for individual requests. Fresh
databases support isolation; they do not prove that every test's process-wide resources, environment
variables, histories, ports and file paths are parallel-safe. These are observations from the
repository's [package versions](../../Directory.Packages.props),
[test project](../../src/ApexRacers.Tests/ApexRacers.Tests.csproj) and
[fixture](../../src/ApexRacers.Tests/Helpers/PostgreSqlFixture.cs).

The [general Test job](../../.github/workflows/deploy.yml) runs backend coverage, then Node setup,
repository/deployment-helper tests and frontend coverage. The separate
[privacy job](../../.github/workflows/driver-privacy-contract.yml) builds backend/SPA and runs the full
backend suite without coverage, followed by six browser cases. Its collector starts after builds,
requires a fresh context and hashes all relevant Release DLLs, runtime configurations and built SPA
files before and after execution. The [privacy guide](driver-privacy-contract.md) describes the
43-row inventory, original outcomes, admitted positives, immutable artifacts and synthetic-only
meaning. These rules make generic report reuse insufficient.

## xUnit scheduling

In xUnit v3 4.0+, `parallelMode` defaults to `collections`: different collections can run concurrently,
but tests within each collection run serially. `all` is now an available mode. MTP's
`testconfig.json` supports `maxParallelThreads`, defaulting to CPU thread count, and
`parallelAlgorithm`, defaulting to `conservative`. Those are capabilities, not evidence that enabling
them improves this suite. In particular, increasing threads under `collections` does not split a
single shared PostgreSQL collection. [xUnit configuration reference](https://xunit.net/docs/config-testconfig-json)

The conservative algorithm limits tests started and improves timing accuracy. Aggressive scheduling
starts additional tests that compete to resume after awaits; xUnit warns that this affects timeout
accuracy. Treat aggressive mode as a separate experiment rather than an initial production change for
deadline/drain tests. Measure CPU/I/O pressure and tail latency before selecting a bounded concurrency
level. [xUnit parallelism documentation](https://xunit.net/docs/running-tests-in-parallel)

MTP exposes `--max-threads`, `--parallel`, `--parallel-algorithm`, diagnostic switches, class/method/
namespace filters and the current TRX reporter switches. Use the pinned executable's help to verify
syntax before an experiment; do not copy VSTest `--filter` or runsettings parallelism instructions.
[xUnit MTP reference](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)

Candidate experiment: first map the slow classes to their collection and shared resources. If the
serial collection dominates, compare a small number of explicitly independent collections or runner
shards with isolated fixture ownership. Keep each continuous lifecycle/admitted history and each
multi-host scenario intact. Do not partition an existing ledger by recipient or owner. Avoid blanket
`parallelMode=all` until same-collection fixtures and mutable resources have been audited.

## Coverage measurement and merging

The MTP extension's `--coverage` uses dotnet-coverage and supports Cobertura output and an XML settings
file. Managed static/dynamic instrumentation are supported; native coverage is disabled by default.
The documentation does not establish a universal overhead percentage for this workload.
[Microsoft MTP coverage reference](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-code-coverage)

`dotnet-coverage collect` covers a .NET process and its subprocesses; it supports static and dynamic
instrumentation. Static instrumentation can modify files in an explicit instrumentation step.
`dotnet-coverage merge` can merge coverage, XML and Cobertura reports. Therefore shard coverage is a
tool-supported option, but arithmetic averaging of shard percentages is inappropriate: merge coverage
for the exact same product build, then enforce the non-empty line/branch denominators and both gates.
Pin the tool for reproducibility if introduced.
[Microsoft dotnet-coverage reference](https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage)

Candidate experiment: build separately and time matched `--no-build` runs with and without coverage,
using fresh equivalent fixtures, fixed runner class and pinned SDK/packages. Preserve the coverage
settings and inventory. Record setup/teardown, process/container counts, TRX wall intervals, coverage
report validity and CPU/resource usage. The current difference between jobs cannot isolate coverage
overhead because build, browser prerequisites and runner conditions differ. Instrumentation affecting
on-disk binaries must also be tested against the privacy collector's unchanged-build requirement
before proposing one instrumented execution as shared privacy evidence.

## Artifact reuse and workflow topology

GitHub supports sharing artifacts between dependent jobs and immutable artifact handling. Download
digest mismatch is documented as a warning, so privacy reuse needs an explicit failing integrity check
against the expected inventory; transport alone does not certify commit, build or original outcomes.
Prefer one producer in the same run/attempt, with explicit artifact IDs and source/build hashes, over
selecting the latest artifact by name from another run.
[GitHub artifact documentation](https://docs.github.com/en/actions/tutorials/store-and-share-data)

The upload action documents file permission loss for zipped artifacts. Executable apphosts used by
child HTTP processes must retain their permissions, for example through a tested tar transport. Unique
names per shard avoid colliding uploads; retain each original TRX and fault directory rather than
overwriting failed reports during merging.
[Official upload-artifact README](https://github.com/actions/upload-artifact)

Candidate designs, in increasing evidence complexity:

| Design | Benefit to measure | Required preservation / cost |
| --- | --- | --- |
| Run frontend/repository checks independently and aggregate into `Test` | Earlier frontend failure feedback and removal of the serialized tail | Preserve both frontend coverage gates; extra runner/setup cost; backend still likely controls successful PR duration |
| Produce exact Release/SPA build once, consume it in dependent jobs | Duplicate build/restore work | Include every inventory path and executable permission; fresh producer bound to run/attempt/commit; download overhead may exceed saved build time |
| Balance independently isolated backend shards | Shorter backend wall time | More runner-minutes/setup, complete disjoint case inventory, per-shard container/history/port/artifact isolation, valid merged coverage |
| Share one backend execution between coverage and privacy evaluation | Less duplicated backend execution | Explicit review of evidence topology; collector start before original execution; compatible instrumentation/build hashes; complete original failures, matrix, browser and artifact inventory |

The existing privacy collector expects one backend TRX and a single fresh execution context; it is not
a shard aggregator. Replacing it requires tests proving rejection of missing/duplicate cases, skipped
or inconclusive outcomes, stale or mixed builds, nonzero original exits, retry substitution, missing
artifacts and altered admitted receipts. A filtered privacy run is also a test-inventory change:
preserving row names alone does not justify dropping backend evidence. No such change is selected here.

## Required checks and cancellation

GitHub distinguishes skipped workflows from skipped jobs: path/branch filtering can leave required
checks pending, while conditionally skipped jobs can report success. A required aggregation job should
use `always()` with `needs`, explicitly reject every prerequisite result other than success and verify
the expected inventory. Keep the exact `Test` and `Driver Privacy Contract` contexts and eligible PR/
push triggers; a dispatch-only replacement does not satisfy PR rulesets. Do not add path filters.
[GitHub required-check troubleshooting](https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/troubleshooting-required-status-checks)

GitHub concurrency supports cancellation of in-progress runs and recommends workflow-specific group
names to prevent workflows canceling one another. PR-only cancellation may avoid obsolete work, but
does not shorten one run's execution. Measure saved runner-minutes separately. Preserve existing
deployment serialization, complete original failure uploads and reliable child/container cleanup;
interrupted privacy executions must remain incomplete rather than being promoted to success.
[GitHub concurrency documentation](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency)

Before implementation, rank these candidates using measured critical-path time, total job
runner-minutes, artifact size/transfer time and flake rate. No numerical savings can be derived from
tool documentation alone. Any green outcome remains controlled synthetic evidence with
`liveEligible=false`.
