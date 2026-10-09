# Scoped Driver references and private Follow integration

Implementation for [#375](https://github.com/jwh3times/apexracers/issues/375), following the
[accepted architecture](../design/driver-disclosure-ownership.md),
[acceptance matrix](../design/driver-acceptance-and-rollout.md) and
[atomic release accounting](atomic-publication-reservations.md).

This is controlled synthetic implementation evidence. Ordinary startup provides no reference
catalog/review/history adapter, and its proof/journal adapters remain unavailable. The new routes
therefore return generic unavailable responses in ordinary environments. Existing credential-free
Demo routes retain their explicitly synthetic behavior. No provider shape, live ownership,
production journal, catalog admission, browser invalidation or deployment certification is inferred.
The separately accepted exact #374 fabricated template is unchanged; it does not admit the new
reference fields, purposes, source or response representations.

## Owned publication and reference contract

`ScopedDriverPublication` owns discovery, detail, comparison and Follow responses. Controllers
extract the authenticated JWT `sub` and return its protected `IActionResult`; they release no DTO
or lease for MVC to serialize later. The actual executor reads the executing request's authenticated
principal again. Query/header actor or provenance values cannot establish permission.

References use 256 random bits; the database stores only a SHA-256 hash. Each reference binds its
allowed Detail, Comparison or Follow purpose, the recipient's original Personal grant/proof/revision,
the target's original Sharing grant/proof/revision, and Demo provenance. Expiry is the original
issue time plus fifteen minutes; at the expiry boundary it is unusable. This does not expire consent.
Every use, admission and final unsent dispatch rechecks current authority, journal vetoes, expiry,
actual recipient, full source/catalog fingerprint and original copy/purpose receipt. A name or
proof-generation change between discovery selection and token issuance rejects the whole prepared
output. Reassignment, another recipient, another purpose, a raw Customer ID or an old Follow cannot
substitute for those checks.

Discovery emits only eligible consented names with three separately scoped references and their
expiry. Detail emits that current name and the controlled official lap/rating fields. Comparison
emits the current target name, the recipient's and target's official best lap seconds and their
lap delta. No raw Customer ID, grant/proof identifier, Follow key, uploads or private history appears
in these owned contracts. Hidden contributors receive no individually addressable representation.
Invalid, ineligible, nonexistent, expired and unavailable authenticated target requests receive the
same `503` ProblemDetails body and `Cache-Control: no-store`; unauthenticated requests receive the
ordinary authentication challenge without target data.

Publication uses `PublicationReleaseStore` under advisory lock 370, sharing coordination with grant,
copy and lifecycle writers. Independent reservation intent precedes primary admission. Completed,
pending and possibly dispatched output remains in the same conservative history; only the ended
owned executor may checkpoint terminality. Both target and recipient authorization dependencies
participate in withdrawal drain. Dispatch/source failure never resets disclosure history or infers
that a writer stopped. Generic denials finish their HTTP transport before the durability checkpoint;
an unavailable checkpoint cannot truncate that denial or establish terminality. Uncertain independent
reservation/dispatch intent remains counted, and started or uncertain dispatch is never marked
proven unsent. Follow mutations occur in the admitted executor, after current authenticated
eligibility; delayed requests cannot mutate a Follow using a stale generation.

## Private Follow clocks and copies

`PrivateDriverFollow` stores only the private internal pair of original authorization associations,
creation time and lifecycle state. It stores neither names nor Customer ID/name snapshots. Lists
issue fresh references solely for currently active, eligible associations. Follow membership is
rechecked at admission and dispatch, including withdrawal/regrant between initial list selection
and later target resolution.

Journal-first intent immediately hides affected Follow views before primary closure. The primary
lifecycle transaction records inactivity and original loss, day-90 reactivation cutoff and day-97
physical-removal maximum. Target Sharing/Personal/proof/binding loss and relevant recipient
Personal/proof/binding loss participate; recipient Sharing withdrawal alone does not withdraw that
recipient's Personal workflow. Fresh eligible proof/consent can reactivate only the same original
pair strictly before its original day 90, via a new Follow-purpose reference. Retries and reactivation
retain the original clocks. A withdrawn reference never revives. After the old inactive object is
physically erased, a separate explicit Follow request with a fresh currently eligible reference can
create a new object with its own ID and creation time. It imports no deleted metadata, history or
old clocks; this is new authorized collection under Q17, not restoration of the erased Follow.
Physical erasure does not create a permanent prohibition on following the same Driver.

Cleanup removes inactive associations promptly at day 90; day 97 is the maximum, not a required
wait. User deletion tightens removal to original request plus seven days, and completed User erasure
removes all owned reference/Follow metadata alongside private copies/account rows. The existing
thirty-minute cleanup service performs reconciliation. Expired reference rows are physically deleted.
Demo teardown removes these synthetic stores after required lifecycle drain, and fresh-preview
checks reject lingering associations. Release/enforcement history remains under its separate policy.

## Audited route inventory

| Entry point | Current boundary |
| --- | --- |
| `GET /api/drivers/scoped/discovery` | Actual JWT recipient, current eligible Sharing names; protected reference issuance/publication. |
| `GET /api/drivers/scoped/follows` | Current private active associations; fresh references, final active-membership recheck; no legacy Rival promotion. |
| `POST /api/drivers/scoped/detail` | Body-based Detail reference; protected authorized official detail. |
| `POST /api/drivers/scoped/comparison` | Body-based Comparison reference; exact controlled official lap comparison. |
| `POST /api/drivers/scoped/follows` | Body-based Follow reference; current eligibility and original recovery clocks; protected acknowledgement. |
| Legacy `/api/users/me/rivals` list/add/delete/search/suggestions | Real/Unknown remain unavailable before model binding; no caller-supplied Customer ID/name or saved Rival authorizes the new store. Explicit Demo behavior retained. |
| Legacy `/api/users/me/compare?rivalCustId=…` | Real/Unknown unavailable; raw-ID comparison cannot bypass scoped detail. Demo retained. |
| `/api/subsessions/{id}`, `/api/subsessions/{id}/laps?customerId=…` | Unintegrated Real/Unknown official detail and lap traces unavailable, including direct/alternate IDs. Demo retained. |
| `/api/series/{id}/standings`, `/tt-standings`, `/qualify-results` | Unintegrated Real/Unknown Driver detail unavailable; no reference permission inferred from standings/position or warm cache. Demo retained. |
| `/api/series/{id}/weeks/{week}/cars/{car}/percentile?customerId=…` | Arbitrary Real/Unknown Driver percentile remains unavailable; no raw-ID alternative. Demo retained. |
| `/api/users/me/profile-stats`, `/progression`, `/races`, `/achievements`, `/analytics`, `/recommendations` | Unintegrated Real/Unknown owner/Driver responses unavailable. Demo retained. |
| `/api/series/{id}/weeks/{week}`, `/cars`, `/my-percentiles`, `/strategy` | Unintegrated Real/Unknown Field/Driver-dependent views unavailable. Demo retained. |
| `/api/leaderboards`, `/api/race-guide` | Unintegrated Real/Unknown identifying racing surfaces unavailable. Demo retained. |
| `/api/telemetry/upload`, `/api/telemetry/laps` | Legacy attributed uploads and reads unavailable in every namespace; no recorder/claim/reference grants private upload authority. |
| Auth/Admin/FeatureFlags, catalog Cars/Tracks/Series and Schedule | Independent existing access; catalog/Schedule still omit private overlays. A claim/profile field does not become another Driver lookup route. |

New reference routes are exempt from the legacy filter only because their own module owns all
protected dispatch. Missing adapters do not fall through to any legacy service. The separate
controlled cohort executor continues its original reviewed projection and hidden-summary contract;
this slice adds no references to hidden summaries or new detail permission through its band groups.

## Schema and deployment boundary

Migration `20261008132131_ScopedDriverReferences` adds two initially empty stores, restrictive grant
relationships, uniqueness and purpose/provenance/revision checks. It installs SQL fences for immutable
reference identity/lifetime (at most fifteen minutes), original Follow binding/clocks, current grant
requirements and grant-loss closure, including raw SQL recipient binding/proof loss. Down refuses to
remove enforcement; recovery is forward-only. Migration-chain tests exercise these actual triggers,
not only `EnsureCreated`. Stored claims and legacy Rivals are never promoted or assigned fabricated
historical loss times.

This additive migration requires no new Demo purge/reseed or live activation. Existing legacy fences
remain. #377's [physical migration/restore evidence](driver-migration-restore.md) now exercises
mixed-version rollback and current independent enforcement/history. Deployed verification remains
separate; this local work makes no production observation or erasure claim.

## Controlled execution and limits

The dedicated test executable starts two actual loopback Kestrel processes. Its isolated PostgreSQL
primary and independent history databases have validated fixture-only names. Actual JWT validation
and the real `ScopedDriversController` exercise recipient extraction, resource-filter denial and
owned response execution. Fault/control endpoints and accelerated clock are exclusively test-host
infrastructure, never registered by ordinary startup.

The independently fabricated `controlled-reference-template-v1` declares two original synthetic
associations, official lap seconds `89.90` / `90.16`, ratings `1500` / `1350`, bounded fields and the
fixed recipient. Original copy ID/version, purpose ID/generation/version, acquisition/creation clocks,
complete values, source scopes, catalog revision and independently continuous history are pinned.
The fixture composition reviewer accepts only that declared named source/recipient/dependency catalog
and rejects counted output from any other catalog. It is a controlled test review, not a maintainer
admission or general anonymity/composition classifier. A same-values replacement evidence copy
invalidates the original catalog. The existing #374 catalog/history remains separately unchanged.

Reproduce with Docker running:

```bash
dotnet test --filter-namespace ApexRacers.Tests.References \
  --report-xunit-trx --report-xunit-trx-filename driver-references.trx \
  --results-directory ./TestResults/reference375-focused
```

The cases cover useful discovery/detail/comparison/Follow, authenticated recipient/purpose replay,
generic outcomes, raw-ID and alternate-route denial, cross-provenance denial, unsent expiry at
before/at/after fifteen minutes, source/catalog/history/journal faults, original receipt replacement,
actual writer drain, name-generation/list-membership races, immediate journal-first Follow inactivity,
fresh-proof recovery before/at/after day 90, physical absence by day 97, original retries, User deletion
and migration SQL fences. Scenario/context records are under the test executable's
`TestResults/driver-references/`; the run's TRX owns pass/fail.

Final Linux x64 Release verification on 2026-10-08 passed all 1,285 backend cases, including all
57 References cases, with no failures or skips. The measured five-product-assembly report records
95.82% line and 87.27% branch coverage. Reports are
`TestResults/verified-coverage/backend-tests.trx` and
`TestResults/verified-coverage/coverage.cobertura.xml`. The frontend's 803 cases, build, lint and
format checks also passed; its line/branch coverage is 97.93% / 91.43%.

Use the SDK launcher with explicit project and absolute coverage-settings/results paths:

```bash
dotnet test --project src/ApexRacers.Tests/ApexRacers.Tests.csproj \
  --configuration Release \
  --coverage --coverage-output coverage.cobertura.xml --coverage-output-format cobertura \
  --coverage-settings "$PWD/coverage.runsettings" \
  --report-xunit-trx --report-xunit-trx-filename backend-tests.trx \
  --results-directory "$PWD/TestResults/verified-coverage"
```

The installed SDK is 10.0.112. Direct apphost execution passed the earlier suite but produced an
empty `N/A` coverage report; it is not coverage evidence. The SDK command above produced measured
coverage using the same settings. CI rejects reports without product packages and positive
line/branch denominators. Controlled evidence contributes REF-01–02, AUTH-02/06–07 and HTTP-01/03
within these named boundaries. [#376 browser evidence](driver-browser-validity.md) and
[#377 restore evidence](driver-migration-restore.md) add separate controlled scenarios. #378 real
adapters, #379 pilot scope, #380 integrated acceptance and actual catalog review remain distinct.
