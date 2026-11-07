# Independent journal adapter

Implementation for [#378](https://github.com/jwh3times/apexracers/issues/378). It follows the
[lifecycle admission spine](driver-lifecycle-admission-spine.md) and the
[atomic publication reservations](atomic-publication-reservations.md) record, against the journal
contract the maintainer selected for the slice
([contract summary](https://github.com/jwh3times/apexracers/issues/378#issuecomment-6699806321),
[agent brief](https://github.com/jwh3times/apexracers/issues/378#issuecomment-6700703309)).

The slice adds the production `IDriverEnforcementJournal` and `IPublicationHistoryAuthority`, as one
type, over write-once cloud blob storage that a primary-database restore cannot reach. It doesn't
provision that storage, create its keys, assign roles or lock its retention policies; those are a
maintainer's private provisioning procedure. It doesn't enable Real provenance either. Ownership
proof and operating controls remain unavailable, so Real Driver scopes stay closed even with the
journal configured.

## One type, two authorities

`BlobDriverJournal` implements both interfaces. The Driver authority and publication stores apply
the history checks only when the journal they hold `is IPublicationHistoryAuthority`; a journal-only
adapter would skip them without error. The registration resolves the same singleton for both
interfaces.

## Records

Every fact is its own immutable block blob, created only if its name is free (`If-None-Match: *`).
The adapter never overwrites, deletes, appends to or sets metadata on a blob, so it needs no delete
permission and is compatible with version-level immutability.

| Container   | Name                                         | Record                                              |
| ----------- | -------------------------------------------- | --------------------------------------------------- |
| enforcement | `intents/{user}/{scope}/{operation}`         | One `DriverLifecycleIntent` per operation           |
| enforcement | `reconciled/{user}/{scope}/{operation}/{revision}` | A reconciliation of that operation at that revision |
| history     | `genesis`                                    | Epoch and key fingerprints (plaintext JSON)         |
| history     | `releases/{sequence}`                        | One reservation (`PublicationAccounting` as reserved) |
| history     | `dispatched/{release}`                       | Dispatch start, bound to release and incarnation    |
| history     | `terminal/{release}`                         | Checkpoint outcome, bound to release and incarnation |

`{user}`, `{scope}`, `{operation}` and `{release}` are HMAC-SHA256 segments under the *name* key, so no
name carries a User ID, Customer ID or release ID. Payloads are AES-GCM encrypted under a separate
*payload* key, with the blob name as associated data: a record copied under another name fails
authentication rather than reading as a different fact. The records must decrypt, not just hash,
because `IPublicationHistoryAuthority.ReadAsync()` returns raw `RecipientUserId` and dependency
`DriverScope` values that the stores compare.

## Semantics

The adapter reproduces the controlled PostgreSQL fixtures' behavior; a shared contract suite runs the
same scenarios against both.

- **Journal-first intent.** A re-append of an operation returns the stored record with its original
  loss time. The same operation ID with a different grant, scope or kind is refused.
- **Reconciliation** requires the stored intent. The minimum revision is the highest reconciled
  revision, and reconciled intents stop being pending.
- **Deletion** is User-wide and permanent: `ReadUserAsync` lists every scope of the User and returns
  the earliest deletion, whether reconciled or not.
- **Reservations** claim `releases/{n}` for the next contiguous `n`. A writer that finds the slot taken
  re-reads and claims the next; finding its own proposal there means its earlier response was lost.
- **Dispatch and checkpoint** keep the first clock. Dispatch writes and then checks for a terminal
  record. A proven-unsent checkpoint writes and then checks for a dispatch record. One of any two
  racing writers therefore sees the other and reports failure.
- **Conservative fold.** A release that has both a dispatch record and a proven-unsent checkpoint
  reads as terminal and possibly sent (`ProvenUnsent = false`). Under write-once retention, a stricter
  rule (making the whole history unavailable) would close publication until the records expired.

## Uncertain writes

The vendor documentation doesn't say whether a request that timed out committed. The adapter retries
the same deterministic name, and an occupied name resolves to the stored record, which the adapter
compares with what it meant to write. The documentation lists **412** for an unmet `If-None-Match`.
The emulator answered **409 `BlobAlreadyExists`**, so the adapter treats both as "already exists".
If the outcome is still unknown when the client's retry budget runs out, the operation throws:
journal writes throw `InvalidOperationException`, history writes throw
`EvidenceCopyUnavailableException`. A journal-first caller then doesn't proceed, while a veto that
did land still applies.

## Fail-closed reads

Every operation first confirms `genesis`. Its epoch must equal the configured trusted epoch, and both
key fingerprints must match. A wrong name key would otherwise compute different names, find nothing
and read as permissive. Reads report unavailable, never empty, when:
- genesis is missing or differs
- a record fails decryption or sits under the wrong name
- a reconciliation has no intent
- the reservation sequence has a gap or a duplicate
- a dispatch or terminal record has no reservation (which also reveals a lost newest reservation)

Cancellation propagates rather than reading as unavailable.

Deleting an unreconciled intent outright is not detectable from the journal alone. Protection
against that rests on the account's locked version-level immutability, which the operator
procedure verifies before locking.

## Configuration

`IndependentJournalRegistration.Bind` reads these settings. Their values come from the runtime
secret store through the usual hyphen-to-underscore mapping.

| Setting                                     | Value                                                        |
| ------------------------------------------- | ------------------------------------------------------------ |
| `INDEPENDENT_JOURNAL_ENDPOINT`              | Blob service endpoint (`https` only)                         |
| `INDEPENDENT_JOURNAL_ENFORCEMENT_CONTAINER` | Enforcement container name                                   |
| `INDEPENDENT_JOURNAL_HISTORY_CONTAINER`     | History container name (different from enforcement)          |
| `INDEPENDENT_JOURNAL_NAME_HMAC`             | Base64 of 32 random bytes                                    |
| `INDEPENDENT_JOURNAL_PAYLOAD_AES`           | Base64 of 32 different random bytes                          |
| `INDEPENDENT_JOURNAL_TRUSTED_EPOCH`         | Non-empty GUID, matching genesis                             |
| `INDEPENDENT_JOURNAL_WRITE_GENESIS`         | Optional `true`/`false`; default `false`                     |

With no settings, the unavailable adapters remain, silently. If any setting is missing or invalid,
the unavailable adapters remain and startup logs a warning naming the settings, never their values.
Only a complete, valid set selects the blob adapter, which authenticates with managed identity
(`DefaultAzureCredential`); there is no connection-string or shared-key path. Only the API host
registers the journal; ingestion never resolves it.

`INDEPENDENT_JOURNAL_WRITE_GENESIS=true` is a one-time provisioning switch. The adapter writes
genesis (conditional create) only when both containers are completely empty. Records without a
genesis are never re-anchored. The operator removes the setting once genesis exists.

No production code constructs `PublicationReleaseStore` yet. When a controlled or Live publication
path does, it must pass the same trusted epoch.

## Evidence

Local Debug runs, 2026-11-06, against `mcr.microsoft.com/azure-storage/azurite:3.37.0` (with
`--skipApiVersionCheck`) and the pinned PostgreSQL fixture:

- `BlobDriverJournalTests` (22) and `BlobPublicationHistoryTests` (13) cover:
  - idempotent appends and original clocks
  - reconciliation and the User-wide deletion veto
  - genesis absence, opt-in, re-anchoring refusal and concurrent provisioning
  - wrong epoch, wrong name key and wrong payload key
  - tampered, moved and unknown-format records
  - a lost response inside the retry budget, and one that exhausts it
  - eight writers racing for sequence slots, behind a barrier with collision counts
  - both dispatch/checkpoint interleavings, forced by a gate
  - sequence gaps and orphaned dispatches
  - cancellation
  - write-once request discipline, and no raw identifiers in names, metadata or payloads
- `IndependentJournalRegistrationTests` (19) cover: complete configuration resolves one instance as
  both authorities, and each missing or invalid setting keeps the unavailable adapters.
- `IndependentAuthorityContract` runs seven scenarios each against the controlled PostgreSQL
  fixture and the blob adapter.

For the conservative fold and the orphan check, mutating the implementation made the corresponding
tests fail, and restoring it made them pass.

The emulator doesn't implement immutability policies, zone redundancy, RBAC or shared-key
refusal. Those are observed on the real account by the operator procedure before its policies are
locked; this slice establishes none of them.
