# Selection-scoped percentile storage cutover

Implementation for [#426](https://github.com/jwh3times/apexracers/issues/426), following
[ADR 0007](../adr/0007-percentile-history-is-scoped-by-evidence-selection.md) and
[the selection/owner contracts](percentile-history-contracts.md).

The later [shared refresh](percentile-history-refresh.md) integrates API writers, typed private-upload
calculation and fresh Demo seeding. Its current behavior supersedes the producer staging described
below. The subsequent reader contract supersedes the temporary legacy analytics staging. The migration and
historical evidence below retain their original #426 scope.

The subsequent [coherent readers](percentile-history-readers.md) add catalog discovery, immutable
snapshot witnesses, idempotent unchanged history reads and controlled Personal dispatch. Current
analytics and fresh seeding use classified history; historical staging and measurements below
retain their original slice scope. No Real adapter or inherited catalog approval is established.

## Staged storage families

The cutover separates classified history from the existing legacy runtime rather than inferring
selection or Driver authority for old rows. `CarPercentileResult` remains explicitly legacy and
maps to `LegacyUnclassifiedPercentileResults`; the former physical name is not a compatibility
alias. The new `ClassifiedCarPercentileResult` family maps to
`SelectionScopedCarPercentileResults` and has its own query surface and copy validation.

Old row identities, metrics, timestamps, marker references and source/copy clocks are preserved.
No backfill invents original selection, winning Personal Best or consent. Existing Demo callers
remain on the legacy family until shared refresh and coherent reader integration in
[#427](https://github.com/jwh3times/apexracers/issues/427) and
[#428](https://github.com/jwh3times/apexracers/issues/428). They never consume classified rows
through an unscoped legacy lookup. Conversely, new-policy history never falls back to legacy rows.
This is storage preparation, not a claim that the full runtime history policy has shipped.

## Classified owner and selection

Canonical selection and owner identity data live in Core so API, Data and Seeder can share one
policy without reversing project dependencies. Classification includes the current User, Driver,
provenance, owner kind and canonical policy selection for a Car, Series and Week. The observed
Personal grant revision is authority/version metadata, not another history identity on each change.
Demo Subject classification uses the actual synthetic Demo Driver independently of a stored claim.
The classified Customer ID column matches the existing integer `DriverScope`/grant/private-upload
contract so its composite grant binding can be enforced. This is not a statement about the range of
all upstream identifiers: only the fixed Demo Subject and currently representable Personal scopes
can enter this storage interface. Broader identity data remains `long`.

Personal rows must match their original grant's User/Driver/provenance and current revision. Demo
Subject rows belong to the explicit synthetic family and carry no Personal grant. No constructor or
database default supplies missing classification. Data identities and canonical keys remain
insufficient for acquisition, persistence or disclosure permission.

## Copy and retention coordination

Typed private-upload calculation and explicit fresh Race-only Demo seeding produce classified
readings. Their stable derivative identities include the owner and selection; one selection cannot
overwrite another. Typed Personal calculation retains its existing official-session source rule;
Demo Subject seeding uses owned synthetic timed Race Evidence under its distinct preview purpose.
Both require exact Week/Track/Car identity. This storage slice does not assert that those two owner
kinds have an identical acquisition/source policy or activate the later shared refresh module. Closed-batch validation and the database fences validate the same classification
against the marker/purpose family and key hash.

Purpose/generation/source versions are captured before reading inputs and rechecked at final
commit. Current authorization, complete input-set checks and protected dispatch remain necessary.
Recomputing a reading does not renew its source acquisition, expiry, loss or cleanup clocks. Old
selection-less rows are excluded from new-policy results, and recomputation requires currently
authorized retained sources rather than reacquisition or quarantine promotion.

Cleanup, purge, teardown and acquisition-transition checks inspect both storage families. Minimal
markers and enforcement history survive physical payload removal under their existing rules.

## Migration and restore evidence

The forward migration preserves old rows and fences the previous physical name. Prepared and
fresh old reads/writes must fail after upgrade. Tests apply the historical migration chain rather
than substituting `EnsureCreated`, probe classified identity/scope constraints, and exercise snapshot
restore followed by current migration and independent enforcement reconciliation.

A full old-schema restore deliberately activated with only an old binary discards current fences;
current migrations and independently current enforcement must run before affected output opens.
Forward-only migration recovery cannot authorize an unsafe downgrade. Local synthetic evidence
does not establish deployed backup expiry, production activation controls or Live eligibility.

Query-plan observations must use the actual owner/selection/Series and Week-refresh predicates
before introducing additional indexes. The two-row synthetic fixture selected the required User-ID FK index for an owner/selection/Series
query and the Week-ID FK index for refresh; each returned one row. No speculative secondary query
index was added. This is actual planner evidence, not a scale-performance claim.

## Reproduce

With Docker running, from the public root:

```bash
dotnet test --project src/ApexRacers.Tests/ApexRacers.Tests.csproj \
  --filter-class ApexRacers.Tests.Services.PrivateUploadLifecycleTests \
    ApexRacers.Tests.Seeder.DemoPercentileSeederTests \
    ApexRacers.Tests.Services.PercentileSelectionTests \
    ApexRacers.Tests.Services.PercentileHistoryKeyTests \
    ApexRacers.Tests.Services.PercentileHistoryContributionTests \
    ApexRacers.Tests.Services.PercentileHistoryOwnerTests \
    ApexRacers.Tests.Data.PercentileHistoryStorageTests \
    ApexRacers.Tests.Restore.DriverSnapshotRestoreTests
```

The final combined run passed **89 cases**, with zero failures or skips, in 106.664 seconds on
PostgreSQL 18.0-alpine. The local TRX is retained in ignored
`TestResults/426-storage/combined-final.trx`; the schema cases regenerate
`TestResults/percentile-history/storage-plans.json` with actual EXPLAIN JSON.

Migration `20261015011526_ClassifyPercentileHistory` is forward-only. The migration tests prove
prepared and fresh old SELECT/INSERT/UPDATE fail with SQLSTATE `42P01`, preserve the pre-cutover
unclassified row identity/rank/computation clock across a real dump/restore/upgrade, and create no
classified backfill. Separate current typed-copy restore cases retain copy acquisition clocks and
deny restored authority after independent journal replay. They are complementary cases, not one
claim that every historical schema and activation topology was tested.

Producer cases cover distinct selections and owners, exact identity replacement, current source
sets, source loss and a new official Field committed after calculation. The stale calculation is
refused and a fresh one uses the expanded Field. Fresh/rerun Demo seed cases preserve clocks,
selection/legacy isolation, actual Demo Subject identity and wrong-layout exclusion. Full suites,
coverage and the integrated privacy contract run separately in CI.
