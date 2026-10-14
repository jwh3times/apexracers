# Percentile Rank history ownership — investigation #352

Status: **proposed; no history semantics approved or implemented**. Investigation completed
2026-10-14 against production baseline `74f2c4e8738752441c599d2812e9c7ca04a0dc08`
(v14.0.17). Source issue: [#352](https://github.com/jwh3times/apexracers/issues/352).

The recommended next step is a selection-scoped history: each canonical Lap Evidence selection
has one current, version-checked reading per User/Driver scope, Car, Series and Race Week.
This is a proposal for maintainer review, not an immutable event log or permission to implement it.
The present investigation adds characterization tests and this brief only.

## Executed consistency assessment

Five PostgreSQL characterization cases passed, with no failures or skips (15.836 seconds).
The tests call actual service interfaces successively; they never pre-seed percentile cache rows.
Each test uses a fresh PostgreSQL database through the existing fixture, with fabricated catalog,
Drivers and laps. No live iRacing calls, credentials or new wire-shape assumptions are involved.

| Scenario | Actual result | Interpretation |
| --- | --- | --- |
| Compute with Uploaded Laps, then analytics with Race Laps only | Real-scope compute returns rank 90 from a 90-second upload but persists **nothing**, because the User has no authorized Subject Driver. A subsequent recommendation call persists rank 90. Race-only analytics then returns rank 90 alongside a 110-second Race Lap Personal Best. | The originally suspected compute-to-cache path does not reproduce. The recommendation-to-analytics path demonstrates the mixed-selection reading at the direct legacy service seam. |
| Change Sim Session Type selection for one Week | Practice upload gives 90; Race-type upload gives 70; Race-only percentile lookup gives 50. Repeated recommendation has Expected Percentile 50 and one stored row. | Current one-slot replacement does not double-count this Week, but destroys the previous selection's reading. These synthetic enum cases do not settle upstream session taxonomy questions. |
| Refresh through both writers and cross the minimum Field Size | Demo compute writes Week ranks 50 and 30; recommendation Expected Percentile is 40. Shrinking the second Field from five to four makes it non-presentable, leaves Expected Percentile 50 and one eligible analytics history Week. Growing back to five restores 40, including on repetition; two stored Week rows remain. | Same-Week replacement and removal of the formerly eligible contribution work in this exercised sequence. Undersized arithmetic can still be stored without becoming a presentable history contribution. |
| Claimed Identity differs from Subject Driver; lookup another Driver | User claim 777 resolves to Demo Subject Driver 100001. Own compute stores rank 50; another Driver's compute returns 90 without changing the own row or its timestamp. Recommendation for the Demo Subject finds no User by claim and does not persist. Analytics retains rank 50 but has no Personal Best and Total Weeks 0. | The compute overwrite guard works. Writer/reader ownership resolution is inconsistent: claim lookup and resolved Subject Driver are different seams. Demo cannot see the legacy quarantined upload. |
| Exact Track and half-open Week | Same-venue Track 102's one-second upload is ignored for Track 101. An upload at the Week's exact end is excluded; the in-Week 90-second upload wins. The excluded two-second upload retains its original timestamp. | Existing exact Track ID and `[start, end)` boundaries are preserved by PostgreSQL timestamp translation. |

The Real cases directly exercise legacy services behind a fixture's retained-evidence adapter.
Ordinary Real legacy HTTP routes remain unavailable through `LegacyDriverAccessGuard`; a stored
claim is not Driver authority. These results do **not** establish a reachable Live disclosure,
acquisition permission, protected HTTP acceptance, migration/restore safety or a security exploit.
The Demo ownership result is service-level evidence; this investigation did not execute its HTTP route.
The fixture uses `EnsureCreated`, not the production migration fences. The threshold case changes
fabricated retained source rows directly; it is not a source-correction or acquisition protocol rehearsal.

### Reproduce

Docker must be running. The checked-in fixture uses `postgres:18.0-alpine`; the measured run used
.NET SDK 10.0.112 with the repository's Microsoft Testing Platform runner. From the public root:

```bash
dotnet test --project src/ApexRacers.Tests/ApexRacers.Tests.csproj \
  --filter-class ApexRacers.Tests.Services.PercentileHistoryCharacterizationTests \
  --report-xunit-trx --report-xunit-trx-filename characterization.trx \
  --results-directory "$PWD/TestResults/352-history"
```

The [test source](../../src/ApexRacers.Tests/Services/PercentileHistoryCharacterizationTests.cs)
is the durable executable evidence. Its measured SHA-256 is
`e5c87d27a40931c9bf2bd8687f1bb2f4a21a1b4ef8cae1213a8742517fbf9e47`.
A [compact observation record](../research/evidence/percentile-history-352.json) preserves the
five run outcomes. Local TRX, log and per-scenario JSON are retained in ignored
`TestResults/352-history/`; the test regenerates observations there. Week dates are relative to the
run date, so a fresh run should preserve the assertions rather than the historical calendar date.

## Current ownership and readers

The current unique identity is `(Provenance, UserId, CarId, SeriesId, WeekId)` in
`iracing.ScopedCarPercentileResults`. A row contains the rank, Top Share, Sample Size, computed
clock and evidence-copy identity, but no selection, Subject Driver or winning Personal Best.
Consequently the row cannot establish which current evidence request its rank describes.

| Participant | Present responsibility and constraint |
| --- | --- |
| `PercentileCalculationService` | Calculates a Field and Personal Best; persists only when the requested Driver equals the caller's resolved Subject Driver. Real claim-only compute cannot persist. |
| `CarRecommendationService` / `GetMyPercentilesAsync` | Resolves User through the requested claimed Customer ID, independently replaces the Week slot and adjusts eligible history sum/count. Week Detail’s `GetMyPercentilesAsync` reuses this writer. |
| `UserAnalyticsService` | Reads stored presentable ranks unchanged but separately computes Personal Best using the request's selection and the User's claim. History eligibility and available Personal Best Week counts can diverge. |
| `PrivateUploadStore.CalculatePercentileAsync` | Additional typed, authorized personal derivative producer. It captures purpose/preparation generations and exact private plus official source dependencies before committing a closed percentile batch. Its copy key is `private-percentile:{user}:{week}:{car}` and has no selection dimension. It must participate in any future ownership change. |
| Seeder | Produces Race-only synthetic readings for seeded Users. A new policy must explicitly classify fresh seed output, without pretending old rows carry inferred selection metadata. |

The recommendation implementation also has a history fallback when no eligible current Personal
Best exists. The exercised small-Field path does not resolve that separate missing-Personal-Best
policy. Existing aggregation includes cached Weeks regardless of their position relative to the
requested Week; changing the meaning of “past” requires an explicit decision.

## Three possible history meanings

| Meaning | Analytics and recommendations | Existing rows | Cost and tradeoff |
| --- | --- | --- | --- |
| Preserve the original reading | Show original selection, winning Personal Best and Field context together. A new selection must not relabel that reading. Expected Percentile reflects the chosen original-reading policy. Source/consent loss still invalidates inaccessible evidence. | Old rows lack the metadata to prove their original selection or winning evidence. Do not backfill those facts from current laps or `ComputedAt`. | Cheap history reads after capturing sufficient metadata; additional frozen context and a clear refresh policy. Retention/lifecycle obligations still apply. |
| Recompute history for the current selection | All displayed Weeks and Expected Percentile share the active selection; toggles can change historical ranks and best Week. One mutable cache slot must identify its current selection and validity. | Exclude unclassified rows or safely recompute under the chosen policy. | Potentially expensive across many Weeks/Cars on each toggle; batch reads and lazy version-checked cache refresh are needed. |
| Distinguish histories by canonical selection (**recommended**) | Toggling does not overwrite a different selection's readings. Within one selection each eligible Week contributes once, with coherent rank and Personal Best context. Refresh replaces the current reading rather than appending events. | Treat old selection-less rows as unclassified; exclude or safely recompute. No invented metadata. | More bounded cache rows and selection in unique identity/copy keys; still needs source-version invalidation and miss recomputation. Repeated visits can reuse valid per-selection readings. |

This comparison is qualitative, not a measured query-cost claim. A future implementation should
measure query counts, materialized rows and database plans for multi-Car/multi-Week history, keeping
arithmetic timing separate from source loading and wall-clock time. Batch Fields by `(WeekId, CarId)`
and Week windows by Season; do not call a single-Car interface in an N+1 loop. A proposed
owner/selection/Series index prefix must be evaluated against actual plans before adding indexes.

### Selection identity needs a policy, not a raw request string

Current `PersonalBestEvidence` semantics establish useful equivalences: uploads disabled means
Race-only regardless of upload arguments; uploads enabled with null or empty types means unrestricted;
restricted types are a distinct set with order irrelevant. Restricted predicates include `Unknown`
automatically, so adding explicit `Unknown` to Practice changes nothing, while `Unknown` alone is
not unrestricted. Unrestricted versus all presently known enum values needs a decision about future
enum additions and policy versioning. No new canonicalizer is implemented here.

## Proposed deep module

One module should own personal history source eligibility, owner resolution, canonical selection,
Field construction, presentability, replacement/removal, source versions, persistence and coherent
history aggregation. A small conceptual interface is sufficient:

```text
RefreshAsync(ownerScope, weekId, carIds, canonicalSelection)
    -> coherent per-Car reading, eligibility and Expected Percentile
ReadHistoryAsync(ownerScope, seriesId?, canonicalSelection)
    -> coherent history with its Personal Best and selection context
```

`ownerScope` must resolve the authenticated User and current Driver/provenance. Authorized
personal scopes require current proof and Personal consent; synthetic Demo preview personalization
uses its existing Demo Subject policy without claiming Real ownership. A raw claimed Customer ID
is insufficient for either scope. Read-only other-Driver calculation has no
capability to persist personal history. The module may use `AppDbContext` directly and existing
pure Core helpers; no generic repository or command framework is needed. Protected publication
still owns final dispatch: a returned reading/DTO is never proof of disclosure authority.

The implementation must keep exact Track ID identity, Race Week windows, tie arithmetic, the
subject's once-only Field contribution, and minimum presentable Field Size five under
[CONTEXT](../../CONTEXT.md) and [ADR 0002](../adr/0002-track-identity-follows-iracing-track-id.md).
Recommendation Rank ordering, UI presentation and world-record overlays remain outside the module.
Any change to Track/Week identity or the glossary's Expected Percentile meaning needs separate
approval and a domain/ADR update; this document records none as accepted.

Typed private uploads must stay in their authorized source path. Integrate their preparation and
purpose generations, exact source sets, original clocks, source-loss invalidation, final commit and
protected dispatch checks; never move them into the legacy quarantine reader. Selection-scoped
identity must reach both cache uniqueness and derivative copy keys. Driver reassociation, dormancy,
withdrawal, deletion and restore cannot transfer old authority to a new Driver or reset retention clocks.

## Decisions required before implementation

1. Choose one history meaning; accept, amend or reject the selection-scoped recommendation.
2. Define canonical selection identity and unrestricted behavior when Sim Session Types evolve.
3. Define Expected Percentile chronology: requested Week included, strictly earlier Weeks, and handling
   of later stored Weeks. The current implementation's inclusion is evidence, not the new contract.
4. Define a Week with no eligible Personal Best: remove its contribution, omit it, or preserve a
   properly labelled valid original reading. Small Field arithmetic alone does not answer this.
5. Choose the selection-less-row exclusion/recompute and rollout policy, including old-writer fences
   and new seed classification. Do not invent original metadata.

Existing consent/source/copy clocks and current Driver binding are mandatory boundaries, not
optional product choices. Final decisions should be recorded in the glossary/ADR only after approval.

## Implementation slices and genuine dependencies

These are future work, not authorization or completed implementation.

| Slice | End-to-end outcome | Prerequisite |
| --- | --- | --- |
| 1. Record decisions | Approved history, selection, chronology, missing-Personal-Best and legacy-row policies with examples. | Maintainer decision on this brief. |
| 2. Owner and selection contract | Canonical equivalence tests and module-resolved owner capabilities; existing route gates remain effective. | Slice 1. |
| 3. Coordinated storage and copy identity | If selection-scoped is chosen, schema/unique identity and typed copy keys change together; old writers are fenced, old rows handled honestly, fresh seed writes classified. | Slices 1–2; lifecycle/migration review. |
| 4. Shared refresh | Authorized batched sources and one refresh rule replace independent writers, including the typed upload producer; repeated calls, five-to-four-to-five and missing Personal Best behave coherently. | Slice 3 or the approved alternative storage policy; slice 2 owner contract. |
| 5. Coherent readers and protected acceptance | Analytics and recommendation Expected Percentile use the same module; ordering stays outside. Execute lifecycle/source races and protected HTTP cases. | Slice 4; existing privacy acceptance prerequisites. |

Preserve pure tests for Field ties, minimum size, Personal Best selection and Race Week windows;
preserve exact Track scoping, another-Driver non-overwrite and one-contribution-per-Week regression
properties. Keep protected lifecycle/source-race tests. The characterization assertions that expose
mixed selection or claim-based ownership should be replaced by approved module contracts when the
new behavior ships, retaining this observation record as historical evidence. Tests that merely
pre-seed cached ranks or duplicate averaging helpers should yield to successive service-operation
cases. The current Real compute guard must remain until authorized protected integration replaces it.

#352 is complete when this investigation and reproducible brief are published. Selecting semantics
and executing these slices are separate follow-up work; merging this documentation approves neither
selection-scoped history nor a Live eligibility claim.
