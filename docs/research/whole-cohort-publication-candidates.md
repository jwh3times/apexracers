# Whole-cohort publication candidates

Candidate contract version 1, prepared for [#373](https://github.com/jwh3times/apexracers/issues/373).
This record describes controlled synthetic engineering and its risk-review inputs. It supplies no
maintainer catalog admission, anonymity guarantee, provider permission or production publication.
The [accepted decisions](../design/driver-disclosure-ownership.md) and
[acceptance matrix](../design/driver-acceptance-and-rollout.md) remain the policy authority.

## Versioned catalog review artifact

The [machine-readable v1 artifact](whole-cohort-catalog-v1.json) records a concrete fabricated
seven-Driver Field, declared variants, expected ranges and explicit risk dispositions. Its Customer
IDs and exact inputs are internal fixture data, not candidate response fields. Six hidden Drivers
support an owner-inside variant alongside one separately consented synthetic Driver. The owner's
declared Personal Best is faster than their Race Best; the expected Percentile Rank is
`(6 + 0.5) * 100 / 7 = 92.85714285714286`.

| Property | Candidate v1 contract |
| --- | --- |
| Catalog identity | `synthetic-lap-rating-v1` |
| Schema | `whole-cohort-candidate-v1` |
| Scope | `synthetic-single-field-v1`: one complete normalized synthetic Field; no selectable predicates or caller-defined extra fields |
| Source provenance | Explicit Demo; fabricated identities and measurements only. Real and Unknown input is unavailable. No provider sentinel or wire-field mapping is selected. |
| Cohort unit | One normalized contribution per distinct Driver; duplicate observations cannot increase support |
| Hidden fields | Joint lap-seconds and absolute-rating ranges, with lower-inclusive/upper-exclusive decimal boundaries; an omitted measurement remains a supported joint missingness dimension |
| Named fields | Consented synthetic Driver name and the same permitted ranges, only in an explicitly reviewed signed-in audience |
| Owner fields | Exact authorized owner Percentile Rank using the existing `FieldPercentile.Rank` formula; no supplementary exact Field Size, Field Position, histogram, extrema or Top Share |
| Audience/owner variants | A finite declared set assessed together. An unlisted recipient/owner context is unavailable. Visitors receive ranges alone. |
| Binding | Catalog/schema/scope, complete-cohort content and revision, source/evidence, authority and enforcement dependencies, and reviewed audience/owner variants |
| Status | Controlled review fixture only; ordinary catalog availability remains closed |
| Pending publication gates | Actual maintainer review, atomic prior/pending/possibly-dispatched accounting in #374, current dependency checks and protected dispatch integration |

Internal contribution identities and dependency fingerprints are review inputs, never hidden row
keys, references, cursors or public metadata. The candidate is an internal review artifact, not a
response returned by a product endpoint. A controlled review decision cannot authorize Real input
or register a production catalog.

Eligibility, consent audiences, authority revisions and ranked owner measurements in these inputs
are controlled fixture declarations. They are not ownership proofs, persisted grants or journal
observations. Product integration must obtain a complete authorized snapshot from the existing
lifecycle/copy spine and revalidate it during combined-release admission and protected dispatch;
calling the candidate math with a claimed Customer ID cannot establish permission.

## Whole-cohort algorithm

Select one finest supported level for the complete catalog-declared audience/owner variant set,
before output pagination. Exclude each variant's owner and currently disclosed Drivers when testing
its hidden support. Every emitted joint group needs at least five distinct hidden contributors.
An audience or owner change cannot select a fresh finer level within that same artifact.

| Level | Lap-time width (seconds) | Absolute-rating width (points) |
| --- | --- | --- |
| Base | 0.5 | 250 within [1000,2500), 500 outside |
| 1 | 1 | 1000 |
| 2 | 2 | 2000 |
| 3 | 4 | 4000 |
| 4 | 8 | 8000 |

Origins are zero and intervals half-open. Decimal arithmetic preserves exact boundaries. The
exceptional base rating intervals nest within the 1000-point parents, including the transition
at 2500. Unsupported measurements are omitted before grouping; their visible omission cannot
create an unsupported leftover group. The catalog exposes no rating delta, incident, exact race
position, race identifier or timestamp.

Emit one summary per supported joint tuple, ordered using its displayed ranges only. Do not emit
an exact hidden count or repeat a summary once per contributor. Pages slice the already selected
representation; they do not rerun grouping or supply custom source filters. If support or review
fails, range-only fallback cannot recover a separate sparse statistic. The candidate stays
unavailable instead of exposing a failing count, source key or hierarchy diagnostic.

An authorized owner calculation retains its exact internal Field. Its percentile excludes that
owner's source row before adding the ranked Personal Best once, preserving tie splitting and the
existing formula. Coarsening hidden output does not alter that arithmetic. Publishing the exact
owner reading still requires composition review with all other candidate outputs.

## Adversarial review obligations

| Scenario | Required observation and risk disposition |
| --- | --- |
| Four/five/six hidden Drivers | Four cannot supply support; five can support an owner-outside or visitor variant; owner-inside variants need five other hidden Drivers. |
| Duplicate laps/observations | Repeating one Driver cannot manufacture a five-Driver group. Conflicting normalized contributions must not be silently treated as independent Drivers. |
| Joint sparsity | Separate well-supported lap and rating marginals do not rescue an unsupported joint cell. Widen the entire cohort or suppress. |
| Exact boundaries | Exercise half-second, every rating transition, zero-origin parent boundaries and values just below/at/above them. |
| Missing/unsupported measurements | Omitted dimensions still require joint support. No inferred provider sentinel validity rules. |
| Names and audience | Visitor output has no names/references. Sharing-eligible names contribute no hidden support. Unreviewed audiences remain closed. |
| Owner variants | One level covers all declared variants. Exact owner metrics and owner removal must be reviewed with hidden ranges; unlisted owners are unavailable. |
| Pagination/filter probes | Pages cannot change the selected level, add source filters, restore unsafe fields or emit source-derived pagination keys/counts. |
| External racing knowledge | Known classifications or auxiliary facts can identify members of otherwise supported groups. An unresolved or unsafe linkage assessment closes the context. |
| Repeated/overlapping candidates | Prior views, changing membership, owner readings and totals may disclose new equations. Missing prior-release assessment closes the candidate; fixture assessment is not an atomic ledger. |
| Corrections and consent changes | A changed complete-cohort fingerprint or revision/dependency invalidates the entire reviewed candidate, including a five-to-four support transition. |
| Useful positive | A reviewed supported synthetic context must produce ranges and permitted named/owner variants. Denying every context is not positive evidence. |

These are risk-review dimensions, not a general automatic test for safe external linkage. The
controlled fixture supplies explicit decisions and regression data. Actual catalog admission must
review the proposed fields, external knowledge, prior/overlapping releases and usefulness with
documented per-context rationale. The number and existence of occupied groups convey information
even when exact counts are omitted.

## Evidence and remaining integration

Run the reproducible candidate suite from the repository root:

```bash
dotnet test --configuration Release --filter-class ApexRacers.Tests.Services.WholeCohortCandidateTests \
  --report-xunit-trx --report-xunit-trx-filename whole-cohort-candidates.trx \
  --results-directory ./TestResults
```

The owned implementation lives in `ApexRacers.Core.WholeCohortBands` and
`ApexRacers.Api.Services.WholeCohortCandidates`. Their inputs are immutable normalized fixture
snapshots, not upstream SDK payloads or user-supplied HTTP request data. The controlled review binds
the full snapshot and all declared variants. Source, consent, evidence or enforcement changes make
that review unusable; paging an offline artifact is not a current-authority check or a dispatch.
Feature callers cannot use this module to obtain protected publication admission.

Local execution on 2026-10-06 used the implementation working tree based on `64fc27e8`.
`TestResults/cohort373-final/execution-context.json` records final source hashes, provenance and
non-admission status; its commit field identifies the base, not a committed implementation release.

| Check | Observed outcome |
| --- | --- |
| Focused synthetic candidate suite | 46 passed, none skipped |
| Initial complete Release backend run | 1,184 passed, none skipped; before the final malformed-input guards and formatting |
| Final-source Release candidate cases | All 46 passed in the complete backend run |
| Final-source complete backend run | 1,183 passed; one existing auth setup test failed on Windows socket error 10048 before its token-cap assertion |
| Final-source coverage | 95.60% line, 87.83% branch; both exceed 85% |
| Exact failed auth method in a fresh isolated run | Passed after socket state cleared; this does not turn the failed complete run into a pass |
| Formatting, generated config and neutrality checks | Passed |

The harness failure is tracked separately in
[#391](https://github.com/jwh3times/apexracers/issues/391). Windows logged TCP/IP event 4227 at the
failure; read-only observation found 5,089 `TIME_WAIT` sockets to the test PostgreSQL endpoint.
The unmodified fixture disables pooling. These observations support a connection-churn/address-reuse
problem, not a confirmed diagnosis of complete port exhaustion. Failed and recheck TRX reports are
retained separately. No production or global network settings were changed, and no test exception
was suppressed. This bounded result is not a clean final complete-suite verdict or integrated
publication acceptance.
[Microsoft's troubleshooting guidance](https://learn.microsoft.com/en-us/troubleshoot/windows-client/networking/tcp-ip-port-exhaustion-troubleshooting)
distinguishes a large `TIME_WAIT` population from confirmation that available ports are exhausted.

Ship-time review subsequently found decimal division could round an extremely precise value into
the next half-open band. Remainder arithmetic now computes exact boundaries, with four additional
regressions for positive values just below a boundary and negative values close to zero. A further
regression verifies unrepresentable endpoints close the candidate. All 51 focused cases passed.
The local
full-run coverage above belongs to the earlier source hashes; corrected-source full-suite and
coverage verification belongs to PR CI. The historical Windows failure remains tracked in #391.

The candidate tests contribute bounded evidence to PUB-01–06 and PUB-09. A useful controlled fixture
supports the engineering portion of PUB-10; it does not satisfy that row's actual catalog-admission
requirement. #374 owns atomic combined-release review/reservation. #380 owns the complete integrated
matrix and required check. Existing Driver authorization, copy fences and legacy route denials
remain prerequisites; this module does not create another acquisition or publication path.

The existing private catalog-review follow-up remains conditional on both #373 candidate artifacts
and #374 composition evidence being available. No live operation or flag change is part of this
slice. Material schema, context, audience, evidence, authorization or prior-release changes require
new assessment rather than copying an old review to a new catalog identity.
