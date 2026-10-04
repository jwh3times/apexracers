# Driver acceptance evidence and staged rollout

Status: accepted plan. The maintainer accepted Q1–Q17 in three decision rounds on
2026-10-02 in [#362](https://github.com/jwh3times/apexracers/issues/362), within
[planning map #354](https://github.com/jwh3times/apexracers/issues/354).
No acceptance runs, new safeguards, migrations, production inspections or live activation are
completed by this document. Every new contract below is **not exercised** until implementation
produces the specified evidence. Existing green CI is not evidence for unimplemented contracts.

The [ownership architecture](driver-disclosure-ownership.md) owns the mechanisms and semantic
interfaces. Policy sources are [ownership/consent #358](https://github.com/jwh3times/apexracers/issues/358#issuecomment-5936739052),
[presentation #359](https://github.com/jwh3times/apexracers/issues/359#issuecomment-5938675485),
[retention #360](https://github.com/jwh3times/apexracers/issues/360#issuecomment-5944439567), and
[architecture #361](https://github.com/jwh3times/apexracers/issues/361#issuecomment-5954711734).
#361 refines hidden output into group summaries and ends dormant reactivation at day 90.
This plan neither grants processing permission nor establishes anonymity.

## Four gates: Q1

| Gate | Required deliverable | What passing establishes |
| --- | --- | --- |
| Accepted implementation plan | Accepted policies, ownership, this matrix, bounded work briefs, dependencies, unknowns and rollout criteria | Work can graduate into its specified implementation scopes. Closing #362 completes this gate only. |
| Integrated synthetic validation | Implemented contracts; required privacy checks; positive and negative cases; actual multi-instance HTTP/database faults; migration/restore rehearsal | The specified implementation behaves correctly in the recorded synthetic environment. No real identity proof, provider permission or deployed retention is established. |
| Verified live prerequisites | Applicable per-use processing review, confirmed client/provider contracts, implemented proof safeguards where needed, credentials, provenance/migration evidence, independently protected enforcement, observed copy/backup/log/restore settings and declared operating budgets | The recorded scope and environment are eligible for an explicitly approved pilot. Unresolved prerequisites keep their affected live scope closed. |
| Explicit staged activation | Current evidence, API-enforced audience control, scope/budget manifest, maintainer decision, monitored pilot and later promotion records | Only the approved stage/scope is enabled. Time elapsed, Alpha membership and credential arrival do not substitute for another gate. |

Synthetic work proceeds independently of unavailable credentials. Proof-dependent features remain
closed until their applicable contracts and safeguards are verified. A prerequisite that applies
to one scope does not silently approve another or impose a blanket synthetic-engineering stop.

## Evidence contract: Q2–Q5, Q7, Q10, Q13, Q17

The [#368 transport rehearsal](../research/driver-publication-drain-rehearsal.md) supplies candidate
two-process HTTP/PostgreSQL evidence. Its test-only protocol does not pass the full matrix or
establish implemented application authorization, journal/restore behavior or deployed settings.

Each matrix entry has a durable identifier and records its policy source, affected entry points and
copies, fixture/provenance, test layer, observable assertions, execution result and artifact.
Use **passed**, **failed**, and **not exercised**; an omitted, skipped or inconclusive case is not
passed. A known safe pending/closed outcome can pass its failure scenario, but cannot pass a
separate successful-completion scenario. Mere refusal of every request is insufficient.

Record the commit, catalog/consent versions, environment, relevant configuration, dataset provenance
and execution date. Material changes invalidate affected evidence; rerun dependent cases before
using it for a gate or claim. Reproducible artifacts use explicitly synthetic identities and no real
credentials. Operational evidence stays in private destinations. Operational logs must still exclude
identity/telemetry even when detailed synthetic test artifacts are available separately.

Unauthorized disclosure/use, false withdrawal acknowledgment, revived deletion, extended cleanup
clocks and publication bypass are blocking failures. Deterministic barriers must establish the
intended interleaving; sleeps and a passing retry are insufficient. Investigate unexplained critical
failures even if general E2E retry machinery subsequently passes. Line/branch coverage supplements
the matrix and cannot replace it.

### Test layers and existing limits

| Layer | Required use and boundary |
| --- | --- |
| Pure policy tests | Explicit time and independent expected outcomes for scopes, transition effects, bands, deadlines and eligibility. Do not guess provider sentinel/measurement semantics. |
| Real PostgreSQL integration | Independent connections and deterministic races for authorization/cleanup transactions, admission/reservation and stale writers. Exercise the migration chain as well as final models. |
| Actual HTTP/process tests | Two API instances, real response writers and observed bytes, controlled dispatch/failures, connection/session loss and restart. A returned DTO, MemoryStream or fake HasStarted value is not terminal-writer proof. |
| Built-SPA browser tests | Authenticated direct API probes plus rendered state, independent pages, delayed responses, expiry, offline/focus/reconnect and sensitive response headers. jsdom state tests supplement actual browser evidence. |
| Synthetic copy/restore rehearsal | Seed marked records, apply migrations/cleanup, restore an earlier snapshot, reconcile current independent enforcement and prior possible releases, then probe actual copies and blocked/allowed requests. |
| Deployed operator evidence | Observe actual topology, durability, copies, removal, backup lifecycle, logs and restore controls. Synthetic clock advancement does not prove deployed expiry. |

Existing PostgreSQL fixtures use real databases but normally `EnsureCreated`, not the full migration
chain. Existing middleware tests do not use live writers. Built-SPA E2E runs one API process, and
general Playwright E2E is currently non-required. Its demo seed does not cover diverse authorization
states, rating boundaries or explicit cross-namespace isolation. Those gaps are work to perform.

The dedicated **Driver Privacy Contract** check is an accepted target, not a configured check today.
Once implemented, it covers critical database, HTTP and browser scenarios, reports on every PR
without path filtering, and becomes required before live eligibility. General E2E remains
supplementary. A green check must not label missing acceptance scenarios as passed.

### Fixtures and coverage dimensions

Use controlled fixtures for an owner with personal consent; an owner with both scopes; sharing-only
withdrawal; personal withdrawal; asserted/unverified identity; detected invalid proof; non-User
Drivers; dormant and deletion-due original-Driver evidence; another User proving that Driver;
deleted Users; and Demo Driver. Simulated proof in a test never establishes real ownership, and
Demo data is not a source of real proof or consent.

Exercise visitor, signed-in and allowed pilot audiences, including self-selected preview tiers;
direct calls as well as pages; cold fetch, warm/expired caches and historical reads; saved follows;
in-flight fetch/write/dispatch; original/different User and Driver associations; explicit Demo/live
provenance and misses; and unavailable authority. Every audited API/copy category is represented.
Cover dangerous combinations explicitly rather than relying on pairwise coverage or constructing
an indiscriminate Cartesian product.

## Acceptance matrix

All rows are defined requirements, **not exercised** by this planning change. Evidence must name
the concrete endpoint/copy inventory it covers; one representative controller cannot establish
coverage of every audited surface. Check complete caller-visible JSON, URLs, keys and errors as
well as rendered text, within each audience's permitted fields.

| ID | Scenario and required observable outcome | Evidence |
| --- | --- | --- |
| AUTH-01 | An asserted claim, local password, recorder ID, OAuth login without affirmative consent, or feature flag grants no private Driver access or named sharing. Matching simulated proof plus personal consent permits the synthetic owner's authorized view. | Policy, database, HTTP and browser positive/negative probes. |
| AUTH-02 | Personal consent without sharing retains owner analytics; permitted sharing exposes only the consented name/racing fields to signed-in recipients, never another Driver's Customer ID, uploads or private history. Visitors get no names/references. | Audience-specific response and request assertions. |
| AUTH-03 | Sharing withdrawal preserves valid personal access; personal withdrawal/unlink/Driver change/User deletion closes both scopes. Every follower becomes inactive without granting access. | Transition effects and post-transition cold/warm/historical/follow requests. |
| AUTH-04 | Routine expiry/transient provider outage preserves grants and original retention state; detected revocation/invalid proof suspends both. Relink and material scope expansion require the applicable fresh proof/opt-in. | Explicit event fixtures, current state and deadline assertions. |
| AUTH-05 | Confirmed verified ownership can supersede an unverified claim; verified conflicts require explicit resolution. No account merge, private disclosure, upload transfer or consent reuse occurs. | Conflict/race/recovery assertions with separate Users. |
| AUTH-06 | Journal intent is durable first; pending intent vetoes affected acquisition/publication. Primary closure/revisions and cleanup work commit together. Repeated operations recover from stable checkpoints with original clocks. | Fault cuts before/after each durable boundary; primary uncertainty and replay. |
| AUTH-07 | Journal unavailability, unestablished current enforcement or primary state known to lag cannot open access. The journal never grants consent. Required completion entries exist before acknowledgment. | Independent-journal loss/lag/recovery and grant-negative tests. |
| HTTP-01 | Withdrawal before admission blocks new output. Withdrawal after admission remains pending until the actual writer finishes or is verifiably stopped. No affected dispatch occurs after successful completion acknowledgment. | Two live API instances, controlled barriers, wire observations and transition checkpoints. |
| HTTP-02 | Cancellation requests, lease expiry, process-liveness claims or database session loss do not falsely prove terminality. A late writer that can still send keeps acknowledgment pending; unknown dispatch remains possible release history. | Delayed/partial writes, transport/session/process failures and observed resumed behavior. |
| HTTP-03 | Provider fetching/preparation stays outside admission; a protected result retains coordination through actual serialization/dispatch. Unsent work with changed dependencies is rebuilt or denied. | Writer instrumentation and whole-projection race assertions. |
| PUB-01 | The complete authorized filtered cohort is assessed before pagination. Every emitted joint group has at least five distinct hidden Drivers, excluding owner/disclosed contributions; duplicate laps do not increase support. | Cohorts with 4/5/6 contributors, duplicate entries, mixed consent and multiple pages. |
| PUB-02 | Exact lower-inclusive/upper-exclusive boundaries hold for 0.5-second lap bands, 250 rating bands inside [1000,2500), 500 outside, then 1/1000, 2/2000, 4/4000, 8/8000. One finest supported level applies to the whole cohort. | Independent boundary/coarsening expectations, nested partitions and page consistency. |
| PUB-03 | Unsupported measurements are omitted; visible missingness needs joint support. No unverified source sentinel rules or rating-delta bands are inferred. | Known-shape fixtures and unsupported-field negative tests. |
| PUB-04 | One summary per admitted band group contains no exact count or Driver-addressing key. Hidden ordering derives only from approved bands, with no exact source position/delta/incident/race-reference leakage. | Full field/key/ordering assertions and repeated-response linkage probes. |
| PUB-05 | Initial visitor/fallback output contains only approved band-group ranges from the same artifact. Failed support/composition is not rescued by a separate sparse count, extrema, median, position or custom predicate. | Sparse and failed-artifact cases across audiences. |
| PUB-06 | Versioned catalog review covers fields/scope/audience, prior/overlapping releases, totals, owner variants and external racing knowledge. Unknown/unsafe/unreviewed entries stay unavailable. | Recorded threat review and adversarial regression cases, with maintainer acceptance for admission. |
| PUB-07 | Review/reservation is atomic with admission. Concurrent publishers account for completed, pending and possibly dispatched releases across recipients; neither can ignore the other's reserved output. | Real PostgreSQL concurrent barriers and combined-release assertions. |
| PUB-08 | Sharing changes, hidden membership changes, late ingestion/corrections or catalog/enforcement changes invalidate the entire unsent projection. Removing a name alone cannot repair stale support. | Changes across prepare/admit/dispatch boundaries, including 5-to-4 support. |
| PUB-09 | Internal Field membership and authorized owner percentile arithmetic remain exact. Unsafe supplementary Field Size/Position/histograms/extrema are withheld without changing the owner's formula. | Independent numeric baselines plus projection/composition checks. |
| PUB-10 | At least one reviewed, supported catalog entry produces useful permitted synthetic output. Unknown/unsafe contexts still close; passing by denying every view is not integrated validation. | Positive supported-cohort run and recorded catalog admission. |
| REF-01 | Random references are recipient/purpose scoped, expire at 15 minutes and bind current authorization revision. Wrong/expired/revoked references give generic unavailability without identity/existence disclosure. | Before/at/after expiry, recipient/purpose substitution, replay and lifecycle cases. |
| REF-02 | Arbitrary raw Customer IDs and private follows do not authorize other-Driver lookup. Eligible views issue fresh references; withdrawn follows disappear, cannot reactivate at day 90 and are removed by day 97. | Discovery/comparison/lap-trace direct API and follower-copy cases. |
| COPY-01 | Shared mapped evidence is name-free; authorized names/private payloads are separately scoped. Fetch completion and every persistence writer recheck current generation/purpose/provenance. | Cold/warm/history and fetch/write races, including ingestion/backfill. |
| COPY-02 | Stale writes and retries cannot recreate deleted names/personal data, renew eligibility or extend original deadlines. Transition-triggered work is durable and idempotent. | Delayed writers, repeated operations, failure/restart and copy queries. |
| COPY-03 | Dormant personal data is immediately unavailable. Fresh proof/consent before day 90 restores only the same original User/Driver; at day 90 restoration ends, physical removal completes by day 97. | Original/new Driver and different User cases at temporal boundaries. |
| COPY-04 | Explicit User deletion stops access immediately and erases owned live data within 7 days and backup copies within 14 days. It never receives the dormant grace; earlier applicable deadlines win. | Copy/dependency inventory, separate live/backup observations and exception reporting. |
| COPY-05 | Deleted personal sources invalidate/delete derivatives or rebuild them only from independently authorized retained evidence. Minimal enforcement/audit exceptions do not preserve profiles, names, telemetry or usable credentials. | Source-to-derived markers, rebuild baselines and restricted-record inspection. |
| COPY-06 | Independent official evidence retains its continuing purpose and exact Field membership; collection is active Seasons or authorized historical feature requests. Purpose termination removes affected evidence within 7 days. | Purpose/collection fixtures and preservation/removal contrast cases. |
| COPY-07 | Unauthorized name copies are removed within 24 hours; their backups expire within 8 days. Expired mapped payloads are physically removed within 48 hours; temporary material is removed after processing or orphan cleanup within 24 hours. | All applicable copy queries at earliest-deadline boundaries, not TTL/job-run assertions. |
| COPY-08 | Backups have maximum 7-day lifetime; operational logs maximum 30 days; explanatory consent audit maximum 12 months. Minimal enforcement persists while stale copies can return, with only permitted fields. | Synthetic boundary tests plus actual topology/configuration/lifecycle evidence before claims. |
| COPY-09 | Cleanup failure reports overdue work, retries and withholds affected use. Durable withdrawal, verified live erasure and backup expiry are separate outcomes; no false completion or clock reset occurs. | Failed cleanup, recovery and user/operator completion-report assertions. |
| UPLOAD-01 | Matching verified personal ownership permits attributed private persistence; unverified previews are transient/non-identifying and mismatches generic. Recorder identity alone never proves ownership; no raw-file archive remains. | Matching/unverified/mismatched receipts, persistence and temp-file queries. |
| BROWSER-01 | Owner withdrawal clears immediately. Connected checks occur at least every 15 seconds and display validity ends no later than 30 seconds from check start. Delayed receipt cannot restart validity. | Controlled clock/response unit tests and real built-SPA observations. |
| BROWSER-02 | Denial/uncertainty/expiry clears state; obsolete generations cannot repopulate it. Navigation/focus/reconnect and resumed offline/suspended sessions revalidate before display. Independent pages observe withdrawal. | Delayed success/error, multiple pages, offline/focus/resume cases. |
| BROWSER-03 | Sensitive responses use no-store. App-managed caches/reloads do not resurrect affected display. Already-delivered/exported bytes are outside recall guarantees and are not claimed erased. | Real headers, reload/network observations and accurate completion wording. |
| DEMO-01 | Explicit provenance separates request/store/cache/publication namespaces. Demo misses remain synthetic and never invoke real acquisition; numeric IDs/expiry sentinels alone do not decide provenance. | Namespace collision, warm/miss/stale-write and unknown-provenance cases. |
| DEMO-02 | Demo remains useful without provider credentials, real verification or real consent. Legacy ambiguous provenance stays unavailable; successful teardown precedes real ingestion if Demo was enabled. | Synthetic built-SPA/seed checks and migration/teardown rehearsal. |
| RESTORE-01 | A pre-withdrawal/deletion snapshot serves no affected data until current independent enforcement and overdue cleanup reconcile. Restored revisions cannot revive grants; unavailable authority stays closed. | Real synthetic snapshot restore, current journal reconciliation and copy/HTTP probes. |
| RESTORE-02 | Restored state also reconciles prior/pending/possibly dispatched release accounting. Missing accounting cannot be treated as no prior output or be bypassed with a new catalog/namespace. | Restore after admission/dispatch uncertainty and composition-negative probes. |
| MIGRATE-01 | Mixed-version cutover inventories/fences old writers and permissive routes before new publication opens. No fabricated proof/provenance/loss time, silent upload transfer or grant revival occurs. | Migration-chain and old-writer/cached-route rehearsal. |
| MIGRATE-02 | Rollback retains independent enforcement, possible-release accounting and deadlines. An insecure binary or stale data snapshot cannot reopen access; unsafe recovery stays closed. | Rollback/restore rehearsal with actual requests and copy verification. |
| PILOT-01 | Every affected API acquisition/publication path enforces allowlisted User, catalog/scope and operating limits, including direct calls and background collection. Flags/preview choice/Admin status cannot bypass the declared pilot boundary. | Allowed/disallowed requests and configured/missing budget/scope tests. |
| PILOT-02 | A safety failure pauses affected acquisition/publication while required withdrawal/cleanup continues. Resumption requires cause-specific evidence and returns through the allowlisted stage. | Synthetic stop/recovery run plus later private operational records. |

### Deadline proof and completion

For each applicable clock, test immediately before, at and after the boundary, plus earliest-deadline
precedence, retries, delayed acknowledgment, stale writers and restart. Original loss/request time
remains authoritative. Unknown historical clocks are not invented or reset at migration; they are
reported as unresolved, with affected use unavailable and no false erasure claim.

Verify physical copies and derivatives, not merely permission denial, expiry metadata or job
execution. Independent official evidence and restricted enforcement/audit exceptions retain their
separate policies. Configuration inspection plus observed copy-removal, backup-lifecycle and restore
evidence is required for deployed claims; accelerated clocks alone are not that evidence.

Positive validation must include authorized personal analytics, unchanged owner percentile
arithmetic, permitted sharing, eligible references, private upload attribution, lifecycle recovery,
functional Demo behavior and at least one admitted useful catalog entry. If safeguards prevent all
useful output, report the limitation and revisit the plan rather than declaring validation complete.

## Implementation and migration sequence: Q8–Q9, Q14

1. Establish synthetic fixtures, explicit provenance and executable transport/database evidence.
   Resolve actual writer terminality/failure feasibility before relying on it for the live protocol.
2. Build the shared Driver Authorization/Driver Publication/Copy Lifecycle enforcement spine
   together using controlled adapters. Record durable lifecycle intent, admission and copy work;
   unknown authority closes affected paths. This is not three independently permissive services.
3. Integrate reviewed projections, current references and browser lifecycle behavior. Named/private
   access requires current grants; all audited surfaces and persistence writers join the protocol.
4. Rehearse migration, mixed versions and restore. Inventory copies and old writers; classify
   provenance/ownership from obtainable evidence; preserve justified official evidence; quarantine
   unverifiable attribution/provenance. Remove unauthorized snapshots under original applicable
   deadlines. Fence old paths before opening new publication. No insecure fallback is retained.
5. Complete integrated required-check evidence and the applicable production adapters/contracts.
   Provider-dependent work uses existing OAuth tasks; actual journal topology/resources must be
   selected and verified rather than assumed to match test storage.
6. Obtain applicable live prerequisite evidence and execute only the explicitly authorized stage.

Each graduated issue has an authoritative Agent Brief with current/desired behavior, semantic
interfaces, independently testable criteria, exclusions, evidence and genuine dependencies.
`ready-for-agent` means fully specified, not necessarily unblocked. Missing provider or runtime
contracts are explicit evidence blockers. Do not duplicate OAuth #270–#272 or live activation #268.
The separately tracked [implementation handoff](driver-implementation-handoff.md) owns slice IDs,
readiness and links; the project board owns current status.

## Staged activation and recovery: Q6, Q11–Q12, Q15–Q16

| Stage | Minimum observation and approval |
| --- | --- |
| Maintainer smoke | All applicable gates pass; actual permitted acquisition/persistence/cache and authorization behavior are observed before inviting pilot Users. |
| Allowlisted pilot | At most 10 explicitly allowlisted Users; minimum 14-day observation period; current required evidence and explicit maintainer promotion approval. |
| Open Alpha | Minimum 7 days of observation and explicit approval before further expansion. Alpha is self-selected and is not an approved tester boundary. |
| Beta | Minimum 7 days of observation and explicit approval before Standard. Beta is also self-selected; assess the enlarged audience and workload. |
| Standard | Current gates and explicit approval, including visitor-output evidence and applicable consent/public-copy changes. No automatic promotion. |

Before starting, declare permitted catalog entries, collection scope and an operating/quota budget
grounded in verified provider limits. Enforce the pilot boundary at the API for acquisition and
publication, not only navigation/flags, and account for background collection as well as recipients.
The User cap is not the five-hidden-Driver support floor and grants no additional Driver permission.
Minimum periods are observation floors, not substitutes for evidence or promises that all retention
cycles are demonstrated merely by waiting.

At each expansion, reassess audience-wide composition, capacity/quota controls and relevant
prerequisites. Material consent audience/purpose/scope changes require fresh opt-in. A change in
preview tier does not itself supply consent, proof, provider permission or catalog approval.

Stop affected acquisition/publication on any safety-contract failure, missed cleanup deadline,
lost enforcement/release accounting, provenance uncertainty or invalidated prerequisite. Continue
required withdrawal and cleanup, preserving current enforcement, possible-release history and
original clocks. Routine token expiry/transient outages preserve grants and use accepted unavailable
behavior; detected invalid proof/revocation retains its different policy.

To resume after a safety stop: diagnose/fix the cause, reproduce the original failure, verify its
recovery, physical copy cleanup and usable authorized behavior, and obtain current approval/evidence.
Resume through the allowlisted pilot. Material safety-control changes restart that scope's 14-day
pilot observation; they do not restart retention clocks or revive a grant. Unknown authority or
writer status remains closed/pending, never falsely completed. General CI success cannot override
missing cause-specific evidence.

## Existing dependencies and human handoff

- [#270](https://github.com/jwh3times/apexracers/issues/270),
  [#271](https://github.com/jwh3times/apexracers/issues/271) and
  [#272](https://github.com/jwh3times/apexracers/issues/272) retain ownership of the registered-client
  OAuth entry/callback/frontend chain. Local SDK identity models are obtainable, but they are not
  a complete verified registered-client proof contract. Missing external shapes are not guessed.
- [#268](https://github.com/jwh3times/apexracers/issues/268) retains actual live activation.
  Its older Alpha-first checklist must follow this allowlisted sequence and all implemented gates.
  Credentials or closure of #362 alone are insufficient. Demo disable/purge/teardown precedes
  real ingestion where applicable, with explicit provenance evidence.
- [#269](https://github.com/jwh3times/apexracers/issues/269) retains later measured ingestion tuning.
  It is not permission to omit verified initial operating limits or safeguards.
- Existing private processing review, separate credential/client registration and OAuth-security
  verification remain distinct. Conditional deployed retention/restore verification stays Parked
  until implementing evidence/rehearsal and proposed rollout/publication. A journal runtime contract
  may need its own earlier conditional selection step to avoid a circular dependency on deployment.

Human actions use the [private follow-up procedure](../agents/human-actions.md) with step-by-step
wiki instructions and reciprocal tracking. Operational identifiers/settings stay private. This plan
does not obtain credentials, inspect/change production, contact a provider, enable flags or execute
implementation. Public product/privacy claims must describe verified behavior and settings;
accepted targets remain clearly identified as targets until then.
