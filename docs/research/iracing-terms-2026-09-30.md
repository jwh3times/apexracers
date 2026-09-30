# iRacing third-party terms and ApexRacers identity audit

Reviewed 2026-09-30. Public-source engineering research; interpretation is identified below and does
not resolve the legal applicability of every clause to ApexRacers.

The current implementation has four main screen families that expose other Drivers by name:
race detail, series standings, global leaderboards, and rivals/comparison. Identity handling
requires changes across persistence, caching, APIs and UI. Core recommendations and field
analytics can remain useful without competitor names, subject to permission for the underlying
data processing. The implementation inventory below explains the evidence and scope.

## The September notice

The [third-party development notice][notice] shows **Modified on September 30, 2026, 10:41 AM**.
That verifies modification, not original publication. It contains no OAuth registration or reopening
schedule. Inferring an imminent reopening would be speculation.

The notice says iRacing does not review, pre-approve, or authorize third-party developers. API keys
and OAuth credentials grant technical access, without permitting otherwise prohibited activity.
Third parties using those mechanisms need the affected member's **"explicit, affirmative, and
revocable consent"** before exposing either their display name or customer identifier **"in any
context"**. It also reserves changes to terms/data access and identifies developers' independent
data-protection obligations. [Source: notice][notice].

**Engineering interpretation:** a logged-in page is not an established exemption. OAuth login alone
does not demonstrate consent to identity publication. Hiding rendered text while returning identifiers
in JSON still exposes them. Withdrawal must affect subsequent disclosures, including cached outputs.
The notice does not itself prescribe database deletion, prohibit every internal cache, or require
erasure of all anonymous derived statistics. [Basis: notice][notice].

## Current underlying documents

The [terms landing page][terms-page] identifies August 5, 2026; its linked [EULA][eula] says Updated
August 2026. The explicit third-party identity-consent sentence was found in the notice, not the
EULA's extracted text.

| EULA provision | Relevant content |
| --- | --- |
| 3, page 2 | Limited license for personal, noncommercial entertainment, training, and education. |
| 4.1, pages 2-3 | Claims ownership of service-generated data and statistics, including a member's own statistics. |
| 5.1, page 3 | Addresses display-name consent within the Sim. |
| 6.1, page 4 | Restricts copying, collecting/mining, and making data available for value. |
| 6.2-6.3, page 4 | Derivative works and commercial exploitation require written consent. |
| 6.5, page 5 | Restricts connection methods and references interfaces provided for personal, noncommercial use. |
| 13.1, page 9; 22, page 13 | Reserves account termination, service changes, and access restrictions. Section 22 includes a notification provision for material terms changes and arbitration exceptions. |

These clauses require a separate assessment of hosted analytics, collection, and monetization;
removing names cannot establish permission for those activities. [Source: EULA][eula].

The [Website Conditions of Use][website] (July 2, 2025), page 1, restrict downloading except page
caching, derivative/commercial use, and data gathering without written consent. Page 2 claims rights
in data compilations. **Interpretation:** the page-caching exception does not establish permission
for a hosted `/data` result archive. [Source: Website Conditions][website].

The [Privacy Policy][privacy] (linked file dated August 11, 2026) describes iRacing's own processing.
Sections 1, 3, 5, and 7 discuss identifiers, racing statistics, and public availability. Section 8
limits the policy to iRacing's collection. Section 11 describes purpose-based retention; section 12
addresses qualified erasure rights and practical limitations. These are not a third-party API cache
retention schedule or blanket republication permission. [Source: Privacy Policy][privacy].

The [Supplemental EU Terms][eu] (August 2026), sections 1 and 3-5, address DSA obligations,
moderation, and redress, with precedence for applicable conflicts. They do not supply an API
registration timeline. [Source: Supplemental EU Terms][eu].

## Remaining uncertainty

No concrete third-party API retention period, consent-record format, or revocation deadline was
identified in this review. Server-side retention, aggregate reuse, and commercial rights need their
own assessment; disclosure consent does not settle them. Replacing a name with a stable identifier
also does not, by itself, demonstrate anonymity.

The web reader could not retrieve the notice and HTML landing pages. Direct HTTPS retrieval
succeeded; the linked official PDFs were readable. Findings use those first-party documents, not
search snippets or older EULA versions. No raw provider documents were added to the repository.

[notice]: https://support.iracing.com/support/solutions/articles/31000179757-third-party-development-please-review-the-iracing-terms
[terms-page]: https://www.iracing.com/terms-use-eula/
[eula]: https://ir-core-sites.iracing.com/members/pdfs/20260805-iRacing_Terms_of_Use_and_EULA_dated_Aug_05_2026.pdf
[website]: https://ir-core-sites.iracing.com/members/pdfs/20250702-iRacing_Website_Conditions_of_use_dated_July_02_2025.pdf
[privacy]: https://ir-core-sites.iracing.com/members/pdfs/20260811-iRacing_Privacy_Policy_dated_Aug_11_2026.pdf
[eu]: https://ir-core-sites.iracing.com/members/pdfs/20260805-iRacing_Supplemental_EU_Terms_dated_Aug_05_2026.pdf

## ApexRacers implementation audit

Audited on 2026-09-30 at public-repository commit
`e82cf56e64b7af6165246f28ddda8fbbccebbc03`. This is a static code and public-documentation
audit, not an inspection of the deployed database, current feature-flag values, member counts,
or live iRacing responses. No claim below establishes that real member records are currently
being served in production.

### Where other Drivers appear

| Surface | Current output and access | Other-Driver identity dependency |
| --- | --- | --- |
| Race detail, `/races/:subsessionId` | The classified individual field: driver name, customer ID in JSON, positions, laps, incidents and rating changes. Race-result API is anonymous; UI falls back to the actual customer ID when a name is empty. | Direct, for every represented Driver in an ingested race. [UI](../../web/src/features/racing/RaceDetailPage.tsx), [service](../../src/ApexRacers.Api/Services/SubsessionDetailService.cs), [controller](../../src/ApexRacers.Api/Controllers/SubsessionController.cs). |
| Series standings, `/series/:seriesId/standings` | Championship, Time Trial and qualifying tabs; each service keeps up to 100 rows. Names on screen and customer IDs in JSON. APIs allow anonymous access. | Direct, across three standings views. [UI](../../web/src/features/series/StandingsPage.tsx), [service](../../src/ApexRacers.Api/Services/StandingsService.cs), [controller](../../src/ApexRacers.Api/Controllers/StandingsController.cs). |
| Global leaderboards, `/leaderboards` | Up to 200 Drivers per category, with names, customer IDs, ratings and other statistics. Signed-in access. | Direct. [UI](../../web/src/features/rivals/LeaderboardsPage.tsx), [service](../../src/ApexRacers.Api/Services/LeaderboardService.cs), [controller](../../src/ApexRacers.Api/Controllers/LeaderboardController.cs). |
| Rivals and comparison, `/compare` | Name search, up to 12 suggested rivals from shared races, saved follows, both named profiles, rating histories, shared-race finishes and track pace. Signed-in access. | Central to the feature. A Rival need not have an ApexRacers account. Comparing a customer ID does not require the target to have consented or even be followed. [UI](../../web/src/features/rivals/ComparePage.tsx), [rival service](../../src/ApexRacers.Api/Services/RivalService.cs), [comparison service](../../src/ApexRacers.Api/Services/RivalComparisonService.cs), [controller](../../src/ApexRacers.Api/Controllers/CompareController.cs). |
| Per-Driver lap API | Signed-in callers may supply any `customerId`; response returns that ID plus lap trace and pace statistics. The current race-detail UI requests only the caller's claimed identity. | Wider API capability than the visible UI. [controller](../../src/ApexRacers.Api/Controllers/SubsessionController.cs), [service](../../src/ApexRacers.Api/Services/LapDataService.cs), [UI](../../web/src/features/racing/RaceDetailPage.tsx). |
| Car/week percentile lookup | API accepts an arbitrary subject customer ID and returns it with that Driver's metrics; the page offers manual lookup when the User has no claimed ID. Competitor identities are absent from the distribution. | The selected subject can be another Driver, even though the field is aggregate-only in the response. [UI](../../web/src/features/series/PercentileCarPage.tsx), [controller](../../src/ApexRacers.Api/Controllers/PercentileController.cs), [service](../../src/ApexRacers.Api/Services/PercentileCalculationService.cs). |
| Telemetry upload receipt | Returns the file's Driver name and customer ID to the uploader. Persists the recorder ID, not the name. Mismatched claimed IDs are rejected, but an uploader with no claimed ID is allowed. | Usually personal, but file possession is not verified member ownership or publication consent. Error text on a mismatch also includes IDs. [service](../../src/ApexRacers.Api/Services/TelemetryUploadService.cs), [UI](../../web/src/features/telemetry/TelemetryPage.tsx). |

These are **four main named-competitor screen families**, plus arbitrary-Driver API/lookup
paths and the telemetry edge case. This is a feature inventory, not a percentage of usage or
a count of actual affected members. The owned JSON response contracts expose both
`CustomerId` and `DriverName` across these features; a hidden customer ID remains available to
the browser even when no table column prints it.
[Response contracts](../../src/ApexRacers.Api/Dtos/ResponseDtos.cs).

### What remains useful without competitors' names

- **Car recommendations, week/car field statistics, percentile distributions and personal
  analytics:** calculations use other Drivers' results, but return car ordering, lap times,
  field sizes, medians, percentiles and distributions rather than competitor name lists.
  Internally they group by customer ID to count each Driver once, so permission to retain and
  process the underlying field data still matters.
  [Recommendations](../../src/ApexRacers.Api/Services/CarRecommendationService.cs),
  [week statistics](../../src/ApexRacers.Api/Services/WeekCarStatsService.cs),
  [percentiles](../../src/ApexRacers.Api/Services/PercentileCalculationService.cs),
  [analytics](../../src/ApexRacers.Api/Services/UserAnalyticsService.cs).
- **World-record gap:** only a fastest lap time is mapped and cached; record-holder names/IDs
  are not returned by this service.
  [World-record service](../../src/ApexRacers.Api/Services/WorldRecordService.cs).
- **Own race history, progression, profile and achievements:** primarily the resolved Subject
  Driver's data, not named competitors. Recent-race rows do not include the winner's name.
  Ownership/consent still needs addressing because the current identity is asserted.
  [Race history](../../src/ApexRacers.Api/Services/RaceHistoryService.cs),
  [driver statistics](../../src/ApexRacers.Api/Services/DriverStatsService.cs),
  [achievements](../../src/ApexRacers.Api/Services/AchievementsService.cs),
  [subject resolution](../../src/ApexRacers.Api/Services/SubjectDriverContext.cs).
- **Schedules, weather, BoP strategy, race-start guide, cars and tracks:** no competing Driver
  names in their response contracts. Personal uploaded bests are User-scoped.
  [Response contracts](../../src/ApexRacers.Api/Dtos/ResponseDtos.cs),
  [schedule](../../src/ApexRacers.Api/Services/ScheduleService.cs),
  [strategy](../../src/ApexRacers.Api/Services/StrategyService.cs).

Absence of competitor identities is an engineering property, not a conclusion that every
underlying collection, retention or derivative use is permitted by the provider's terms.

### Existing identity and retention design

There is no implemented member-publication consent record, consent withdrawal path, or
consent check on the audited read paths. `ApplicationUser` stores a manually supplied
`IRacingCustomerId`; changing it requires the local account's password but no iRacing proof.
OAuth callback handling throws `NotImplementedException` and the controller returns 501.
A future successful OAuth exchange would establish identity only to the extent the provider
supports it; the application must separately establish the member's affirmative permission
for the relevant identity displays.
[User model](../../src/ApexRacers.Data/ApplicationUser.cs),
[auth service](../../src/ApexRacers.Api/Services/AuthService.cs),
[auth controller](../../src/ApexRacers.Api/Controllers/AuthController.cs),
[domain definitions](../../CONTEXT.md).

Names and IDs are not confined to a single identity map:

| Store | Relevant contents | Consequence for withdrawal handling |
| --- | --- | --- |
| `SubsessionResult` | `CustId` plus a `DisplayName` snapshot on each ingested result. | The same member's identity can appear in many historical race rows. [model](../../src/ApexRacers.Core/Models/SubsessionResult.cs), [mapper](../../src/ApexRacers.Ingestion/SubsessionMapper.cs). |
| `Rival` | `RivalCustId` plus `DisplayName` snapshot, per following User. | Removing one follow affects only that User's row. Other followers' copies remain. [model](../../src/ApexRacers.Core/Models/Rival.cs), [service](../../src/ApexRacers.Api/Services/RivalService.cs). |
| `ExternalDataCache` | Mapped JSON containing names/IDs for search, standings and leaderboards; member profile names and customer IDs in member/lap cache keys. | Fresh cached responses bypass upstream fetching and have no consent recheck. Search freshness is 30 minutes, profile freshness 6 hours, standings/leaderboards/laps 24 hours. Cleanup runs every 6 hours with a 2-day post-expiry grace; TTL expiry is not immediate physical deletion. [cache](../../src/ApexRacers.Api/Services/CachedIRacingClient.cs), [keys](../../src/ApexRacers.Api/Services/IRacingCacheKeys.cs), [cleanup](../../src/ApexRacers.Api/Services/ExternalDataCacheCleanupService.cs). |

The public privacy page expressly describes showing other Drivers who never joined ApexRacers,
and says account deletion leaves ingested results, other Users' follows and independently
expiring cache entries. Account deletion therefore does not currently amount to a system-wide
withdrawal of identity display.
[Privacy page, sections 2 and 6](../../web/src/pages/PrivacyPolicyPage.tsx).

Two existing architectural decisions deserve reassessment under the notice: the deliberate
snapshot/bare-customer-ID model, and the assumption that availability in iRacing justifies
arbitrary-Driver lap reads. The latter ADR establishes intentional product behavior; it does
not establish a third-party redistribution license.
[ADR 0001](../adr/0001-drivers-referenced-by-customer-id.md),
[ADR 0004](../adr/0004-lap-traces-are-readable-for-any-driver.md).

### Feature flags and the meaning of "today"

The React router hides the iRacing surface unless `iracing-live` or `iracing-demo` is enabled.
Demo subject resolution substitutes the synthetic Demo Driver. These are not blanket API
authorization checks: the audited anonymous race-result and standings actions do not consult
those flags, and the cache can serve an existing unexpired row without live credentials.
A synthetic-only deployment is different from serving real members' records, but source code
alone does not verify which dataset a deployed instance currently holds.
[Router](../../web/src/App.tsx),
[subject resolution](../../src/ApexRacers.Api/Services/SubjectDriverContext.cs),
[demo constants](../../src/ApexRacers.Core/DemoData.cs),
[cache](../../src/ApexRacers.Api/Services/CachedIRacingClient.cs).

### Engineering assessment

**The identity rule implies a substantial change across storage and API responses, while much
of the core analytics experience could retain its value.** Race tables and standings can
technically present positions and metrics without member identities. Named rival discovery
and persistent named comparisons suffer the biggest product impact unless limited to
members who explicitly opt in.

An adequate identity-display design would require verified consent ownership, an explicit
scope and withdrawal mechanism, server-side decisions covering both names and customer IDs,
and consistent treatment of historical rows and fresh/cached responses. A central identity
and consent boundary would make that tractable without needing a complete mirror of all
iRacing members. Removing names in React, replacing them with the actual customer ID, or
waiting for cache TTLs would not meet the notice's stated display rule.

Any anonymous row handles must avoid exposing the upstream identifier; merely using a stable
alias does not prove anonymization, especially when exact race context can identify a person.
Whether internal IDs and non-identifying aggregates may be retained, and whether this hosted
analytics/data-processing model is permitted, remains a separate provider-terms question.
The audit does not establish that withdrawal requires erasing every aggregate statistic.
