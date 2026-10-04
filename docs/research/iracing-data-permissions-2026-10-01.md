# iRacing data-use permissions: evidence and remaining decisions

Reviewed 2026-10-01 for [Establish the evidence and unknowns for hosted iRacing data use](https://github.com/jwh3times/apexracers/issues/355), under the [iRacing data and identity plan](https://github.com/jwh3times/apexracers/issues/354). This public-source engineering note extends the [September 30 audit](iracing-terms-2026-09-30.md); it does not repeat the implementation inventory or establish legal permission for ApexRacers.

**Historical review scope:** this is the 2026-10-01 public-source snapshot, not a current
permission decision or a fresh check of provider registration. Later supplied-policy review and
maintainer follow-ups are separate evidence. The open questions below record that date's planning
frontier; use the [accepted architecture](../design/driver-disclosure-ownership.md) and
[acceptance/rollout plan](../design/driver-acceptance-and-rollout.md) for subsequent engineering
decisions. Neither those decisions nor this note grant provider processing permission.

## Finding

The public OAuth documentation explains identity verification and Data API access, including an internal-support role for retrieving official results. Its introduction explicitly subjects OAuth, SDK, Data API and resulting data use to the underlying terms. No reviewed public source supplies a distinct license for ApexRacers' hosted archive, cross-member analytics or aggregate reuse. This is an unresolved permission question, not proof that every possible analytics design is prohibited. [OAuth introduction][intro], [Client Roles][roles].

Removing competitor names and Customer IDs addresses a disclosure requirement. It cannot, by itself, establish collection, retention, derivative-use or commercial rights. The notice separates technical credentials from authorization for otherwise prohibited activities. [Third-party notice][notice].

## Four separate questions

| Concept | First-party evidence | Consequence for the plan |
| --- | --- | --- |
| Technical access | The Data API workflow uses an authenticated member's tokens, a registered `data-server` audience and secure token storage. `iracing.auth` enables service access. [Data API workflow][data], [Scopes][scopes]. | A successful request demonstrates technical access, not an archive or redistribution license. |
| Identity proof | The Identity Verification Workflow requests `iracing.profile`, obtains the member's Customer ID/name from `/iracing/profile`, and discards the access token. The profile endpoint publishes its response fields. [Verification workflow][verification], [Profile endpoint][profile]. | Provider-backed ownership proof has a documented route. No manual Customer ID assertion or other-member search is equivalent to this workflow. |
| Disclosure consent | The notice requires the member's explicit, affirmative and revocable consent to expose their display name or Customer ID in any context. [Third-party notice][notice]. | Authentication or access authorization does not demonstrate the separate consent ApexRacers needs for its publication purpose. |
| Processing permission | The EULA restricts collection, copying, derivative and commercial use; OAuth documentation incorporates the underlying terms. [EULA, sections 3, 4.1, 6.1–6.5][eula], [OAuth introduction][intro]. | Resolve permitted purposes and methods independently of disclosure consent. |

The second column describes source content. The third is an engineering interpretation; it does not settle legal applicability.

## Additional OAuth evidence that matters

The book describes End-User Support for member logins and Internal Support for data available to authenticated members, including schedules and official results. It recommends separate client IDs for the roles. This supplies a technical distinction worth preserving; it does not specify bulk retention or redistribution rights. [Client Roles][roles].

Authorization Code is required for broad-user-base clients. Password Limited is a headless flow; requests on behalf of other users must use Authorization Code. The Password Limited chapter says fewer than three pre-registered users, while the token endpoint says up to three on the access list. Record that inconsistency for any affected configuration rather than choosing a number silently. [Authorization Code Flow][code], [Password Limited Flow][password], [Token endpoint][token].

Access tokens are not identity tokens. The provider cautions that their payload claims may change and directs clients to the dedicated profile endpoint for identity information. Token expiry is a technical access lifetime, not a cached-result retention period. [Access Token][access].

As checked on 2026-10-01, Client Registration still states that new client IDs are paused pending evaluation of third-party usage; reopening is to be announced in forums and release notes. The support article retains its March 23, 2026 modification date and the same pause notice. This is the published status of those sources at review time, not proof of every account's eligibility or a reopening forecast. [Client Registration][registration], [OAuth Client Credentials][credentials].

## What remains unresolved

| Topic | Explicit provision or limitation | Precise clarification needed; decision owner |
| --- | --- | --- |
| Hosted collection and internal Customer IDs | EULA 6.1 restricts copying and collection/mining; 6.5 limits connection methods. No API-specific storage exception was found. [EULA][eula]. | Which documented permission covers automated official-result ingestion and internal member identifiers for deduplication, joins and personal analytics? Maintainer to obtain provider clarification; qualified legal reviewer to assess applicability. |
| Caching and archive retention | Website Conditions allow page caching but restrict other downloads, gathering and derivative/commercial use. No third-party API cache duration was identified. [Website Conditions, page 1][website]. | Are shared server caches and historical result archives permitted; for what purpose, fields, duration and deletion/backup conditions? Provider clarification, then maintainer retention policy. |
| Anonymous aggregates and derived metrics | EULA 4.1 includes service-generated statistics; 6.2 reserves derivative works absent prior written consent. [EULA][eula]. | Which aggregate computations and retained outputs are permitted, including contributions from non-consenting or withdrawn members? Provider/legal assessment before architecture acceptance. |
| Commercial model | EULA 6.3 requires express written consent for commercial exploitation; 6.1 addresses making data available for value. [EULA][eula]. | Does the intended funding model require a separate written grant, and what rights would it cover? Maintainer defines the model; provider/legal reviewer resolves permission. |
| Consent scope and withdrawal | Notice specifies the consent properties but no record schema, withdrawal deadline or private-view exception. [Notice][notice]. | Which displays are covered, what evidence proves ownership/affirmative choice, and how must already-served pages, caches and historical output respond to withdrawal? Maintainer/product decision with legal review; provider clarification where interpretation remains material. |
| Re-identification | No reviewed source establishes that aliases or exact race context are anonymous. | What identifiers and contextual combinations remain linkable, and what presentation constraints are acceptable? Privacy/security analysis and product decision; do not claim anonymity from removing two fields alone. |

The Privacy Policy describes iRacing's purpose-based retention and qualified member rights, including erasure. It does not set ApexRacers' retention period or grant third-party processing rights. Withdrawal of identity-display consent and deletion of underlying contributions therefore remain distinct questions. [Privacy Policy, sections 8, 11–12][privacy].

The next planning step can choose a conditional product envelope and list the required evidence. It should not label an ungranted use permitted, infer a blanket prohibition from missing answers, or make architecture acceptance depend on a guessed legal rule.

## Review boundary

Reviewed the notice, legal documents already cited in the audit and public OAuth book, including registration, workflows, scopes, profiles, tokens and client roles. The legal landing page remained accessible. Direct HTTPS retrieved the support notices when the web reader could not. The support-linked [Data API forum announcement][forum-data] and [registration forum thread][forum-registration] redirected direct retrieval to the OAuth login application; their discussion contents were not available for this review. No authenticated sources, production data, live Data API calls, account-specific contracts or provider contact were used. No raw provider documents were committed.

The absence findings apply to this reviewed source set. A client-specific contract or inaccessible forum guidance could change the assessment and must be inspected if supplied later.

[intro]: https://oauth.iracing.com/oauth2/book/introduction.html
[roles]: https://oauth.iracing.com/oauth2/book/print.html#client-roles
[verification]: https://oauth.iracing.com/oauth2/book/print.html#identity-verification-workflow
[code]: https://oauth.iracing.com/oauth2/book/print.html#authorization-code-flow
[data]: https://oauth.iracing.com/oauth2/book/data_api_workflow.html
[scopes]: https://oauth.iracing.com/oauth2/book/scopes.html
[profile]: https://oauth.iracing.com/oauth2/book/iracing_profile_endpoint.html
[password]: https://oauth.iracing.com/oauth2/book/password_limited_flow.html
[token]: https://oauth.iracing.com/oauth2/book/token_endpoint.html
[access]: https://oauth.iracing.com/oauth2/book/access_token.html
[registration]: https://oauth.iracing.com/oauth2/book/client_registration.html
[credentials]: https://support.iracing.com/support/solutions/articles/31000177790-oauth-client-credentials
[notice]: https://support.iracing.com/support/solutions/articles/31000179757-third-party-development-please-review-the-iracing-terms
[eula]: https://ir-core-sites.iracing.com/members/pdfs/20260805-iRacing_Terms_of_Use_and_EULA_dated_Aug_05_2026.pdf
[website]: https://ir-core-sites.iracing.com/members/pdfs/20250702-iRacing_Website_Conditions_of_use_dated_July_02_2025.pdf
[privacy]: https://ir-core-sites.iracing.com/members/pdfs/20260811-iRacing_Privacy_Policy_dated_Aug_11_2026.pdf
[forum-data]: https://forums.iracing.com/discussion/15068/general-availability-of-data-api/p1
[forum-registration]: https://forums.iracing.com/discussion/93956/oauth-client-id-creation/p1?new=1
