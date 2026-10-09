# Driver migration and physical snapshot restore

Public #377 rehearses MIGRATE-01–02, RESTORE-01–02 and participating copy/Demo obligations on
disposable PostgreSQL 18.0 databases. Ordinary Real ownership, collection-purpose, journal and
catalog adapters remain unavailable. These controlled synthetic scenarios do not admit a live
scope or replace the full #380 acceptance gate.

## Rollback fence

The pinned pre-lifecycle commit is `abf8050f3d12b0b667c3501d3246416a0c9feca8`.
The fixture archives that exact Git tree, builds its original API, and runs the actual binary with
provider/cloud credentials disabled. It first returns a synthetic User's private Uploaded Laps
with HTTP 200. Before this change the same binary also returned them **after** the current migration
chain: the ordinary API's route guard could not protect a rolled-back binary.

`20261009005518_FenceLegacyUploadedLapReaders` renames the physical legacy table to
`iracing.QuarantinedUploadedLaps`, preserves its rows, original telemetry dates, account/catalog
foreign keys and indexes, and records an observed unknown-row count in migration inventory.
No compatibility view recreates `UploadedLaps`. Both a running old binary across upgrade and a
fresh old binary after upgrade fail with HTTP 500 without private values; old SQL readers/writers
fail with undefined relation; a SQL reader explicitly prepared before upgrade is invalidated too.
Current ordinary startup returns generic no-store HTTP 503 for Telemetry while the independent
catalog remains useful. Actual downgrade migration execution
throws: recovery is forward-only.

This is application-binary rollback evidence on the **upgraded** schema. Restoring the entire
old schema and deliberately running only the insecure old binary discards the fences; no
application can retroactively make that combination safe. Restore activation requires current
migrations and independently current enforcement/history before opening any affected output.
Production deployment/activation controls and independently trusted adapter selection remain
separate work; these tests do not certify arbitrary administrator raw-SQL activation.

## Synthetic dataset and topology

`driver-restore-synthetic-v1` is defined in `src/ApexRacers.Tests/Restore/`. Legacy fixture values
use owned columns from the checked-in historical `AddKnownDevices` migration model; no unknown
provider shape is inferred. It includes an unverified account claim, unexpired/expired opaque
profiles, three Uploaded Laps with matching/missing/conflicting recorder IDs, a nominal official
race with two exact Driver members plus recorded team/AI gaps, percentile/Follow copies, BoP and
weather. The actual legacy schema is dumped, physically restored, then upgraded through the real
chain; no `EnsureCreated` substitutes for migration.

The inventory is two mapped profiles, one Subsession, two race results, one percentile, one
Follow, one BoP, one weather and three legacy Uploaded Laps. Unknown origin remains unavailable.
Claims, names, official flags and recorder IDs create no proof, consent or continuing retention
purpose. Original profile acquisition/expiry and telemetry dates survive quarantine; the inventory
observation timestamp is not a historic loss time. Ordinary startup may promptly erase unclassified
non-upload payloads through its existing cleanup service after this inventory is observed.

Current lifecycle scenarios use actual migrations, typed personal uploads, a derived exact owner
percentile, a tracked owner/name payload, stale prepared upload tokens and an independently
justified synthetic official Field. Two actual Kestrel processes serve protected HTTP against a
primary database; the current enforcement journal is in a **separate database excluded from the
primary snapshot**. Pre-withdrawal, pre-unlink and pre-User-deletion snapshots physically restore
old grants and copies, then restart both processes. Known current vetoes and unknown journal reads
both deny output before reconciliation. Canonical journal replay never renews original loss,
acquisition or removal clocks. Matching fresh synthetic proof/consent restores useful dormant
uploads only before the cutoff. After the original deadline, private sources, derivatives and
tracked payloads are physically absent, while exact official members and justified retention
remain. User deletion also covers a historical Customer ID, removes the account, retains minimal
enforcement and rejects historical/current/future Customer IDs.

The reference/history scenarios use actual JWT authentication and production controllers, two
Kestrel incarnations and separately persisted current journal/release history. Restored profile
names, opaque references and private Follows cannot revive a deleted Target. Replayed deletion
physically removes references/Follows and the Target account; unaffected Recipient personal output
remains useful. A different snapshot restores empty primary release rows while completed,
pending-before-dispatch and possibly dispatched actual writers exist independently. Output stays
closed until conservative accounting import; only each owned writer's real completion establishes
terminality. Missing history closes actual personal/discovery HTTP and does not authorize a fresh
catalog or namespace. The broader Publication tests also exercise independent continuity loss,
killed writers, new catalogs, physical source loss and lost checkpoints.

The Demo scenario restores a real synthetic `--ci` catalog and Demo cache. Ordinary API startup
serves useful no-store Demo profile output. It then disables Demo, runs the actual purge SQL and
verifies physical teardown **before** the Real acquisition simulation. Real remains unavailable
and the provider delegate is never reached. This is a negative activation check, not fabricated
Real proof or processing permission.

## Reproduce and inspect

Use Docker, Git, the .NET SDK from `global.json` and a clone containing the pinned legacy commit.
CI's Test checkout fetches full history. For a shallow local checkout fetch it first:

```bash
git fetch origin abf8050f3d12b0b667c3501d3246416a0c9feca8
dotnet test --project src/ApexRacers.Tests/ApexRacers.Tests.csproj \
  --filter-namespace ApexRacers.Tests.Restore
dotnet test --project src/ApexRacers.Tests/ApexRacers.Tests.csproj \
  --filter-namespace ApexRacers.Tests.Publication
```

The fixtures run `pg_dump --clean --if-exists --no-owner` and `psql -v ON_ERROR_STOP=1` inside the
pinned disposable PostgreSQL container. Restore destinations are restricted to unique loopback
fixture databases; no production connection is accepted. The legacy process build uses
`git archive` followed by `dotnet build --configuration Release`; it is neither a rewritten test
controller nor a simulated HTTP response. Temporary source/build files are removed after that
test, and fixture containers own the synthetic dumps/databases.

`TestResults/driver-restore/*.json` records dataset version, compiled commit, fixture/migration source hashes, snapshot SHA-256,
database identity, topology and observations. The legacy record includes the pinned original
commit, actual binary SHA-256 and old/current process IDs. Related writer events, incarnations,
clock/deadline and physical-state evidence are in `TestResults/driver-lifecycle/`,
`TestResults/driver-references/` and `TestResults/publication-ledger/`. CI uploads `TestResults/`.
Failed assertions fail the test run; artifacts alone are not acceptance approval.

`coverage.runsettings` excludes only the temporary archived legacy-binary directory from the
current-product denominator, alongside the existing generated/infrastructure exclusions. The old
binary still builds and executes in the test; current product assemblies stay included and both
85% thresholds stay unchanged. The filter uses the documented assembly-path selector in
[Microsoft's coverage configuration](https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage).
The ordinary current API process executes the production entry point and runtime/dependency files
copied beside the Tests assembly, using the same compiled module locations as the parent. Loading
another identical copy from the API project's output caused combined coverage to lose already
covered methods; a focused 19-case copy-lifecycle/legacy/Demo check verifies that coverage survives
the ordinary API processes. No current product code or child-process coverage is excluded.

Accelerated local clocks prove deadline handling for these copies. They do not establish deployed
backup lifetime, backup expiry, production restore safety, deletion outside application control,
production log retention or recall of bytes already delivered/exported.

## Local validation

Final combined #376/#377 validation at compiled commit `bbb3935` passed all 1,294 Release backend
cases, including all seven restore cases, with measured 96.03% line / 87.88% branch coverage.
The report contains measured current product code and no archived legacy modules; both existing
85% gates pass. All 814 frontend tests passed with 96.85% line / 91.04% branch coverage, and all
six built-SPA privacy cases passed against the final migration chain. Production build, Oxlint,
whole-web Prettier, generated-agent parity, changed-source whitespace and all 40 repository
coordination cases passed. The matching EF tool reported no pending model changes.

This is local implementation evidence. Shipping, PR CI and the production/pilot acceptance gates
remain separate.
