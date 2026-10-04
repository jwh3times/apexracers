# Driver implementation handoff

Status: accepted planning handoff from [#362](https://github.com/jwh3times/apexracers/issues/362),
2026-10-02. These issues describe future implementation. Their publication completes the planning
gate, not a synthetic validation run, deployed certification or live activation.

The [accepted ownership architecture](driver-disclosure-ownership.md) defines the coupled
Driver Authorization, Driver Publication and Copy Lifecycle contract. The
[acceptance and rollout matrix](driver-acceptance-and-rollout.md) defines observable evidence,
migration safeguards and explicit stage decisions. Read both before implementing any slice;
splitting work does not authorize an independently permissive service or legacy fallback.

Each issue has an authoritative **Agent Brief comment** with semantic interfaces, current/desired
behavior, observable criteria, exclusions and evidence mapping. Native issue dependencies are the
source of truth for blocking relationships; the project board owns current status. The table records
readiness at this handoff, which may change when evidence or code lands.

## Slices and genuine prerequisites

| Issue | Responsibility | Code prerequisites | Handoff readiness |
| --- | --- | --- | --- |
| [#368](https://github.com/jwh3times/apexracers/issues/368) Real HTTP writer/drain rehearsal | `dotnet-api`: two actual API hosts, real PostgreSQL, deterministic faults and terminality evidence | None | `ready-for-agent`; first priority |
| [#369](https://github.com/jwh3times/apexracers/issues/369) Demo acquisition/cache provenance | `dotnet-api`: explicit namespaces, no real fetch on Demo miss, useful seeded Demo and teardown | None | `ready-for-agent`; independent of writer rehearsal |
| [#370](https://github.com/jwh3times/apexracers/issues/370) Coupled lifecycle/admission spine | `dotnet-api`: journal-first intent, primary closure/revision/copy work, actual writer drain and controlled proof/journal adapters | #368 | `ready-for-agent`, internally blocked |
| [#371](https://github.com/jwh3times/apexracers/issues/371) Evidence/name-copy fencing | `dotnet-api`: all participating writers, explicit purpose/provenance, original deadlines and physical cleanup | #369, #370 | `ready-for-agent`, internally blocked |
| [#372](https://github.com/jwh3times/apexracers/issues/372) Private upload/dormant lifecycle | `dotnet-api`: verified attribution, day-90 recovery cutoff/day-97 removal, explicit deletion and exact authorized analytics | #370, #371 | `ready-for-agent`, internally blocked |
| [#373](https://github.com/jwh3times/apexracers/issues/373) Whole-cohort band-group candidates | `dotnet-api`: fixed nested bands, distinct hidden support, suppression and versioned adversarial review artifacts | #370, #371 | `ready-for-agent`, internally blocked; actual catalog admission is separate |
| [#374](https://github.com/jwh3times/apexracers/issues/374) Atomic combined-release admission | `dotnet-api`: current dependencies, prior/pending/possibly dispatched accounting and protected dispatch | #368, #370, #373 | `ready-for-agent`, internally blocked; controlled review fixtures do not approve a catalog |
| [#375](https://github.com/jwh3times/apexracers/issues/375) Current scoped references/detail | `dotnet-api`: generic ineligible outcomes, opaque recipient/purpose/revision references and private Follow lifecycle | #370, #374 | `ready-for-agent`, internally blocked |
| [#376](https://github.com/jwh3times/apexracers/issues/376) Browser authorization invalidation | `react-frontend`: immediate owner clearing, 15-second checks/30-second validity from check start, multiple pages/offline/resume and no-store | #370, #375 | `ready-for-agent`, internally blocked |
| [#377](https://github.com/jwh3times/apexracers/issues/377) Migration/restore rehearsal | `dotnet-api`: actual migrations, old-writer fences, current independent prohibitions and release history, original clocks and useful safe recovery | #369–#372, #374–#375 | `ready-for-agent`, internally blocked |
| [#378](https://github.com/jwh3times/apexracers/issues/378) Independent journal runtime adapter | `azure-infrastructure` with `dotnet-api`: verified selected resource/access/durability/current-state/restore contract | #370 | `needs-info`; actual runtime contract unselected/unverified |
| [#379](https://github.com/jwh3times/apexracers/issues/379) API pilot/operating-limit controls | `dotnet-api` with `react-frontend`: at-most-10 allowlist, scope/budget admission, explicit stages, safe stop/recovery and continuing cleanup | #370, #374, #375 | `ready-for-agent`, internally blocked; synthetic budget fixtures do not establish real provider limits |
| [#380](https://github.com/jwh3times/apexracers/issues/380) Integrated required evidence check | `dotnet-api` with `react-frontend`: all matrix rows, useful positives, actual HTTP/database/browser/restore evidence and an unfiltered required check | #368–#377, #379 | `needs-info`; actual initial catalog admission and dependent suites are outstanding |

`ready-for-agent` means the behavioral contract is specified. It does not mean prerequisites have
landed or authorize live work. `needs-info` records a missing contract or actual review/evidence;
internal code prerequisites are also represented with native dependencies. No upstream provider
field, historic loss time or deployed durability property may be inferred to make a ticket ready.

## Boundaries that make this sequence viable

The initial writer rehearsal establishes actual dispatch/drain feasibility before the core spine
relies on it. Report unresolved terminality as a blocker, rather than acknowledging withdrawal on
lease expiry or an assumed canceled writer. Demo/provenance work can proceed alongside it.

The spine uses controlled synthetic proof and independent-journal adapters, so local engineering
does not require credentials or an unselected cloud target. Callers cannot select those adapters to
authorize real data. Missing real proof or journal authority keeps affected real scopes closed.
The production journal adapter requires a separately verified resource contract; it is not needed
to manufacture a green synthetic test topology.

Projection and ledger slices produce candidates and adversarial evidence. Test-only review fixtures
can exercise their contracts, but cannot substitute for recorded maintainer catalog admission. The
integrated gate requires at least one actually reviewed useful candidate and all other positive
cases; refusing everything is not validation. An unsafe or unknown context remains unavailable.

All integrations retain original cleanup clocks and independently justified official purpose.
Migration/restore must reconcile current independent prohibitions and prior/pending/possibly
dispatched release accounting before serving affected output. Neither a fresh namespace nor an
insecure rollback can erase that history or revive permission.

## Existing work retains its ownership

- [#270](https://github.com/jwh3times/apexracers/issues/270) retains registered-client OAuth entry
  and state/nonce behavior. [#271](https://github.com/jwh3times/apexracers/issues/271) retains the
  verified callback, now also integrating the lifecycle spine. [#272](https://github.com/jwh3times/apexracers/issues/272)
  retains OAuth UI integration, including current browser authorization behavior. Unavailable
  registered-client proof shapes remain evidence blockers; controlled proof fixtures are not live proof.
- [#268](https://github.com/jwh3times/apexracers/issues/268) retains actual live activation,
  blocked by integrated evidence, migration/restore, actual journal and pilot controls as well as
  applicable external/operator prerequisites. The four gates apply to each proposed live scope;
  proof-dependent scopes additionally require the existing OAuth/security chain. An approved
  anonymous scope does not silently enable named/private access or need unrelated proof contracts.
- [#269](https://github.com/jwh3times/apexracers/issues/269) retains measured ingestion tuning
  after activation. Verified initial quota/operating limits are required before the pilot and cannot
  be deferred to later tuning.

The accepted sequence is maintainer smoke, at most 10 explicitly allowlisted Users for at least
14 days, open Alpha for at least 7 days, Beta for at least 7 days, then Standard with visitor-output
evidence. Every promotion requires current evidence and explicit maintainer approval. Alpha/Beta
are self-selected audiences. Material safety repair returns through allowlist and restarts the
affected pilot observation, not cleanup clocks.

## Conditional operator handoff

The private tracker/wiki retains per-use processing review, credential/client registration and
OAuth-security verification. Deployed retention/backup/log/restore inspection remains Parked until
implementing evidence and a proposed rollout/publication exist. It requires configuration inspection
and observed physical lifecycle behavior; synthetic time advancement cannot certify production.

Three independently completable follow-ups supplement those existing actions: select/verify the
independent journal contract before its production adapter; review the initial useful publication
catalog after candidate/adversarial artifacts exist; and execute evidenced pilot/promotion/recovery
only after implemented gates and applicable live prerequisites are verified. Each has its own exact
conditional trigger, private issue, board status and numbered wiki procedure. Selection does not wait
for the later full deployed restore rehearsal, avoiding a circular prerequisite.

This handoff has not selected infrastructure, admitted a catalog, performed an acceptance run,
obtained provider permission/credentials, inspected or changed production, enabled flags or executed
a rollout. Public claims must continue to distinguish accepted targets from verified behavior.
