# Private upload and dormant-data lifecycle

Implemented synthetic scope for #372. This record describes the application modules and their
PostgreSQL/HTTP evidence. Production Telemetry routes remain unavailable before body binding:
provider ownership proof and the independently selected production journal are still unavailable.
Nothing here activates Live, grants real ownership, admits sharing, or verifies deployed backups.

## Attribution and retained sources

`TelemetryUploadService.ProcessAsync` produces only a transient preview containing persistence
status, lap counts and best time. It never retains laps or returns recorder IDs/names, recording
YAML or untrusted catalog labels. `ProcessSyntheticAsync` requires the original User/Driver's
current verified Personal grant before parsing, compares the recorder only with that verified
binding, and captures a receipt before preparation. Missing/mismatched recorder outcomes are
identical and generic. Claims, passwords, feature flags and recorder IDs are not ownership proof.

`PrivateUploadStore` owns the closed typed persistence operation. It rechecks independent
User/Driver enforcement, grant generation, purpose version, catalog references and timed laps under
PostgreSQL coordination before saving. Only controlled Demo provenance is accepted. `PrivateUploadSessions`
and their cascading `PrivateUploadedLaps` retain no raw bytes, names, YAML or credential values.
The unique original User/Driver/car/track/recording identity and Lap Number preserve idempotence.
The old `UploadedLaps` are not promoted into this store or guessed to have proof/consent.

The processor owns and disposes its input stream on success, cancellation, malformed content,
missing authority and attribution failure. It creates no staging file or raw archive, leaving no
module-created orphan material to sweep. Ordinary production denial precedes multipart binding;
the controlled HTTP test host uses small synthetic multipart fixtures. This does not certify the
retention of arbitrary external/framework stores or a future production upload transport.

Migration `PrivateUploadLifecycle` installs kind-8 copy markers and private source/lap SQL fences.
Raw, bulk and stale writes cannot reuse an earlier transaction's marker, change the original
association or mutate retained laps. Source/lap deletion invalidates descendants. The later
`SeparateDriverEnforcementHistory` migration removes the proof receipt's User FK: minimal bindings
can survive physical account erasure. Grant issuance, resolution and admission require a current
User. A User-delete database fence requires all historical grants closed, completed User-wide
operations and terminal writers before account erasure; direct old Identity writers cannot bypass
that ordering. Both migrations refuse Down; recovery preserves current enforcement and proceeds forward.

## Recovery, deletion and clocks

Personal withdrawal, unlink and invalid proof immediately close acquisition/read/publication.
Typed private sources remain unavailable during the bounded recovery window. Matching original
User/Driver ownership requires a fresh proof receipt and affirmative current Personal consent.
Before day 90, regrant creates a new marker generation for retained sources while preserving their
original acquisition time. Old purpose/marker loss/deadline history is immutable, and dependent
summaries remain invalidated until recomputed from current authorized sources.

At day 90 ordinary dormant reactivation ends. Reconciliation physically removes deletion-due
sources during days 90–97; the deadline remains original loss plus 97 days through retries/restart.
A different Driver or User cannot inherit them. `GrantFreshCollectionAsync` is a separate explicit
operation permitting newly acquired evidence after the recovery cutoff; it restores no dormant
copies and cannot override a User-deletion tombstone.

DeleteUser is a User-wide journal veto, including historical and future Customer IDs. The durable
root intent precedes one atomic primary closure of every association. Deterministic child operation
IDs retain the root's original action time. All affected admissions must reach trusted terminal
checkpoints before withdrawal is acknowledged; neither lease expiry nor process replacement proves
terminality. Interrupted operations replay their canonical journal intent.

Deletion never receives dormant grace. It tightens all owned source deadlines to the earlier
applicable boundary or original request plus seven days. Reconciliation removes private sources,
invalidated derivatives, legacy owned uploads and the User/profile/credential row; identity-owned
sessions/devices cascade. Minimal proof/grant/operation/copy history remains, without a profile or
name; explanatory proof authority is cleared. Opaque prototype payloads retain their stricter
24-hour ceiling. Independently justified official Fields survive Personal loss and User deletion.

`InspectUserAsync` separates durable withdrawal, retained/overdue work, verified live erasure,
seven-day live deadline and fourteen-day backup deadline. Backup expiry is always unverified here.
Synthetic time advancement and a primary delete do not establish expiry in a deployed backup,
log sink or restored environment. Independent journal/retention/restore follow-ups retain ownership.

## Useful owner analytics and protected HTTP

`ReadBestsAsync` returns exact all-time Uploaded Bests grouped by catalog IDs. Personal percentile
calculation uses only uploads in the same `RaceWeekWindow`, exact official Field membership and
`PersonalBest.Select` / `FieldPercentile`. A Race Best wins a tie, the owner is counted once, and
faster excluded-week uploads remain available in the all-time inventory. Typed derivatives record
both contributing private and independently retained official copy versions; source loss prevents
reuse and rebuild requires currently authorized inputs.

A newly contributed upload invalidates affected existing owner derivatives and advances the purpose
preparation version. Delayed upload/summary writers cannot commit an earlier input set after that
change. Snapshots retain the purpose generation/version captured before reading laps, and final
dispatch compares the complete current authorized source set, including newly added sources.
Percentile commits preserve that starting preparation version; official input rows and their copy
versions are read together. Earlier private sources retain their original clocks and remain available
while authorized.

`DriverPublication.ReadSyntheticUploadedBestsAsync` owns preparation and a protected result with
bounded JSON, `no-store`, final authority/source checks and actual writer admission/drain. A bare
User ID cannot publish old saved uploads. No controller releases an admission before serialization.
The ordinary API/TypeScript preview contract contains only `persisted`, lap counts and best time;
its UI distinguishes saved private laps from an unsaved preview without displaying recorder identity.
The separate process test host exercises actual multipart uploads and response writes using
isolated primary and independent-journal PostgreSQL databases. It is never registered by product startup.

## Reproduce evidence

With Docker running, from the repository root:

```bash
dotnet test --filter-class ApexRacers.Tests.Services.PrivateUploadLifecycleTests
dotnet test --filter-class ApexRacers.Tests.Services.TelemetryUploadServiceTests
dotnet test --filter-class ApexRacers.Tests.Lifecycle.PrivateUploadHttpTests
```

The suites cover useful matching uploads, generic mismatch/missing attribution, unverified previews,
forged scope and other owners, stale receipts and raw writes, original clocks, recovery immediately
before/at/after day 90, physical day-97 removal, separate fresh collection, historical Driver changes,
User-wide journal interruption/restart, seven- and fourteen-day boundaries, overdue opaque payloads,
pending writers on old/current associations, exact arithmetic,
source invalidation, uploads arriving during percentile preparation or before the first response
byte (including an empty prior inventory), official input loss during calculation, actual multipart
transport, suppression of unsent prepared output and protected response terminality. HTTP artifacts
under `TestResults/driver-lifecycle/` contain synthetic outcome/context metadata; TRX owns pass/fail.

This contributes UPLOAD-01, COPY-03–05 and AUTH-01–03 within the named synthetic boundaries. Real
proof exchange, production journal selection, publication catalog/composition admission, browser
invalidation, integrated restore acceptance and deployed backup/log expiry remain separate slices.
