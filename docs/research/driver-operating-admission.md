# Driver operating admission and staged rollout controls

Implementation for [#379](https://github.com/jwh3times/apexracers/issues/379), following the
[accepted matrix](../design/driver-acceptance-and-rollout.md) and the existing
[lifecycle](driver-lifecycle-admission-spine.md), [release accounting](atomic-publication-reservations.md)
and [scoped reference](driver-scoped-references.md) contracts.

This slice implements controlled synthetic stage, scope, budget and stop/recovery behavior.
Ordinary API and ingestion startup use `UnavailableDriverOperatingControls`. Neither settings,
credentials, JWT roles, feature flags nor a request can select the synthetic adapter. It never
admits Real provenance. This is not provider quota verification, production activation, maintainer
catalog admission, the integrated #380 gate or deployed privacy acceptance.

## Admission and recorded decisions

`DriverOperatingPolicy` owns the explicit smoke → pilot → Alpha → Beta → Standard progression.
Every promotion records evidence and approval for the current scope version, composition, capacity
and applicable prerequisites. Pilot observation is at least fourteen days; Alpha and Beta each require
at least seven days. Standard additionally requires visitor-output evidence. Clock advancement alone
performs no promotion. Smoke and pilot use an explicit list of at most ten Users, plus their current
scope opt-in. Admin and preview tiers have no enrollment exemption and confer no Driver permission.
Open Alpha/Beta enforce the declared audience and opt-in; visitor publication opens only at Standard.

A manifest enumerates exact catalog ID/revision, collection scope and work kind. Unknown entries,
missing budgets, unverified provenance/evidence and malformed manifests remain closed. Changing scope
requires a new version and fresh recorded smoke approval, invalidates outstanding reservations and
requires fresh opt-in. Prior manifests and stage decisions remain recorded. The acknowledgment is
additional operating evidence, never a replacement for applicable proof or Personal/Sharing consent.

`SyntheticDriverOperatingStore` persists the controlled manifest, decisions, stop/recovery history,
original budget-window start and reservations in PostgreSQL. Advisory transaction lock 379 serializes
hosts and atomically charges shared acquisition/publication allowances. API and background collection
share the acquisition allowance. Failed, canceled, uncertain or withheld work is not refunded.
Restart, promotion, recovery and scope replacement do not reset spent units. The next declared window
can replenish them; backward clock movement and expired reservations do not admit work. Independent
release accounting remains under its existing lock/history and is never reset by an operating change.

## Safety stop and recovery

A recorded safety stop closes new acquisition/publication and invalidates prepared unsent work.
The protected executor checks operating eligibility again alongside existing current authorization,
source, catalog and journal checks. Already sending writers retain their original drain and release
accounting; an operating reservation expiry or stop does not prove transport terminality or erasure.
Withdrawal, cleanup, original-clock reconciliation and terminal checkpoints remain available because
those paths do not require operating admission.

Recovery binds the exact stop ID/cause to recorded reproduction, repair verification, physical cleanup,
useful authorized output and current approval. Every unresolved cause needs its own evidence; resolving
one cause cannot reopen work while another remains. It returns through the explicit pilot list. A declared
material repair restarts the affected fourteen-day observation, while original cleanup and possible
release clocks remain in their owning modules. Stops, approvals, original pilot time for nonmaterial
recovery, prior scope records and cause-specific recovery evidence are preserved.

## Participating entry points

| Entry point | Operating boundary |
| --- | --- |
| `ScopedDriverPublication` personal/discovery/follows/detail/comparison/Follow | Actual authenticated executing JWT User; exact reference catalog/revision; reservation and final unsent checks inside protected dispatch. |
| `DriverPublication` controlled owner/sharing/Uploaded Best | Separate declared synthetic catalog/purpose; actual executing recipient; existing proof/journal/snapshot and writer-drain checks remain required. |
| `ControlledCohortPublication` | Exact catalog/revision and whole-cohort scope; actual recipient/visitor; operating validity supplements atomic composition and release history. |
| `CachedIRacingClient` Real cache miss | Executing authenticated audience, exact cache key/purpose/Season scope, operating acquisition charge, Demo teardown, issued purpose and current copy receipt. |
| Ingestion seasons/catalog/assets/schedule/search/result callbacks | Each callback uses the shared background collection interface and an explicit catalog/active-Season scope; a stopped or unavailable run cannot start another callback. |
| Legacy Real/Unknown Driver routes and Telemetry | Existing resource fence remains closed, including warm cache/direct/alternate-ID paths. |
| Credential-free ordinary Demo preview | Existing synthetic preview behavior remains independent of controlled rollout fixtures. |
| Withdrawal, reconciliation, cleanup and independent account/catalog reads | Continue their existing authority; operating enrollment neither grants access nor blocks required cleanup. |

`DriverOperatingCollection` consumes one declared **application work unit** before invoking its
adapter and rechecks the reservation before returning acquired material. An SDK operation can issue
multiple transport requests, chunks, retries or token calls. A synthetic work unit is not a verified
provider request/quota unit. Actual provider accounting, registered-client behavior, service-account
quota/window/concurrency, processing permissions and durable independent operating authority must be
verified before a Real adapter can replace the unavailable one. No new live adapter is registered.

## Persistence and recovery limits

Migration `20261009162419_SyntheticDriverOperatingControls` creates an empty, singleton synthetic
store with nonnegative counters. It does not enroll accounts or promote stored claims. Down refuses
to discard safety/history/budget state. Existing Demo preview requires no purge/reseed for this
additive table. Its serialized owned Core contracts are persisted field shapes; changing their names
or meaning requires explicit compatibility handling.

This controlled store resides in the synthetic primary database. Its restart/concurrency evidence
is not independent production durability or restore certification. A primary snapshot cannot establish
that operating history or spent provider quota is current. Production remains closed until a verified
current operating authority and applicable journal/restore contract exist. Existing #378 and private
rollout procedures retain ownership of those external prerequisites; this slice introduces no immediate
required operator action or Live activation.

A writer whose operating reservation is absent after a synthetic primary restore stays closed even
when independent publication history is successfully reconciled. Its proven-unsent terminal checkpoint
still completes; a later explicitly admitted request can provide useful synthetic output. This does
not certify current operating counters after a production restore.

## Reproduction and evidence

With Docker running:

```bash
dotnet test --filter-namespace ApexRacers.Tests.Operating \
  --report-xunit-trx --report-xunit-trx-filename driver-operating.trx \
  --results-directory ./TestResults/operating379
```

Policy tests use explicit synthetic time. Store tests use independent PostgreSQL contexts and
exercise shared budgets/restart, stopped and delayed background collection, and the actual migration
chain. HTTP tests use two Kestrel processes, actual JWTs, real scoped Driver controllers and independent
existing release history. They cover useful output, excluded Admin/parameter attempts, fresh scope
opt-in, stop before new calls, stop before unsent dispatch and continuing withdrawal. Dedicated host
control endpoints and synthetic decisions are test-only, with no corresponding product route.

These cases contribute PILOT-01–02 and preserve AUTH-06–07 within the named participating paths.
They do not label the complete matrix, real quota/client facts, deployed erasure or backup expiry as
passed. General coverage and green CI remain supplementary to #380's integrated evidence.

Local validation on 2026-10-09 used Release builds on base `085b71523dac8e38776de92574b984990405d1be`
with the unmerged #379 changes. The full backend run exercised 1,313 cases: 1,312 passed, no skips,
and one restore assertion failed because it expected a reservation missing after restore to resume.
The corrected assertion requires closed output and a proven-unsent terminal checkpoint; both affected
restore cases subsequently passed. Product code was unchanged between these runs. The full run's
nonempty product report covers 7,266/7,581 lines (95.84%) and 3,591/4,127 branches (87.01%), above
both unchanged 85% gates. This records a full run plus a focused correction replay, rather than a
claim that the original full run was green.

Local artifacts are `TestResults/operating379-full/backend-tests.trx`,
`TestResults/operating379-full/coverage.cobertura.xml`,
`TestResults/operating379-full/context.json` and
`TestResults/operating379-replay/restore-operating.trx`. The context records the base, local-source
status, final Release assembly hashes and unexercised production facts. Dedicated operating cases
are recorded in `TestResults/operating379/driver-operating.trx`; these artifacts are regenerable
local outputs, not committed or published acceptance evidence. All 19 dedicated operating cases
passed with no skips in the final focused run.
