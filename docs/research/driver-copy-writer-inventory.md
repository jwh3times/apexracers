# Driver evidence and name-copy writer inventory

Implementation for [#371](https://github.com/jwh3times/apexracers/issues/371), based on
merged main `055e2ccd4cb6be5aa7357b3aaa8c9818c0207f4e`, reviewed on 2026-10-05. The inventory
records application/database enforcement and controlled synthetic evidence. It does not establish
deployed retention, backup expiry, restore safety, live processing permission or publication admission.

Read the [accepted ownership architecture](../design/driver-disclosure-ownership.md),
[COPY acceptance rows](../design/driver-acceptance-and-rollout.md), and
[implemented lifecycle spine](driver-lifecycle-admission-spine.md) together. Existing namespace
selection, name stripping, TTLs and route refusal do not establish collection purpose.

## Physical copies and participating writers

| Copy | Participating writers | Enforced contract |
| --- | --- | --- |
| Mapped cache payloads | `CachedIRacingClient`, warm repair/refill; `DemoCache` synthetic upsert | Closed owned contract and explicit purpose, namespace, generation, original fetch/expiry clocks; warm reads and final commit both check current eligibility. |
| Race Evidence Subsessions and Results | Ingestion `Worker`/`SubsessionMapper`; Seeder ordinary and CI paths | Independent official evidence or explicit synthetic collection. Preserve exact Customer IDs, Field membership, Team/AI counts and measurements; Real Results have no Driver-name snapshot. Commit complete classified Fields atomically. |
| BoP and Week weather copies | `SeasonIngest`, Seeder/demo builders | Explicit evidence purpose and namespace; include both weather columns in inventory. Catalog metadata is distinct from personal Driver material. |
| Driver name authority | `DriverAuthorityStore.GrantAsync` | Verified original association and current Personal/Sharing purpose, with journal vetoes and generation coordination. No population-wide identity mirror. |
| Tracked names/personal payloads | `CopyLifecycle` / `DriverAuthorityStore.CommitCopyAsync` | Current grant/journal/generation. The bounded prototype payload may contain names and therefore receives the earlier 24-hour cleanup ceiling. Typed authorized names and concrete evidence dependencies live in the new stores; private-upload dormancy remains #372. |
| Private Follow association and legacy Rival names | `RivalService`, Demo interactions, maintenance SQL | Internal association and authorized names are separate. Real legacy service calls must not persist arbitrary caller-supplied names. Reference/follow publication belongs to #375. |
| Percentile derivatives | `PercentileCalculationService`, `CarRecommendationService`, synthetic seeding | Bind original User/Driver scope and actual sources/revisions; invalidate on source loss or rebuild solely from independently permitted evidence. Preserve exact owner arithmetic. Upload/dormancy reopening belongs to #372. |
| Uploaded Laps | Legacy `TelemetryUploadService`, synthetic Seeder | Legacy HTTP workflow stays unavailable in every namespace. Full attribution and dormant ownership contract belongs to #372; this slice cannot make a recorder or claim into ownership. |
| Quarantined mapped payloads and Unknown evidence | Provenance migration; maintenance/cleanup SQL | Preserve recorded provenance and original clocks. No key-prefix, sentinel, event-time or Season-ID inference can invent historical purpose or loss time. Unknown affected use remains unavailable. |
| Cleanup records | Existing `DriverCopyCleanup`, `ExternalDataCacheCleanupService`, maintenance scripts | Durable earliest deadlines, generation partitions and source markers; physically inspect applicable stores before reporting removal. Logical withdrawal, live erasure, overdue work and backup expiry remain distinct. |
| Operational output | Request/exception middleware, ingestion and hosted cleanup logging; framework/provider sinks | Trusted route templates and bounded diagnostic fields, without Driver names/IDs, tokens or telemetry. Local privacy checks do not certify external sink retention. |

No separate implemented backfill executable was found. The official collection interface must cover
authorized historical/backfill requests rather than leaving a future raw persistence bypass.
Both normal and CI Seeder paths participate; Developer tools and direct EF/bulk/SQL writes must
appear in the fence tests, rather than relying only on conventions at feature call sites.

## Purpose inventory

An owned, name-free contract is necessary but insufficient for shared official storage. Member
Profile, career, summary, recap, awards and rating charts remain Driver-specific payloads; removing
a display name does not turn them into independent official evidence. Recent-driver activity and
lap-detail requests likewise need the applicable scoped feature authority. Standings, qualifying,
Time Trial, leaderboard, world-record and race-guide families need their explicit feature-purpose
classification; a cache-key prefix alone is not approval. Their unintegrated Real routes remain
unavailable pending the corresponding publication/ownership integrations.

Explicit synthetic Demo copies retain useful synthetic names and deterministic seeded behavior;
they never establish Real grants or trigger provider acquisition on a miss. Existing Real/Unknown
rows without verified purpose metadata must not receive a permissive migration default.

Active-Season eligibility limits collection, separately from continuing official retention purpose.
A Season becoming inactive does not itself establish purpose termination. Personal withdrawal
does not remove independently retained official evidence. Termination requires an explicit durable
purpose transition and original clock. The accepted historical model is a current verified-owner
feature request or an admitted publication-catalog scope. Controlled owner-request fixtures require
a distinct request ID, Season and original grant revision. Request completion closes collection and
fences delayed receipts, while independently retained name-free official evidence remains eligible.
Personal withdrawal denies further collection without ending that continuing official purpose.
Ordinary Real ownership/catalog issuers remain unavailable until those integrations exist; catalog
admission belongs to #374. A standing owner grant alone does not authorize arbitrary backfill.

## Interface comparison

Three independent design explorations compared a small transactional interface, a typed writer
catalog and a default persistence guard. The implemented combination uses typed batches within Copy
Lifecycle, with EF and database enforcement as backstops. Feature callers capture scope, fetch/map
outside coordination, then commit through the same module that owns generation checks, physical
inventory, source dependencies and cleanup verification. An entire official Field is one batch.

An ambient context check alone cannot establish acquisition ownership or complete-Field atomicity;
a writer catalog alone does not fence old binaries or bulk SQL. A PostgreSQL session marker is
not proof of authorization when the same database role can set it. Schema changes and mixed-writer
tests must establish the actual contract, while ordinary independent Identity/catalog writes retain
their applicable behavior.

Retries and warm repairs preserve original clocks. A newly authorized collection has its own
generation and cannot erase prior cleanup work. Physical mapped expiry is bounded by original
expiry plus 48 hours, name removal by original unauthorized time plus 24 hours, and independent
official-purpose termination by original termination plus seven days. Backup and operational-log
retention require their separately tracked deployed observations; no synthetic time advancement
can establish those outcomes.

## Implemented boundary

`EvidenceCopyLifecycle` owns typed preparation receipts, atomic closed batches, original clocks,
grant/journal checks, durable withdrawal, dependency invalidation and verified live erasure. Copy
markers remain after physical deletion so a retry cannot reuse a forgotten key generation.
`SyntheticEvidenceWriter` binds a preview session before work, records contributing Field versions
before calculations, checks the complete source set at commit, and becomes unusable after failure.
It cannot import another purpose's private material. API Demo mutations, both Seeder paths, mapped
Demo upserts and BoP/weather builders participate. Worker collection resolves an issued purpose before
provider acquisition; `SubsessionMapper.ToBatch` preserves all classified Customer IDs plus Team/AI
counts and commits the classified Field together. Schedule payloads use a single closed batch.

The primary transaction uses the lifecycle's advisory lock `370`. Migration-installed PostgreSQL
triggers reject missing metadata and marker reuse from a previous transaction, including old binaries,
EF bulk operations and SQL. Copy identity, acquisition/expiry and prior closure clocks are immutable.
Physical deletion, weather replacement, clearing and Week deletion withdraw descendants and advance
the original purpose version. Read filters withhold unknown, closed, removed and obsolete copies;
authorized name reads also require the current grant revision and name authority.

The cleanup service runs every thirty minutes and removes withdrawn/expired copies promptly. A
failed reconciliation rolls back physical erasure and completion claims together; restarted work
retains its original deadline and reports overdue state separately. Unclassified/quarantined legacy
payloads are erased without inventing purpose or resetting their acquisition/expiry clocks. Minimal
copy markers, lifecycle operations and proof binding metadata contain no retained payload/name.
Explanatory proof authority is erased by twelve calendar months; current consent versions and minimal
enforcement bindings remain distinct from explanatory audit material. There is no separate consent
narrative archive or temporary acquisition file in the current adapter inventory.

Application logs use trusted route templates and bounded failure types. Raw paths/queries, attached
exceptions and provider/framework payload-capable categories are excluded. External automatic
telemetry, access-log collection and sink retention remain operator evidence under private #28.

## Reproduction and evidence limits

With Docker running, execute from the repository root:

```bash
dotnet test --filter-class ApexRacers.Tests.Services.EvidenceCopyLifecycleTests
dotnet test
dotnet ef migrations has-pending-model-changes --project src/ApexRacers.Data --startup-project src/ApexRacers.Api
```

The lifecycle suite applies actual PostgreSQL migrations for old-writer and source-deletion checks.
It covers delayed writes after closure/restart, competing preparations, unchanged original deadlines,
expiry/source invalidation, unauthorized namespaces, scoped name cleanup, inactive-Season collection
versus retention, explicit historical requests, raw weather clearing/deletion, partial contributing
Field deletion, failed cleanup and recovery, and complete ingestion mapping. A migrated CI/Demo
round trip seeds 48 Subsessions and 2,880 Results, verifies every Demo family, runs the actual purge
SQL, rejects a delayed seeder and reopens only through an explicit fresh preview.

Legacy query/mapping unit tests use clearly marked already-retained synthetic fixtures. Their
metadata installation and SDK substitutes are not runtime proof, consent or Real collection issuers.
Ordinary Real acquisition is deliberately closed before the provider callback; these tests make
no successful live fetch or Real authorization claim. Future issuers must join the same lifecycle
and acceptance barriers before being registered.

Validation on 2026-10-05:

- The full backend suite passed: 1,115 tests, no failures or skips. The ignored local artifact is
  `TestResults/driver-copy-fencing.trx`.
- After the final batched schedule/erasure changes, all 24 focused lifecycle and Season ingestion
  tests passed. The final legacy unclassified-payload test also passed after adding its explicit
  Real-context assertion. The full suite was not repeated after those final changes.
- Frontend validation passed: 797 tests in 69 files, lint, production build and whole-tree Prettier.
- Repository coordination passed all 40 tests. The EF model has no pending changes; agent generation,
  neutrality, C# whitespace and Git whitespace checks passed.
- Standards and spec reviews reported no remaining findings after the final fixes.

## Controlled migration and deployment

Migration `20261005162541_EvidenceCopyFencing` refuses enabled or physically populated Demo. Stop
all old writers and disable Demo/Live before the reviewed purge and migration. Existing Real/Unknown
rows receive no permissive purpose metadata. Legacy non-Demo name snapshots are cleared, and original
clock/namespace records are preserved until their affected payloads are physically erased.
Down refuses to remove the fences. Recovery is forward-only with current enforcement and copy history.

After a post-migration teardown, ordinary seeders cannot reopen the old preview. Use
`--new-preview --ci --demo` (or captured catalog inputs without `--ci`) and verify before enabling
Demo. The [conditional operator handoff](https://github.com/jwh3times/apexracers-private/issues/35)
must be fulfilled before the automatic main deployment; its private execution instructions are
linked from that issue. This branch does not change production.
The 8-day name-backup ceiling, 7-day backup lifetime, 30-day operational-log retention and restored
copy enforcement require separately observed topology/configuration/recovery evidence; local clock
advancement and successful live-row cleanup do not certify them.
