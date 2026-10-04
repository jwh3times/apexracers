# Driver lifecycle and protected admission spine

Implementation for [#370](https://github.com/jwh3times/apexracers/issues/370), following the
[accepted ownership architecture](../design/driver-disclosure-ownership.md),
[acceptance matrix](../design/driver-acceptance-and-rollout.md), and
[writer terminality rehearsal](driver-publication-drain-rehearsal.md).

This slice implements the coupled module boundary with controlled synthetic proof and journal
adapters, real PostgreSQL and actual response writers. It does not select a production journal,
invent registered-client ownership proof, admit a publication catalog, enable Live, or certify the
complete matrix. Production proof and journal adapters are unavailable; caller headers and
configuration cannot substitute a test adapter or make a claim into ownership.

## Authority and transition ordering

Driver Authorization accepts a proof receipt bound to its original User, Customer ID and explicit
provenance, and separately versioned affirmative Personal Analytics and Identity Sharing Consent.
The controlled grants accept only synthetic provenance. Another User's receipt, an asserted claim,
password or recording cannot establish that binding. Verified association conflicts remain closed
until explicitly resolved; neither historic copies nor consent transfer between Users.

Driver Publication prepares a bounded synthetic artifact and returns a module-owned protected
result. Actual execution registers durable admission, checks current primary/journal authority at
the unsent boundary, owns all serialization/writes, and records terminality only after its executor
cannot write again. No feature caller gets a publishable DTO and releases protection before MVC
executes it. The initial controlled artifact contains an authorized synthetic name and audience;
it is not a reviewed real catalog or a full personal analytics integration.

An idempotent lifecycle operation follows this sequence:

1. Persist the canonical intent and original loss time in the independent veto journal.
2. In one primary transaction, close affected consent/proof/binding, advance the revision, record
   the operation, mark tracked copies unavailable and commit generation-scoped cleanup work.
3. Keep new affected admissions closed. Previously admitted writers may finish before successful
   acknowledgement, but unknown or nonterminal writers keep the operation pending.
4. Reconcile the journal and primary completion checkpoints before reporting completion.

Retries use the journal's immutable operation identity and original clock. The journal cannot grant
permission; pending, unavailable or unreconciled enforcement denies affected access. A primary
operation that is known to lag cannot be treated as completed. PostgreSQL serializes shared grant,
admission and copy-write decisions; its lock is never proof of HTTP terminality.

Sharing withdrawal leaves valid personal access. Personal withdrawal, unlink, detected invalid
proof/revocation and explicit deletion close both scopes. Routine provider expiry/outage does not
manufacture a withdrawal. Regrant requires current proof/consent, cannot contract scope outside
the journal-first lifecycle, cannot restore explicit deletion, and cannot restore dormant personal
data at or after day 90. A deletion tombstone prevents grants for the same User across Customer IDs.
The controlled deletion transition is unavailable when the User has multiple historical associations;
it cannot acknowledge only one association as a completed User deletion. User-wide journal orchestration
remains required before that account workflow becomes available. New generations do not reset old cleanup clocks. Old cleanup is bounded
to its affected copy revisions and cannot erase later authorized generations.

## Legacy paths and migration

Legacy Driver routes that have not joined protected dispatch are unavailable for Real or unknown
scope, including warm-cache and arbitrary-ID calls. A global MVC resource filter applies before
binding. Demo Driver routes retain their synthetic behavior. The legacy telemetry upload/lap
workflow remains unavailable because recorder IDs and claims cannot establish attributed
persistence or personal use; its full integration belongs to #372. Independent catalog/account
routes remain available. Car/Track metadata and Schedule exclude private upload overlays pending integration.
Schedule's retained `HasUploadedLapAtTrack` field is always false and does not query private laps.
The old Subject Driver resolver no longer derives Real permission from a stored claim. Changing a
claim while an active verified association exists requires the lifecycle rather than a profile edit.
The profile check and persisted mutation share the primary store's grant coordination transaction.

Migration `20261004012741_DriverLifecycleAdmissionSpine` starts authorization stores empty and
preserves existing claims as unverified. Their physical column is renamed
`ClaimedIRacingCustomerId`, fencing old Identity writers while retaining the existing unique-index
name and conflict contract. Proof binding, active-association uniqueness, revision concurrency,
restrictive enforcement relationships and original cleanup clocks have relational constraints.
Down refuses to remove these enforcement fences. #369 provenance/quarantine and original clocks
remain unchanged. This is migration evidence, not a complete mixed-version/restore rehearsal.

## Execute synthetic evidence

With a running Docker engine, from the repository root:

```bash
dotnet test --filter-class ApexRacers.Tests.Services.DriverLifecycleSpineTests
dotnet test --filter-class ApexRacers.Tests.Models.DriverAuthorizationPolicyTests
dotnet test --filter-class ApexRacers.Tests.Data.DriverLifecycleMigrationTests
dotnet test --filter-class ApexRacers.Tests.Lifecycle.DriverLifecycleHttpTests \
  --report-xunit-trx --report-xunit-trx-filename driver-lifecycle.trx \
  --results-directory ./TestResults
```

The HTTP suite starts actual loopback Kestrel processes executing the application modules. Primary
and controlled journal data use separate isolated PostgreSQL databases. A TCP relay can cut the
publisher's primary connection while the other host and journal remain reachable. Deterministic
gates hold lifecycle/dispatch/checkpoint boundaries; expiry, cancellation requests, session loss
and replacement processes never substitute for terminal evidence. Ended-executor recovery is
bound to its original incarnation. A stopped process stays pending without trusted reconciliation.

The committed scenarios cover synthetic positive/negative ownership and scope separation,
journal-first interruption and replay with both hosts restarted, original clocks, writer races on
two hosts, actual TCP reset, session loss, asymmetric coordination loss, stale copies and forbidden
Real/test-adapter use. Migration tests apply the chain and exercise actual constraints rather than
only EnsureCreated. Test artifacts retain scenario outcomes and execution context without resolved
credentials or real Driver data; the complete TRX determines whether the run passed.

The evidence contributes to AUTH-01–07, HTTP-01–03, COPY-01 and MIGRATE-01 only within the named
module/legacy-fence boundaries. Complete projection/copy integrations, references, browser
authorization invalidation, composition accounting, mixed-version restore, actual proof/journal
adapters, catalog review and deployed verification remain their dependent slices. The test topology
does not establish cloud stop/access, production durability or physical removal from every store.
