# Authorized percentile refresh

Implementation for [#427](https://github.com/jwh3times/apexracers/issues/427), following
[ADR 0007](../adr/0007-percentile-history-is-scoped-by-evidence-selection.md) and
[the storage cutover](percentile-history-storage.md).

The subsequent [coherent readers](percentile-history-readers.md) add catalog discovery, immutable
snapshot witnesses, idempotent unchanged history reads and controlled Personal dispatch. Current
analytics and fresh seeding use classified history; historical staging and measurements below
retain their original slice scope. No Real adapter or inherited catalog approval is established.

## Shared production

`Data.PercentileRefresh` owns one explicit Series/Week/Car batch: source eligibility, exact Track
and Week windows, deduplicated Driver Fields, Personal Best selection, presentability, preparation,
source versions and atomic classified replacement/removal. API percentile calculation,
recommendations, Week Detail and Strategy pass the authenticated User explicitly. Only their
currently resolved synthetic Demo Subject can refresh Demo history; arbitrary Driver lookups stay
transient. Stored claims never recover a history owner, and quarantined uploads are not inputs.

`PrivateUploadStore.CalculatePercentileAsync` delegates verified Personal calculation to the same
module. It rechecks the original grant, revision and independent journal, and uses only currently
available typed private uploads. Personal Fields retain their official-session requirement; Demo
Subjects use the separate owned synthetic Race Evidence policy. Fresh Demo seeding delegates a
multi-User batch under its explicit seed policy and Race-only classification. Identical seed inputs
preserve existing readings and clocks. Ordinary refresh replaces the exact current identity.

Missing Personal Best or Field below five removes an existing classified contribution. Diagnostic
arithmetic remains available for undersized Fields. Independent canonical selections and owners
retain separate identities; a second same-Week refresh replaces rather than appends. An upload
selection changes only its own history, including its implicit Unknown session-type inclusion.

## Preparation and commit

The module captures purpose/generation/version before reading inputs. Official rows and their copy
versions are read together; typed upload sessions/laps and versions use a separate bulk query.
The matrix includes empty targets, so a newly arriving source cannot evade an empty-set witness.

One PostgreSQL transaction takes coordination lock 370 and then `SHARE NOWAIT` locks on Seasons and
Weeks. Existing catalog mutation contention refuses the refresh as unavailable evidence; it does
not retry or release fresh output. This order avoids waiting behind a catalog writer while holding
coordination. These table locks hold the witnessed calendar and fallback-window insertions stable through commit.

Final checks compare the complete current source inputs, the target Week descriptors and windows
computed from the full Series calendar, current purpose and original owner authority. A correction,
loss or addition invalidates the prepared batch. Replacement/removal invalidates descendants,
records earliest loss before physical removal and preserves old marker/source clocks. All new
readings retain the original preparation stamp; application code advances the purpose once for the
logical batch. Existing physical-delete fences may also advance its version.

Recommendations average presentable fresh readings through the requested Week's witnessed
StartDate, with each distinct Week counted once across Seasons. Later Weeks are excluded and an
empty average is null. Presentation, recommendation ordering and world-record overlays remain
outside the refresh module.

## Scope at the #427 implementation slice

These are controlled synthetic and explicit retained-source batches. Ordinary Real routes remain
unavailable; returned calculation data is not a protected HTTP result or Live permission.
[#428](https://github.com/jwh3times/apexracers/issues/428) owns broader coherent readers and protected
integration. User analytics and the old Seeder compatibility step still use the separate legacy
family. Classified refresh never falls back to those rows or infers their original selection.
Caller enumeration of Week IDs is an explicit batch limit: newly inserted Weeks after that
enumeration are not a claim of complete catalog discovery. No acquisition occurs to fill history.

## Measurements and reproduction

On a disposable migrated PostgreSQL fixture, a one-combination refresh and a six-combination
(three-Car/two-Week) refresh each used **36 client SQL commands, 22 SELECTs and two source reads**.
The one-combination case materialized 10 source rows across its initial/final reads; six combinations
materialized 60. Recorded arithmetic was 0.0399/0.0408 ms and wall time 78.9794/92.4908 ms respectively.
These are small synthetic observations, not a scale-performance guarantee. Source-query counters
exclude catalog, authorization and mutation bookkeeping; the test interceptor separately counts
all client commands. Personal batches use four source reads, including typed uploads.

With Docker running, from the public root:

```bash
dotnet test --project src/ApexRacers.Tests/ApexRacers.Tests.csproj \
  --filter-class ApexRacers.Tests.Services.PercentileRefreshTests \
    ApexRacers.Tests.Services.PrivateUploadLifecycleTests \
    ApexRacers.Tests.Seeder.DemoPercentileSeederTests \
    ApexRacers.Tests.Services.PercentileCalculationServiceTests \
    ApexRacers.Tests.Services.PercentileCalculationServiceUploadedLapTests \
    ApexRacers.Tests.Services.CarRecommendationServiceTests \
    ApexRacers.Tests.Services.CarRecommendationServiceUploadedLapTests \
    ApexRacers.Tests.Services.PercentileHistoryCharacterizationTests \
    ApexRacers.Tests.Services.StrategyServiceTests \
    ApexRacers.Tests.Restore.DriverSnapshotRestoreTests \
    ApexRacers.Tests.Services.EvidenceCopyLifecycleTests \
  --report-xunit-trx --report-xunit-trx-filename combined-final.trx \
  --results-directory "$PWD/TestResults/427-refresh"
```

The combined run passed **137 tests**, with zero failures or skips, in **4m 23.959s**.
The suite includes successive five/four/five Fields, missing PB and source loss, simultaneous
selection histories, verified multi-Car Personal calculation, original preparation stamps,
whole-batch rejection after an empty target acquires a new Field, catalog contention and actual
post-witness mutation blocking until commit. API cases cover both writers, another Driver,
wrong Track, cross-Season chronology and an intercepted requested-date correction. Restore checks
retain independent journal denial and original clocks. Artifacts live under ignored
`TestResults/427-refresh/`; full suites, coverage, browser and privacy acceptance run in CI.
