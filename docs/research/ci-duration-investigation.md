# CI duration investigation (#403)

Measured 2026-10-11. The main bottleneck is the serialized PostgreSQL collection, not frontend
testing or build time. Its 486 cases occupy 92–98% of backend elapsed time. Two isolated coverage
shards are the strongest candidate for reducing successful-check wall time; they require an isolation
audit and a fail-closed inventory/coverage aggregator before adoption. No workflow, test inventory,
coverage configuration, privacy collector or security window was changed by this investigation.

The [tooling research](ci-duration-tooling.md) provides official-source scheduling, coverage and
artifact constraints. Measurements are retained in
[CI baseline](../testing/ci-duration-baseline.json) and
[local probe](../testing/ci-duration-local-probe.json). Follow-ups are
[#404](https://github.com/jwh3times/apexracers/issues/404) for phase/full-suite measurements and
[#405](https://github.com/jwh3times/apexracers/issues/405) for a bounded shard prototype, blocked by
#404. Existing [#391](https://github.com/jwh3times/apexracers/issues/391) owns Windows connection
lifetime failures; Linux timing successes do not resolve it.

## Comparable retained baselines

PR head `bead7bb1e8c754a499aa7187b2b0d5d4d69ba675` and squash-merged main
`ba858126e6cccb1aac17a73c13611259333c916a` have the same Git tree:
`04960df99c8ce43dfbed4cd785ad45875e2478c6`. All four original backend reports have 1,316 Passed
results and identical sorted case-name inventory SHA-256
`12b681ad91c8b7a027b81c149c1fa171704467f158ece710625f69b8f92ec2d2`.
This establishes comparable source/inventory, not identical runner load or a controlled coverage A/B.

| Original run | Job wall | Backend step | Backend TRX wall | PostgreSQL span |
| --- | ---: | ---: | ---: | ---: |
| [PR general](https://github.com/jwh3times/apexracers/actions/runs/38109169800/job/114380937999) | 33m22s | 31m34s | 30m47s | 30m05s |
| [PR privacy](https://github.com/jwh3times/apexracers/actions/runs/38109169847/job/114380938314) | 19m57s | 18m02s | 18m01s | 16m35s |
| [Main general](https://github.com/jwh3times/apexracers/actions/runs/38110986246/job/114386323063) | 33m42s | 31m48s | 30m59s | 30m15s |
| [Main privacy](https://github.com/jwh3times/apexracers/actions/runs/38110986274/job/114386322850) | 24m56s | 22m33s | 22m31s | 21m30s |

General backend steps include build/restore and coverage; privacy builds first and uses `--no-build`
without coverage. TRX wall is its Times start-to-finish, not a sum of overlapping tests. PostgreSQL
span is earliest-to-latest case interval for classes explicitly in `PostgreSqlCollection`; interval
union is within 0.4s of that span in every report. Other collections overlap it. The parser uses
actual TRX definitions and counts theory executions separately; this timing parser is not an
acceptance gate or a discovery-based proof that a test inventory is complete.

Workflow-created to first-job-start queue delay was 3s/3s for PR and 2s/3s for main. The two test jobs
run concurrently: their successful-check critical path is about 33m22s/33m42s, not their summed
duration. Their combined job runtime is 53.32/58.63 runner-minutes. Including Format and Lint gives
53.95/59.18; main additionally used 2.87 minutes for deployments, totaling 62.05. These are summed
API job timestamps, not invoice/billing-rounded usage. Other workflows are outside that accounting.
Relative launch offsets are a few seconds and are recorded in the baseline JSON.

Main's Build and Deploy and privacy workflows both completed successfully. That records workflow
and deployment-job outcomes; it does not certify live Driver permission or production privacy.

## Where backend time goes

The [fixture](../../src/ApexRacers.Tests/Helpers/PostgreSqlFixture.cs) defines a single collection
sharing one pinned PostgreSQL container. xUnit's default collection scheduling serializes its test
classes. Each ordinary fixture request creates a fresh database and current EF schema; HTTP
topologies instead create independently migrated primary/history databases and real child hosts.
Both patterns explicitly disable per-database pooling. Increasing the thread limit alone cannot
split this collection. Safe resource changes must also address #391's Windows churn/disposal scope.

Class sums below are useful cost attribution within the serialized collection. They are not additive
wall-time estimates across independently parallel collections.

| Class | Cases | PR coverage | Main coverage | PR plain | Main plain |
| --- | ---: | ---: | ---: | ---: | ---: |
| DriverReferenceHttpTests | 55 | 520.1s | 521.2s | 286.5s | 371.6s |
| PublicationReleaseHttpTests | 38 | 330.8s | 324.9s | 174.5s | 228.5s |
| DriverLifecycleHttpTests | 23 | 219.7s | 224.1s | 119.5s | 155.8s |
| AuthServiceTests | 116 | 122.6s | 123.1s | 78.4s | 101.4s |
| EvidenceCopyLifecycleTests | 17 | 86.9s | 86.9s | 47.7s | 59.8s |
| DemoTransitionTests | 3 | 63.7s | 64.6s | 34.0s | 43.2s |
| DemoSnapshotRestoreTests | 1 | 56.9s | 60.8s | 24.6s | 34.7s |
| DriverSnapshotRestoreTests | 3 | 53.7s | 54.2s | 29.3s | 37.8s |

The first three classes account for 54–58% of total backend elapsed time. The slowest coverage case
is the migrated seed/teardown/fresh-session round trip at about 67s. Physical Demo restore costs
57–61s and the actual pinned old-binary upgrade case costs 20–21s. Those measurements include their
entire case paths; TRX alone cannot assign time to dump, restore, legacy build or teardown.

General backend step minus TRX wall is 47–49s, an upper bound on combined build/restore and runner/
collector work outside TRX. Privacy's combined backend+SPA build is 30–40s, Chromium install 19s,
browser cases 52–65s. Frontend coverage is 47–49s; the deployment-pause helper is 37s. They matter
for early failure feedback but cannot explain a 30-minute backend run.

## Controlled local coverage and setup probe

Six alternating plain/coverage invocations exercised one actual two-host method:
`DriverReferenceHttpTests.Eligible_discovery_detail_comparison_and_follow_use_actual_JWT_recipient_and_atomic_owned_dispatch`.
All six original invocations passed with fresh databases, histories and child processes. They used
one separately built, temporarily instrumented Release source on this Fedora host, .NET 10.0.112,
Docker 29.9.0 and the existing coverage settings. This is a cost experiment, not privacy acceptance;
no collector was started and its outputs cannot replace original CI evidence.

| Measurement | Plain median (range) | Coverage median (range) |
| --- | ---: | ---: |
| Whole command | 22.79s (22.76–22.90) | 26.39s (26.38–26.46) |
| TRX case duration | 12.389s (12.379–12.436) | 13.597s (13.538–13.732) |
| Create two databases | 0.189s | 0.204s |
| Independent history genesis | 0.117s | 0.125s |
| Primary migrations | 2.227s | 2.450s |
| Authority/copy/operating seed | 1.056s | 1.163s |
| First host ready | 1.567s | 1.852s |
| Second host ready | 1.550s | 1.825s |

Coverage adds 3.59s (15.8%) to median command duration and 1.208s (9.8%) to case duration here.
Host readiness and migration are measurable recurring costs. Command-minus-case includes framework,
fixture/container and collector lifetimes; the unprobed case remainder includes exercise, artifact
recording and shutdown. This probe does not isolate those components or ordinary `EnsureCreated`.
They are explicit work in #404 rather than invented attribution.

The full CI plain runs vary by 270s on the same source, while coverage runs vary by 12s. Consequently
the observed 8–13 minute coverage/plain difference cannot all be assigned to instrumentation.
Full-suite paired runs and phase/resource measurements remain necessary before choosing a fixture
change. No baseline failures were rerun or discarded in this investigation.

## Ranked proposals and validation targets

| Rank | Proposal | Measured opportunity / target | Cost and evidence tradeoff |
| --- | --- | --- | --- |
| 1 | Audit phases/resources, then prototype two isolated general coverage shards (#404 → #405) | Whole-class greedy allocation gives ideal PostgreSQL bins of 902s each vs 1,805s serial. Target median general Test ≤20m over ≥3 comparable runs. Current independent privacy gate still imposes a 20–25m floor. | Ideal arithmetic excludes repeated fixture setup, contention and merging. More runners; target combined test-job runtime ≤65 minutes. Fail closed on missing/duplicate cases, non-success prerequisites and invalid merged coverage. |
| 2 | Reduce demonstrably redundant fixture/startup work with bounded lifetime (#404, #391) | Probe measures about 2.23s migrations and 3.12s two-host startup per selected plain case. Conditional target ≥10% backend improvement after attributable measurement. | No observed savings yet. Never share a continuous history across unrelated instances, reuse live authority, remove a host, shorten windows or enable unbounded idle database pools. Windows validation remains separate. |
| 3 | Run frontend/repository/helper checks concurrently, preserving Test aggregation | Removes at most the measured 99–101s serialized post-backend tail, while frontend failures could report within roughly a minute. | Extra setup/runner time; cannot solve backend duration. Preserve frontend coverage and report exact required context on failures/cancellation. |
| 4 | Review sharing one original backend execution between coverage and privacy | Could avoid 18–23 minutes of duplicated backend step runtime per commit; primarily a runner-cost saving. | It would not halve critical-path duration: the coverage producer remains ~32m. High-risk change to source/build/instrumentation and original-result binding; separate explicit topology review required. |
| 5 | Build/restore artifact reuse or PR-only superseded-run cancellation | Build opportunity is bounded by current 30–49s setup measurements; no overlapping-push saving measured. | Artifact transfer/permissions/integrity and runner setup can exceed build savings. Cancellation reduces obsolete work, not one run's duration, and must preserve failures and cleanup. |

The selected next work is measurement and an isolated prototype, not adoption of a new production
workflow. A provisional ≤20m general-Test target would save roughly 13½ minutes in that job; combined
required-check improvement is bounded by the unchanged privacy job. Re-evaluate targets using at
least three matched baseline/prototype runs, preserving every original outcome. Zero failures in a
small sample does not establish an absence of flakes.

## Safeguards

The retained privacy manifests each have 43 rows, 149 inventoried artifacts, `syntheticGate=passed`,
`fullMatrix=not exercised` and `liveEligible=false`. Their original failure-sensitive collector,
admitted receipt, continuous history, useful positives and built-SPA cases remain unchanged.
Future inventory/topology changes require explicit justification and correctness/security review.

#405 deliberately leaves privacy execution alone. Any general shard aggregator must independently
retain the full discovered backend inventory, original TRX/fault artifacts, positive merged product
coverage denominators and both existing 85% gates. Use real PostgreSQL, actual independent two-host
HTTP writers and physical snapshot/restore/old-binary tests. Keep each scenario and original history
intact, isolate ports/databases/artifact paths, and reject missing, skipped, inconclusive, duplicate,
stale or failed shard evidence. Required checks must report without path filters. Cancellation and
failed prerequisites must not make an aggregator green. Meaningful expiry/drain/retention windows,
failure provenance and non-Live interpretation cannot be traded for speed.

## Reproduction

Retrieve original artifacts to separate directories; do not overwrite prior outcomes:

```bash
gh run download 38109169800 -n backend-test-results -D /tmp/403-pr-coverage
gh run download 38110986246 -n backend-test-results -D /tmp/403-main-coverage
gh run download 38109169847 -n driver-privacy-contract-38109169847-1 -D /tmp/403-pr-privacy
gh run download 38110986274 -n driver-privacy-contract-38110986274-1 -D /tmp/403-main-privacy
python3 scripts/ci-duration-report.py \
  /tmp/403-pr-coverage/backend-tests.trx \
  /tmp/403-main-coverage/backend-tests.trx \
  /tmp/403-pr-privacy/TestResults/driver-privacy-contract/backend.trx \
  /tmp/403-main-privacy/TestResults/driver-privacy-contract/backend.trx
gh api repos/jwh3times/apexracers/actions/runs/38109169800/jobs
```

Repeat the last API query for the other three run IDs; the baseline JSON retains sanitized workflow,
commit, attempt, job and step timestamps. Downloads expire; the retained summaries include each TRX
SHA-256, inventory hash, counts and outcomes. Full downloaded artifacts, probe logs and original
instrumented-source backup are retained locally in `TestResults/ci-duration-403/`.

For the local cost probe, apply [the temporary patch](../testing/ci-duration-probe.patch) only to a
clean copy of `ReferenceTopology.cs`, build Release once, then run the method filter above six times
in the order plain, coverage, plain, coverage, plain, coverage. Use `--configuration Release
--no-build --report-xunit-trx --report-xunit-trx-filename probe.trx --results-directory <fresh-dir>`.
Set `CI_DURATION_PROBE_OUTPUT` to an absolute fresh JSONL path for each invocation and
`DRIVER_PRIVACY_EVIDENCE_DIR` to a separate artifact directory. Coverage runs additionally use the
unchanged repo coverage arguments from AGENTS.md. Measure command elapsed time with a monotonic
clock, preserve the original exit/log/TRX, and stop on a failure. Read phase seconds from JSONL;
these timings disclose no connection strings or tokens.

Reverse the temporary patch and rebuild before acceptance testing. The investigation did both and
verified that no probe changes remained in test source. Its instrumented runs were never presented
as unchanged-build privacy evidence. Script analysis was checked against all four retained reports
and their identical case counts, outcomes and inventory hashes; no full acceptance rerun is claimed.
