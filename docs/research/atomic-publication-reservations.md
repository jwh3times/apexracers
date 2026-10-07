# Atomic controlled publication reservations

Implementation work for [#374](https://github.com/jwh3times/apexracers/issues/374), building on
the [whole-cohort candidate record](whole-cohort-publication-candidates.md) and accepted
[ownership architecture](../design/driver-disclosure-ownership.md). The authoritative brief is
[#374's Agent Brief](https://github.com/jwh3times/apexracers/issues/374#issuecomment-5962094538).

Status: implementation, focused verification and full Release verification complete; prepared for review and unmerged.
Observed outcomes are recorded below. This is controlled synthetic engineering, not actual maintainer
catalog admission, production journal selection, integrated acceptance or Live activation.

## Contract

Publication prepares the complete normalized cohort outside admission coordination. A fixed,
versioned catalog determines fields, scope and permitted variants; a request cannot provide its
own safe-review decision, owner Personal Best, arbitrary payload or custom source predicate.
The protected result owns serialization, admission, dispatch and the executor's terminal checkpoint.

Atomic review/reservation uses the existing PostgreSQL coordination boundary shared with Driver
authorization and copy writers. It binds complete cohort content, current contributing authority
and evidence, provenance, catalog revision, actual recipient and purpose. Changed dependencies
invalidate the whole unsent representation; removing a name or refreshing one row is insufficient.
The entire reviewed variant manifest and review fingerprint are dependencies too. Removing another
owner's reviewed variant invalidates prepared output even if the requesting owner's projection
remains identical; a fresh request requires the exact declared reviewed manifest.

The protected source fingerprint includes the exact mapped copy and purpose identities, purpose
generation and original clocks. A new purpose can restart its version counters; equal values and
equal numeric revisions therefore do not establish continuity of the reviewed evidence context.
The controlled catalog binds the explicitly reviewed original source, rather than approving the
currently observed copy afresh during each request.

Hidden contributors require current membership and sharing-state dependencies too. Absence of a
sharing grant is a dependency, not affirmative Sharing consent. A new grant can remove a Driver
from a supported hidden group. Protected writer accounting must participate in lifecycle drain,
authorization-generation changes and User erasure, as well as visible owner/name authorization.

## Composition and conservative history

Review sees completed, pending and possibly dispatched releases together across overlapping scopes,
pages, recipients and owner variants. Catalog or namespace changes cannot establish a new empty
history. The durable reservation is recorded before protected output; lease expiry, request
cancellation or an unknown writer outcome never erases its disclosure contribution.

Only an owned executor that is terminal and provably did not begin dispatch can release its
reservation from possible-output accounting. An uncertain first write remains counted. Executor
terminality and absence of a disclosure are distinct facts.

The controlled review fixtures define finite, explicit reviewed combinations. They are not an
automatic anonymity classifier or actual catalog approval. A five-hidden-Driver support floor,
unchanged bands or an isolated safe candidate do not establish that their combination is safe.
Unknown combinations remain unavailable; positive tests must still exercise useful supported output.

The fixture `controlled-composition-v1` explicitly declares the following variant combinations for
the fixed seven-Driver Demo cohort. These declarations are controlled test inputs. The fixture
retains the permitted representation, purpose and recipient signatures for its bounded pages;
changing a source or context does not earn review merely because public ranges remain equal.

| Declared combination | Controlled fixture decision |
| --- | --- |
| Visitor aggregate | Permit |
| SignedIn preview | Permit |
| First owner's exact variant | Permit alone |
| Second owner's exact variant | Permit alone |
| Visitor aggregate with SignedIn preview | Permit |
| Multiple owners or an owner combined with the other variants | Unavailable |
| Any undeclared source, representation, catalog or context | Unavailable |

## Restoration boundary

Primary application rows alone cannot prove that later output did not occur. Independently current
protected history must witness prior reservations and uncertain dispatch before affected output
can reopen. Independent reservation intent precedes the primary commit without claiming a distributed
atomic transaction. A failed or unknown primary commit retains conservative possible accounting.

Restored primary state remains closed while independent history is missing, uncertain or unreconciled.
Reconciliation restores required conservative accounting; it does not invent terminal-writer proof
or mark a release unsent because an application row is absent. A new catalog, namespace or history
identifier cannot bypass this boundary. Controlled initial empty-history setup is explicit fixture
authority, not an ordinary startup fallback.

The controlled independent PostgreSQL fixture keeps a trusted epoch and a separate committed
count/hash alongside its entries. Losing trailing entries, all entries or the metadata closes
history availability. Reconciliation preserves the independent reservation, dispatch and terminal
timestamps and the immutable possible/proven-unsent classification. These are fixture obligations
for a later production adapter, not evidence that a deployed journal resource has been selected.

## Restricted metadata

Release accounting retains only the internal scope, recipient/purpose, dependency and assessed
representation bindings needed for continuing enforcement, together with incarnation and state.
It is not a second Driver profile archive. Names, raw lap/rating measurements, upload contents and
telemetry do not belong in the release ledger or operational logs. Internal dependency identifiers
never become public hidden-Driver keys. Account erasure cannot delete required disclosure history.

## Evidence

The implementation seams are `ControlledCohortPublication` and its owned result executor in Api,
`PublicationReleaseStore` in Data, and `IPublicationHistoryAuthority` /
`IPublicationCompositionReview` in Core. The additive migration
`20261006110517_AtomicPublicationReleaseAccounting` retains history through restricted foreign
keys and SQL identity/checkpoint/dependency fences; Down refuses to discard it. Ordinary startup
does not register the controlled catalog, source or independent history fixture.

The implementation must exercise the real PostgreSQL migration chain and actual protected writers
on two Kestrel hosts, with deterministic barriers rather than timing sleeps.

| Obligation | Required observable evidence | Outcome |
| --- | --- | --- |
| PUB-07 atomic composition | A host held inside review blocks its competitor on PostgreSQL lock 370; a pending reservation is visible and an unsafe pair cannot separately pass | Passed controlled cases |
| Useful composition positive | An explicitly reviewed supported combination emits useful synthetic ranges/variants | Passed Visitor/SignedIn composition and both owners individually |
| PUB-08 complete dependencies | Changes before admission or first write invalidate all output, including hidden grant absence, same-band changes, same-value fresh copy identities and other owners' review context | Passed controlled cases |
| HTTP-01 drain | Withdrawal stays pending while any affected cohort executor can still write, including one whose primary row was restored away | Passed controlled cases |
| HTTP-02 uncertain dispatch | Uncertain append/dispatch/checkpoint, actual session loss and restart retain possible accounting; clocks cannot establish terminality | Passed controlled cases; lease behavior also covered by the existing drain rehearsal |
| HTTP-03 protected result | Actual module-owned serialization and transport remain inside admitted execution | Passed actual two-process Kestrel cases |
| Proven non-dispatch | A terminal, never-started owner reservation remains recorded but permits a supported Visitor release | Passed controlled case |
| RESTORE-02 continuity | An actual primary snapshot restore closes output until independent history is reconciled; live executors remain pending | Passed controlled cases |
| Cross-catalog/namespace history | An unknown catalog and a fresh independent epoch cannot turn missing history into valid empty history | Passed controlled cases |
| Relational fences | First hidden grant/opening and raw revision/User-erasure writes cannot bypass known cohort writer drain | Passed controlled cases |

Execution context must identify the actual source checkout, synthetic dataset/catalog versions,
date, test reports and coverage. Controlled fixture results do not establish deployed durability,
provider permissions, anonymity or the complete Driver Privacy Contract.

### Execution context (2026-10-06)

- Unmerged branch: `feat/atomic-publication-reservations`, based on
  `025f0835fc16ef54f7073f09871ff36230489400` (v13.0.2).
- Final local C# source fingerprint: `8316503713C96377EA726A79915CFFE01CA88FC091D74BD8DD17BD619F949FE0`.
  This hashes the sorted `path SHA256(file)` manifest for all 22 changed/untracked C# files,
  joined with LF using UTF-8; it is not a Git commit or a published artifact.
- Windows, .NET SDK 10.0.401, EF Core/tool 10.0.12, Docker 29.8.2; real PostgreSQL migrations,
  isolated loopback primary/history databases and distinct actual Kestrel child processes.
- Fixed catalog `synthetic-lap-rating-v1`, seven synthetic Drivers, `controlled-composition-v1`,
  current versioned consent and controlled proof fixtures, and an explicitly seeded original copy
  receipt. The only reviewed page offsets are 0/1 with page size 20.
- Focused verification: 51 distinct Publication cases passed (38 release HTTP cases, one atomic
  review test and 12 existing drain-rehearsal cases). The final strengthened cases were verified
  after the initial namespace run; full Release verification passed all 51 on their common final source.
- Scenario records are under `src/ApexRacers.Tests/bin/Debug/net10.0/TestResults/publication-ledger/`.
  Each records its scenario, result, fixture version and actual process/incarnation identifiers.
  Its `SuiteObligations` list names the suite's scope, not obligations proven by every individual case.
- Final full Release run: **1,228 passed, zero failed/skipped**, 14m34s. TRX execution interval:
  `2026-10-06T11:58:22Z`–`2026-10-06T12:12:54Z`. Coverage: **95.66% lines / 87.26% branches**.
  Results are `TestResults/cohort374/backend-tests.trx` and `coverage.cobertura.xml`;
  `context.json` and `code-sources.sha256` retain the local source context and 22-file manifest.
  Release scenario records are under
  `src/ApexRacers.Tests/bin/Release/net10.0/TestResults/publication-ledger/`.
  TRX SHA-256: `DD37D89890621DE4291F9224935851FB92A95A934C1C08E5E078514E58BE72B6`;
  coverage SHA-256: `773BE5DDACAC99A1DCDDD189151AD080E2B5EA18037C788B2B6403BBD5BAF303`.
- EF reports no pending model changes. Whitespace, source neutrality, generated-agent sync and
  diff checks passed; all 14 generator tests passed. Code review resolved copy identity, complete source membership, current
  names/recipient/catalog, independently missing writers, raw grant coordination and clock drift.
  Final review also bound the complete catalog-review fingerprint, with unchanged-projection and
  fresh-subset-context regression checks.

Reproduce the focused scope with
`dotnet test --configuration Release --filter-namespace ApexRacers.Tests.Publication`.
The full verification uses the repository's documented coverage command with
`--results-directory ./TestResults/cohort374`.

Initial fixture runs exposed incorrect expected copy version, an empty minimal-API reconciliation
response, a consumed response-stream reread and fresh-context dependency loading. Those failures
were corrected and rechecked; they are not evidence about production journal durability.
An earlier full Release run passed 1,226 cases before the final whole-review fingerprint correction;
its reports remain at `TestResults/cohort374-before-review-binding/`. The final 1,228-case run above
verifies the corrected source and two added regression cases. No Windows connection-churn failure
occurred in either full run; these passes do not resolve the separately tracked #391 fixture issue.

Raw SQL fences protect known primary rows. Reconciliation and independently current writer checks
cover the controlled paired-host restore scenario. Complete restored activation, production resource
durability and integrated acceptance remain the separate #377/#378/#380 boundaries.

## Independent follow-ups

Actual maintainer catalog review remains a separate conditional human action requiring both #373
candidate artifacts and this slice's composition evidence, before #380 integrated acceptance.
The actual independently protected runtime resource and restore contract belong to the separate
journal-adapter selection and deployment work. Existing production Driver/Telemetry denials and
unavailable ordinary proof/review/history adapters must remain in place.
