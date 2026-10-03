# Demo acquisition and mapped-evidence provenance

Implementation evidence for [#369](https://github.com/jwh3times/apexracers/issues/369),
covering DEMO-01, DEMO-02 and the namespace/classification portion of MIGRATE-01 in the
[accepted matrix](../design/driver-acceptance-and-rollout.md). This is local synthetic
engineering evidence. Lifecycle authority, proof, consent, publication admission, deletion
deadlines, independent prohibitions and production restore remain later implementation gates.

## Implemented boundary

`IRacingDataScope` is selected once per API request from current server-side feature flags
and role eligibility. Eligible Demo takes precedence over Live; otherwise the scope is
unavailable. A Customer ID, expiry date, incoming header or JWT Driver claim cannot establish
provenance. Subject Driver selection carries both Customer ID and provenance.

Mapped cache identity is `(Provenance, CacheKey)`. Identical keys and numeric Driver or race
IDs may exist independently in Real and Demo. Composite race parent/child keys prevent a
Real result from attaching to a Demo parent. Percentile snapshots, follows and BoP have
namespace filters and uniqueness constraints; weather uses separate Real and Demo columns.
Uploaded Laps remain user-provided evidence and are excluded from Demo aggregates.
The caller's upload inventory and import deduplication retain their ordinary User scope
during Demo preview; they read user uploads independently of synthetic Driver aggregates.

Demo reads only explicitly synthetic mapped evidence. Missing or expired Demo cache rows
return the established unavailable response without touching even a configured SDK client.
An unseeded follow target is unavailable; supported Demo follows derive their fictional
name from synthetic evidence rather than accepting a caller-supplied Driver name.

The response header `X-ApexRacers-Driver-Evidence-Namespace` reports the selected Driver
evidence namespace (`demo`, `real`, `unavailable`). It describes request selection, not the
origin of account fields or Uploaded Laps, and is not a processing or publication grant.
The persistent Demo banner remains the product-facing synthetic-data label.

Real mapped profiles, standings, leaderboards, qualifying, Time Trial and Driver search
drop provider Driver names before cache writes and returns. Race ingestion also omits the
provider name. Known warm mapped contracts are cleaned without renewing their original
freshness clocks. Raw SDK collections and untyped object contracts are rejected before
acquisition or Demo serialization. This does not certify the remaining lifecycle or
anonymous-publication requirements, and private caller-authored Real follow labels retain
their existing meaning pending the follow/disclosure slice.

## Migration and recovery

Migration `20261003030330_IsolateDemoProvenance` renames mutable evidence tables, changes
namespace keys and records pre-cutover row counts in `ProvenanceMigrationInventory`.
Existing mapped payloads move to `QuarantinedDataCaches`, preserving payload, `FetchedAt`
and `ExpiresAt`. Existing race, follow, snapshot, BoP and weather copies retain Unknown
origin and are unavailable to ordinary scoped reads. Negative IDs, known Demo Customer IDs,
and far-future expiry are insufficient evidence to classify a legacy copy.

The renamed tables and Real weather column fence old binaries: no compatibility views
permit them to write through the previous names. Migration rollback throws; recovery is
forward-only while retaining these fences. Inventory observation time is not an invented
loss/deletion deadline. Unknown copies are neither certified erased nor reclassified.
Full migration/restore reconciliation remains #377.

A deployed database with existing Demo copies requires a controlled fresh Demo seed before
re-enabling preview after cutover. Maintainers must stop old writers and seeders and keep
Live disabled. The private tracker/wiki holds that conditional operator procedure. Nothing
in this work inspects, migrates or reseeds production.

## Acquisition transition

Real acquisition checks for enabled Demo or any remaining explicitly Demo cache, race,
result, follow, percentile, BoP or weather copy before a provider call, and checks again
before caching a completed fetch. The ingestion worker checks before its first provider
request. Warm Real reads remain scoped to Real and do not acquire data.

`purge_demo_data.sql` requires Demo disabled, locks the relevant tables, and removes only
explicit Demo rows and weather. It preserves Real and Unknown evidence, reference catalogs,
accounts and user uploads. Stop Demo seeders first and require `--verify-teardown` before
simulating or proposing Real acquisition. These checks do not substitute for the later
distributed lifecycle/publication drain protocol or authorize live activation.

## Reproduction and evidence

The PostgreSQL test fixture uses a pinned PostgreSQL 18 container and an isolated database
per test. Tests exercise the real migration chain, composite constraints, the actual SQL
teardown script, repeated credential-free seeding and a controlled provider callback.

```sh
dotnet test --filter-class ApexRacers.Tests.Data.DemoProvenanceMigrationTests
dotnet test --filter-class ApexRacers.Tests.Data.DemoTransitionTests
dotnet test --filter-class ApexRacers.Tests.Services.DemoAcquisitionTests
dotnet test --filter-class ApexRacers.Tests.Services.IRacingRequestContextTests
```

Observed cases include legacy ambiguous payloads with preserved original clocks, old-writer
failure, refusal of rollback, same-ID namespace collision and cross-parent rejection,
idempotent seed, successful teardown before Real acquisition, and a stale Real fetch
completing after Demo starts without publishing or renewing its Real row. Acquisition tests
cover configured-provider Demo misses, warm collisions, typed name-free contracts and SDK
collection rejection.

The built-SPA Playwright test `web/e2e/demo-provenance.spec.ts` runs alongside the existing
gated-page audits with `E2E_DEMO=1`. Build the SPA into API `wwwroot`, seed with `--ci --demo`,
enable Demo for Standard in the isolated test database and configure the Development mail
drop as in `.github/workflows/e2e.yml`. No iRacing credentials or captured response files are
required. A new User with no Claimed Identity sees useful synthetic progression, profile,
race history and details; caller-selected provenance is ignored, and an unseeded search is
unavailable. Local verification on 2026-10-03 passed against the built SPA served by the API.

The full Release backend run passed 1,020 tests with 96.46% line and 92.76% branch coverage.
After review refactoring, all 50 Seeder tests passed again. Frontend verification passed the
production build, whole-tree Prettier and Oxlint checks, and 795 Vitest tests with 97.92%
line and 91.35% branch coverage. Generated agent configuration matched its sources and
all 14 generator tests passed. These measurements describe local synthetic fixtures,
not a deployed environment or the later integrated acceptance matrix.
