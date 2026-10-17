# Mapped Demo writes avoid unrelated Field preparation

Measured implementation for [#437](https://github.com/jwh3times/apexracers/issues/437), fixed source
`09f1db56e76440297db24fcbdbe7703044cf0e8f` (feature `331324b` plus an existing test clock
alignment). The measurements below concern the Demo cache phase, not whole-seeder elapsed time or a shipping claim.

## Narrow preparation capability

`SyntheticEvidenceWriter.OpenMappedAsync` opens a mapped Demo write without legacy or classified
Field discovery when no derivative is pending. It explicitly detects tracker changes first,
including implicit modifications while automatic detection is off. Pending legacy or classified
percentile derivatives retain full preparation at opening, with their existing distinct source
eligibility. Generic `OpenAsync` remains fully prepared.

The opened capability is immutable. A derivative added or implicitly modified after a mapped-only
opening is refused at save, and the failed session cannot retry. Preparation is never captured late
to legitimize a calculation. Purpose generation/version, complete source-set, eligibility and
original-clock checks remain in place; independent purpose source additions/removals still invalidate
prepared derivatives even when the writer's own synthetic purpose did not change.

When automatic detection is disabled, explicit detection immediately before the final EF save
persists internal copy-marker references, preparation metadata and purpose version. The caller's
setting is preserved. Existing typed-write checks and database fences remain effective. This adds
no schema, index, PostgreSQL GUC/statistics setting, runtime configuration or Live authorization.

## Correctness evidence

The focused mapped-preparation class contains 18 cases. Three repeated mapped upserts previously
caused six legacy discovery queries/rows and six classified discovery queries/rows; the optimized
path produces zero in all four counters. The auto-detection-off legacy/classified write cases first
failed with constraint SQLSTATE 23514, then passed after internal metadata detection was restored.

Cases cover pending/mixed writes with automatic detection on/off, late Added and implicit Modified
derivative refusal and poisoned retry, stale own-purpose refusal, original clocks, independent
source additions/removals, and distinct legacy/classified dependencies (three versus one for the
wrong-layout/untimed fixture). Full preparation must not collapse those different source rules.
All 18 new cases and the eight existing tracker-preservation cases passed. The scoped run initially
passed 65 of 67 cases; two existing aged-clock
fixture failures are retained in its original results. Aligning those fixtures to the actual system
clock used by PostgreSQL raw SQL made the two affected cases pass 2/2. This aligns the intended
nonexpired-weather test and changes no production fence. These runs are not a claim that the
original 67-case run passed uninterrupted. Full solution and Release builds passed with zero errors
and eleven existing warnings. Initial red and original run outcomes remain retained.

## Performance method and comparison limits

The four fixed passes use a byte-identical #383 startup hook. Use the same instrumentation as [the completed tracker-cost investigation](seeder-tracking-cost.md):
actual Release Seeder child processes, migrated disposable PostgreSQL 18.0-alpine, captured local
inputs or synthetic CI input, fresh then same-database rerun, verifier, phase counters and original
Field marker count/hash preservation. EF event counters and PostgreSQL execution/planning deltas
are separate from process CPU and wall time and can overlap. Keep captured inputs and credentials
private; publish only sanitized aggregate observations.

The comparable #383 fixed captured-fresh baseline cache phase was 749.260 seconds wall,
722.941 seconds PostgreSQL execution, 943 saves and 15,702 EF commands, with 425 races, 85,000
Results and 942 mapped rows. The #437 issue's earlier 57.740-second CI SQL investigation used
different instrumentation and is not a timing-comparison baseline.

All four fixed passes completed and passed the actual Demo verifier. Captured fresh and rerun
each retained 425 races, 85,000 Results, 942 mapped rows and 425 official Field markers; CI fresh
and rerun each retained 48 races, 2,880 Results, 134 mapped rows and 48 markers. Within each mode,
the complete official Field marker hash is identical across fresh/rerun, preserving original clocks.
The aggregate artifact also records successful owned-container disposal and runner exit 0.

| Fixed run | Cache wall s | Process CPU s | DetectChanges elapsed s / calls | Saves | EF commands | PostgreSQL execution s |
| --- | ---: | ---: | --- | ---: | ---: | ---: |
| Captured fresh | 26.279 | 23.588 | 16.023 / 6601 | 943 | 11934 | 2.868 |
| Captured rerun | 27.458 | 28.474 | 13.856 / 7543 | 943 | 13960 | 3.483 |
| CI fresh | 2.285 | 1.963 | 0.260 / 945 | 135 | 1601 | 1.083 |
| CI rerun | 3.967 | 4.339 | 1.875 / 1079 | 135 | 1940 | 0.274 |

The same #383 startup hook was used byte-for-byte (SHA-256
`B697958020CAB080E628D571164E2134B9D04998FCE5C7977B722B94800B20C6`). Compared with #383's
captured fresh run, cache wall time fell from 749.260 to 26.279 seconds (**96.49% lower**), while
EF commands fell from 15,702 to 11,934: 3,768 fewer, exactly four per 942 mapped writes. PostgreSQL
execution fell from 722.941 to 2.868 seconds. Process CPU was slightly higher, 23.588 versus 22.953
seconds; these results establish no process-CPU reduction or memory guarantee. The captured race
phase remained separate at 400.004 seconds, versus the earlier 376.010-second observation. No
whole-seeder speedup percentage is claimed.

These are single local instrumented observations, not a timing guarantee. Server execution,
provider duration, DetectChanges elapsed and CPU overlap; PostgreSQL returned-row counters do not
measure client materialized rows. Client Field materialization is tested separately by the three
mapped-upsert counter assertions. Rerun writes and marker preservation, not output counts alone,
remain part of correctness. Original #383 stopped/unstarted outcomes and #437's separate SQL
investigation retain their original meaning and instrumentation limits.

Receipts are local ignored `TestResults/437-fixed/summary.json`, `manifest.json`, per-pass tracking,
counts and logs. They contain aggregate diagnostics; captured input files and credential values
remain private. The actual measured source is the revision above, not a reconstructed timing from
the earlier SQL investigation. Full CI suites and coverage are separate pending validation.

## Reproduce the durable checks

With Docker running, from the public root:

```bash
dotnet test --project src/ApexRacers.Tests/ApexRacers.Tests.csproj \
  --filter-class ApexRacers.Tests.Services.MappedSyntheticPreparationTests \
    ApexRacers.Tests.Seeder.SeederTrackingTests
```

The benchmark harness remains local diagnostic evidence; it is not a new checked-in command or
CI topology. Normal seed/setup/verification commands remain unchanged.
