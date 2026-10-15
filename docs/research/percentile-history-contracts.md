# Percentile history selection and owner contracts

Implementation slice [#425](https://github.com/jwh3times/apexracers/issues/425) follows
[accepted ADR 0007](../adr/0007-percentile-history-is-scoped-by-evidence-selection.md).
It establishes contracts for the shared history module. The subsequent
[storage cutover](percentile-history-storage.md) implements [#426](https://github.com/jwh3times/apexracers/issues/426).
Shared refresh and coherent reader integration remain in
[#427](https://github.com/jwh3times/apexracers/issues/427) and
[#428](https://github.com/jwh3times/apexracers/issues/428).

## Selection identity

Canonical selection and owner identity data now live in `ApexRacers.Core.PercentileHistory`, shared
by API, Data and Seeder. The API retains owner resolution, result contracts and pure contribution
accounting. The module owns immutable, canonical evidence selections under an explicit reviewed policy
version. Race-only ignores uploaded-type arguments. Null and empty uploaded selections mean
unrestricted; restricted selections are sets with order and repetitions ignored and the existing
implicit inclusion of Unknown preserved. Unknown-only is restricted, and an explicit list of all
currently supported types remains distinct from unrestricted.

The policy's allowed type set is explicit. Adding an enum value alone does not change unrestricted
history meaning: a reviewed policy-version change and invalidation/recomputation of affected
readings are required. Unknown upstream session taxonomy is still outside this contract.

## Owner identity and authority

Demo preview and authorized Personal history have distinct resolution paths. Both begin with the
authenticated User, never an arbitrary target Customer ID or a stored claim. Demo uses the existing
Subject Driver policy and selected provenance. Personal resolution uses the User's current active
association and the existing authorization/journal checks for Personal consent.

An owner snapshot identifies the history context at resolution time; it is not a reusable grant,
proof of current authority, write admission or permission to disclose a returned result. Later
integration must repeat the existing current-authority, preparation/source-generation, final commit
and protected-dispatch checks. A pure data key is equally insufficient. Another Driver's lookup
cannot become a request to persist the caller's own history merely by supplying that Customer ID.
Ordinary unavailable Real proof/journal adapters and legacy route gates remain in place.

## Contribution and result contracts

History results carry a classified selection, owner identity and Personal Best context together.
An Uploaded Lap also carries its winning session type and must satisfy that selection; a Race Lap
cannot carry an uploaded session type. Explicit empty source sets can describe missing evidence,
but a positive Personal Best requires source dependencies. Source ordering does not change identity.
Preparation generation is positive and its version may be zero; source versions and reading revisions remain positive.
For a given Car, Series and selection, each distinct Week contributes at most once. Eligible Weeks
have a Personal Best, a Field of at least five, and a start date at or before the requested Week's
start date. Distinct Week identities may cross Seasons. Missing evidence and undersized Fields
contribute nothing; an empty eligible history has no Expected Percentile, rather than zero.

Conflicting snapshots of the same Week cannot be reconciled by selecting an arbitrary revision or
newest computation clock. The contribution module rejects conflict; the future authorized source
and persistence module must produce the coherent snapshot. Pure aggregation does not establish
source freshness or authorization. Exact Track identity, Week bounds, tie arithmetic and the
Subject's once-only Field contribution remain governed by the existing Core modules.

The batch request/result contracts describe the future refresh seam without exposing a generic
repository, adding a throw-only writer implementation, or registering a new runtime producer.
Recommendation ordering, presentation and world-record overlays remain outside history ownership.

## Validation and limits

Focused tests exercise canonical equivalence and separation, versioned type admission, chronology,
missing evidence, presentability, duplicate contributions and controlled owner resolution.
PostgreSQL owner cases exercise actual synthetic authorization and independent journal behavior;
they do not establish Live permission or protected HTTP/migration acceptance. Legacy Demo history
writers and readers retain their current behavior; the storage slice adds
classified typed private-upload and fresh Demo seed producers without switching ordinary readers.
Shared refresh and coherent readers remain dependent work.

Reproduce the three focused classes from the repository root with Docker running:

```bash
dotnet test --project src/ApexRacers.Tests/ApexRacers.Tests.csproj \
  --filter-class ApexRacers.Tests.Services.PercentileSelectionTests \
    ApexRacers.Tests.Services.PercentileHistoryContributionTests \
    ApexRacers.Tests.Services.PercentileHistoryOwnerTests
```

The focused run passed 26 cases with no failures or skips. Full suites, coverage and the integrated
privacy contract run separately in CI. The controlled journal in the owner tests is a test adapter;
these cases do not exercise durable journal deployment or protected HTTP writer terminality.
