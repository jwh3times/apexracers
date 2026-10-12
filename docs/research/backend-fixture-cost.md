# Backend fixture cost investigation (#404)

Measured 2026-10-11–12 UTC on one Linux host. Six original full-suite invocations each passed all
1,318 discovered cases. The median paired coverage command was **10.4% longer**; plain and coverage
command medians were **21m13s** and **23m06s**. Host readiness and scenario execution dominate the
instrumented costs. No fixture speedup, concurrency change or production pooling change is adopted.

[Sanitized evidence](../testing/backend-fixture-cost.json) contains the original-file hashes,
source/build manifest, paired figures, phase/method aggregates, resource observations and a separately
bound focused validation. This advances [#404](https://github.com/jwh3times/apexracers/issues/404)
after the [#403 investigation](ci-duration-investigation.md). The isolated shard prototype remains
in [#405](https://github.com/jwh3times/apexracers/issues/405); Windows connection churn remains in
[#391](https://github.com/jwh3times/apexracers/issues/391).

## Original matched full-suite measurements

The frozen source was `8a9aa8a48dd384ecb8cdfcff9fd484a649ed3dbe`. The sequence alternated plain and
coverage Release `--no-build` invocations, with a fresh disposable PostgreSQL container and distinct
probe/artifact directories per invocation. All original 1,316 cases from #403 remain; two probe-helper
cases were added. Every invocation matched discovery, with no failed, skipped or missing case.

| Original | Command | TRX interval | Coverage line / branch |
| --- | ---: | ---: | ---: |
| 1 plain | 1342.886s | 1341.583s | — |
| 2 coverage | 1488.899s | 1482.352s | 95.84% / 87.23% |
| 3 plain | 1272.857s | 1271.592s | — |
| 4 coverage | 1375.727s | 1371.430s | 95.84% / 87.23% |
| 5 plain | 1255.306s | 1254.040s | — |
| 6 coverage | 1386.317s | 1382.084s | 95.84% / 87.23% |

Paired command differences were **10.87%, 8.08%, 10.44%**. Their median is 10.44%; the ratio of the
mode medians gives a different statistic, 8.91%. Plain range was 20m55s–22m23s; coverage range was
22m56s–24m49s. Three pairs describe this host, not a confidence interval or an estimate for all CI
runners. Fixed alternating order does not isolate cache/order effects or background load. The cause
of the remaining runner variability is not established. These figures must not be substituted for
#403's actual GitHub job durations or used to predict #405's runner-minute target.

All coverage reports contain only the five product packages: Api, Core, Data, Ingestion and Seeder.
Every report has 7,581 valid lines / 7,266 covered and 4,127 valid branches / 3,600 covered. The
unchanged positive-denominator and both 85% gates passed. Coverage settings and test windows are
unchanged. Command timing ends immediately after process exit; sampler shutdown is reported
separately. TRX start-to-finish excludes some command/bootstrap/finalization time. Concurrent test
durations and nested phase durations are not additive backend wall time.

## Environment and binding

The context records Linux `7.2.9-200.fc44.x86_64`, glibc 2.43, 16 logical CPUs, SDK 10.0.112 and
Docker 29.9.0. The fixture uses `postgres:18.0-alpine`. SDK, image and NuGet caches were already
populated; restore/build is outside the measured command. The evidence records UTC start/end times.

Before and after every invocation, the runner verified the same 732-entry C#/project/config/build
manifest and SDK/host inventory. The SDK inventory hashes 3,144 installed SDK files plus the dotnet
host. Discovery, original runner and SDK inventory SHA-256 values are retained. Initial and final
Git state were clean at the same source commit. The final validated binaries were built immediately
before committing the probes: their informational version still names parent commit `9a9e0d6`.
Exact source and binary hashes establish the cost-study binding; the older version text does not
establish privacy acceptance.

Two runtime inputs were outside that original per-run manifest: `src/ApexRacers.Data/Seeds/purge_demo_data.sql`
and `docs/testing/driver-catalog-admission-v1.json`. The supplemental end audit found both byte-for-byte
identical to the starting Git blobs, with modification times before the experiment. Clean initial/final
Git state, matching blobs and old modification times support consistency; they do **not** supply
per-run fingerprints or prove no transient edit/revert. This limitation is retained. The final runner
now fingerprints every tracked `src/` input and the catalog receipt before/after execution.

These are instrumented cost observations. The dedicated Driver Privacy Contract collector, 43-row
matrix, catalog receipt and workflow are unchanged. Cost evidence does not supply privacy acceptance
or Live eligibility, and does not change `liveEligible=false`.

## Phase attribution and residuals

Monotonic spans retain process/span/parent identity, method identity and original outcomes. Exclusive
seconds subtract the union of direct child intervals; inclusive totals are nested. A lexical
`completed` outcome means a scope closed, including exception unwinding; async wrappers distinguish
success/fault/cancel. Original TRX outcomes establish test success. Method figures aggregate theory
executions; repeated topology roots are scopes, not additional cases.

| Exclusive phase total per invocation | Plain median | Coverage median |
| --- | ---: | ---: |
| Scenario exercise | 413.35s | 451.75s |
| Child host readiness | 342.02s | 408.98s |
| EnsureCreated | 70.97s | 70.76s |
| Actual migrations | 62.22s | 62.77s |
| Host stop | 56.98s | 52.82s |
| Authority/copy/operating seed | 20.22s | 20.61s |
| Database creation | 12.47s | 12.26s |
| Topology artifact write | 0.080s | 0.088s |
| Snapshot artifact write | 0.031s | 0.036s |

These phase totals locate costs; their sum is not the backend critical path. `topology.exercise`
contains original scenario actions, assertions, waits and I/O. It is a broad named phase, not
attribution to each SQL query or assertion. The older PublicationRehearsal/RehearsalProcess paths
are uninstrumented; external descendant sampling does not fill their phase gap. Ordinary fixture
options are measured, not every ordinary case's seed/assertion work. Unprobed case time and
root-exclusive time remain explicit residuals.

Whole-case/topology roots bracket at least 99.92% of the representative method TRX totals. Root
bracketing is distinct from named-child-phase coverage. In the six originals, immediate child phases
covered 99.79–99.81% of fixture-options roots, at least 99.98% of lifecycle/publication/reference
roots and 97.49–97.83% of Demo restore roots. Legacy upgrade reached only **93.46–94.35%**: the
provisional 95% attribution target was missed there, and those original measurements are retained.

After the six runs, commit `911fc97` added timings for legacy context preparation, legacy seed data,
HTTP reads, post-upgrade assertions and current API checks without changing their lifetimes or order.
Three fresh, original focused invocations passed against that separately fingerprinted source/build
and SDK. Immediate child phases covered **99.58%, 99.51%, 99.51%**, with residuals 0.076s, 0.070s,
0.068s. This closes the named-phase target for the final instrumentation. It does not replace the
six originals, establish final-build full-suite timing, or imply detailed CPU attribution to each
operation. The focused runner/context/original hashes are retained separately.

## Sampled resources

The sampler emits numeric counters and static phase/method labels; no arguments, environments,
connection strings, request payloads or SQL. Process ownership uses PID/start-tick identity.
Short-lived children and peaks can be missed: observed process CPU/read/write totals are lower bounds.
Median observed process CPU was 880.09s plain / 1028.89s coverage, with median peak sampled process
RSS sums about 1.24 / 1.68 GiB. These are observations, not complete profiler results or causal
collector overhead. Collector sampling wall time includes waiting and is not collector CPU time.

Container samples numbered 429/475/409/442/404/444. Unavailable sample counts were 5/4/4/3/4/4 and
remain in the originals. `monitorValid` establishes monitor execution, not complete resource capture.
Docker CPU, memory and block-I/O values are rounded by its CLI and are reported as approximate
sampled values, not exact byte lower bounds. The shared collection container covers multiple
databases; its resource figures are not assigned to an individual case.

TCP figures count host endpoints touching owned mapped ports, by hexadecimal state. They can include
both sides and TIME_WAIT; they are not PostgreSQL sessions. The Linux observations do not establish
Windows address-reuse behavior or close #391.

## Preserved boundaries and saving decision

Ordinary fixtures retain GUID databases, EnsureCreated and Pooling=false. HTTP topologies retain
separate primary/history databases, fresh epochs, real migrations and two independent processes.
Snapshot paths still execute actual pg_dump and plain-SQL `psql -v ON_ERROR_STOP=1`; independent
history is excluded from the primary backup. Legacy checks still archive/build the pinned old binary
and exercise the physical route through the real upgrade chain. Assertions, cancellation tokens,
expiry/drain/retention windows and cleanup ordering are unchanged.

Host readiness is about 26.9% of the plain command median and is the strongest fixture candidate.
Saving 10% of total duration solely there would require roughly a 37% reduction in its measured
cost, assuming other work stays constant. Overlapping independent host starts offers a theoretical
budget, **not a validated saving**: start ordering, contention, original failures and cleanup need
separate proof. Artifact work is far too small for the provisional 10% target. Omitting migrations,
cloning/shared schemas, sharing seeded databases, host reuse or global pools would change important
boundaries and are rejected. No safe 10% fixture reduction is demonstrated or claimed.

Use the measured phase/resource map for #405's separately isolated whole-class shard experiment.
That experiment still needs complete case inventory, identical product builds, supported coverage
merging and a fail-closed required Test result. Dedicated browser bootstrap uses port 8185 and is
outside these ordinary backend runs; future shard work must audit it and all artifact paths.

## Reproduction and retained originals

Run from a fresh, reviewed checkout on Linux with Docker and the required .NET SDK installed:

```bash
dotnet build --configuration Release
python3 -B scripts/backend_cost_tests.py
python3 -B scripts/measure-backend-cost.py TestResults/fixture-cost-new
python3 -B scripts/analyze-backend-cost.py TestResults/fixture-cost-new
```

The output directory must not exist. Defaults are three alternating pairs. The runner preserves the
current invocation through completion, then refuses subsequent invocations for failed/nonpassing or
missing cases, changed inputs/build/SDK, invalid probe, failed monitor execution or a coverage-gate
failure. It never retries a test. Sampler availability errors remain visible for interpretation.
`APEXRACERS_COST_PROBE_DIR` enables C# spans; unset means no probe writes. Only numeric owned-process
and container samples are collected. Each command receives its own `DRIVER_PRIVACY_EVIDENCE_DIR`.

Originals remain locally under `TestResults/fixture-cost-404-matched` and the separately bound
`TestResults/fixture-cost-404-focused`. Public evidence contains their hashes and sanitized aggregates,
not raw snapshots or receipt payloads. The earlier user-interrupted sequence remains separately at
`TestResults/fixture-cost-404-original` with exit -2 and an explicit stop marker; it is not a completed
comparison. Preflight build failure CS0619 and the accidentally launched earlier-binary helper report
remain labelled as development/stale-build evidence. Final-source helper validation passed before
starting the six-run sequence. No failed original was substituted with a later passing artifact.
