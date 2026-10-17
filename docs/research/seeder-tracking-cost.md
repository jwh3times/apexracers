# Demo cache seeding tracker cost

Measured fix for [#383](https://github.com/jwh3times/apexracers/issues/383), comparing baseline
`34bd095746cda869b2bfe3fa549de25503975489` with fixed source
`1d194db35d6aee6c97ad275e5a2b754e0f75a23f`. This receipt concerns the Demo cache phase;
race generation is separate.

## Change and correctness boundary

Before Demo cache seeding, `DemoCacheSeeder.SeedAllAsync` explicitly detects pending changes.
Only a graph with no pending writes is cleared from the EF tracker. A pending graph stays intact
and follows existing typed writer validation, including refusal of incomplete official Fields.
The caller's `AutoDetectChangesEnabled` value is restored before the first awaited seed operation,
even if detection fails. The change affects tracking overhead, not persisted evidence/source clocks,
seed input semantics, acquisition permission or Live eligibility.

The PostgreSQL integration class passed eight cases. It covers completed graphs with automatic
detection on/off, explicit and implicit pending changes, fail-closed partial-Field writes, rerun
source preservation and legacy Real/Unknown/quarantined-row preservation.

## Method and retained outcomes

The local harness runs the actual Release Seeder in child processes against disposable migrated
PostgreSQL 18.0-alpine databases. Captured-input and fully synthetic CI modes each have a fresh and
same-database rerun. Local captured inputs remain private; the receipts contain aggregate metrics,
not payloads or connection strings. Provider credentials are disabled. EF diagnostic events measure
DetectChanges elapsed/calls, saves and command elapsed; phase boundaries record tracker inventory
with automatic detection disabled/restored. Process CPU and wall time are recorded separately;
PostgreSQL pg_stat_statements supplies aggregate server execution/planning counters. These costs
can overlap and must not be added to explain wall time.

| Run | Outcome | Cache wall s | Process CPU s | DetectChanges s / calls | Saves | EF commands | PostgreSQL execution s |
| --- | --- | ---: | ---: | --- | ---: | ---: | ---: |
| Baseline captured fresh | Passed verifier | 2137.276 | 1439.147 | 1424.283 / 5658 | 943 | 15702 | 689.131 |
| Baseline captured rerun | Intentionally stopped | — | — | — | — | — | — |
| Baseline CI fresh/rerun | Not started | — | — | — | — | — | — |
| Fixed captured fresh | Passed verifier | 749.260 | 22.953 | 11.092 / 5659 | 943 | 15702 | 722.941 |
| Fixed captured rerun | Passed verifier | 806.470 | 25.576 | 9.040 / 6601 | 943 | 17728 | 780.828 |
| Fixed CI fresh | Passed verifier | 24.665 | 3.570 | 0.195 / 811 | 135 | 2137 | 22.817 |
| Fixed CI rerun | Passed verifier | 19.751 | 5.700 | 0.381 / 945 | 135 | 2476 | 17.220 |

Baseline and fixed captured fresh runs both produced 425 races, 85,000 results and 942 mapped cache
rows and passed the actual Demo verifier. Fixed output retained 425 official Field markers. The fixed
cache phase began with 85,000 tracked Result entries and ended with zero; baseline still tracked
85,000 afterward. The fixed boundary adds one explicit DetectChanges call (5659 versus 5658), while
saves, EF command count and PostgreSQL call/row deltas are unchanged. Fixed PostgreSQL planning was
5.518 seconds and provider command elapsed was 714.837 seconds.

Captured fresh cache wall time fell from 2137.276 to 749.260 seconds, with process CPU falling from
1439.147 to 22.953 seconds and measured DetectChanges elapsed from 1424.283 to 11.092 seconds.
This pair supports reduced tracking overhead; it does not attribute every elapsed second to the
tracker. PostgreSQL still performed the same operations/material, with server execution measured
at 689.131 baseline versus 722.941 seconds fixed. It is not a race-phase or whole-seeder speed claim.
The fixed captured rerun retained exactly 425 races, 85,000 results, 942 mapped cache rows and
425 official Field markers. Its official Field marker hash equals the fixed fresh hash, including
original marker clocks. Fixed CI fresh and rerun each retained 48 races, 2,880 results, 134 mapped
cache rows and 48 official Field markers, again with identical fresh/rerun marker hashes. All four
fixed passes completed and passed the actual Demo verifier.

These are single local instrumented observations, not a multi-run timing guarantee. Baseline
captured rerun was intentionally stopped and baseline CI never started; neither supplies a passing
comparison. There is no measured baseline-to-fixed CI or rerun speedup claim. The baseline race
phase took 405.685 seconds and is outside this cache tracking change. PostgreSQL remains the large
fixed cache cost; reducing SQL work is separate future work, not demonstrated by clearing tracking.

Local source/revision manifests, logs, aggregate diagnostics and the explicit interruption record
are retained under ignored `TestResults/383-baseline/` and `TestResults/383-fixed/`. The
instrumentation harness itself is local evidence, not a checked-in product command or CI change.
Small synthetic correctness tests do not establish full-input seeding performance. The public
tables above are aggregate observations; local input files, credentials and raw payloads are not
part of this receipt.

## Reproduce the durable correctness checks

With Docker running, from the public root:

```bash
dotnet test --project src/ApexRacers.Tests/ApexRacers.Tests.csproj \
  --filter-class ApexRacers.Tests.Seeder.SeederTrackingTests
```

For a new performance observation, use separate disposable migrated databases for captured and
CI modes, run the actual Release Seeder with `--demo` (adding `--ci` for synthetic catalog input),
then rerun on the same mode's database and run `--verify-demo`. Captured mode needs the existing
private response-object dataset; CI mode does not. Record source/input identity, phase boundaries,
tracker counts, process CPU/wall, EF diagnostic counters, PostgreSQL deltas and verifier outcome;
compare counts and official Field marker rows/clocks across reruns. Preserve failed, interrupted
and unstarted outcomes. The diagnostic harness used here is local, so these steps describe the
measurement method rather than promising a checked-in command that recreates its instrumentation.

No setup, schema, endpoint or operator procedure changes are required. Normal seed/verification
commands stay documented in README and the project guide.
