# Driver publication requires current authorization and safe projection

Accepted target foundations and mechanisms on 2026-10-02 in
[#361](https://github.com/jwh3times/apexracers/issues/361). Supersedes
[ADR 0004](0004-lap-traces-are-readable-for-any-driver.md) as a target decision. Existing arbitrary-ID
routes and distributed response projections remain until later implementation; this record does
not assert that restricted access or safe publication has shipped.

Driver Publication owns audience selection, eligible Driver References, complete-cohort projection,
publication safety, and actual response admission/dispatch. Official data accessibility elsewhere,
signed-in status, account membership, following, and a UI feature flag do not supply this permission.

## Target outcomes

- Personal Driver access derives from the verified, personally consenting owner. Other-Driver
  detail requires a currently eligible recipient/purpose-scoped Driver Reference and current
  sharing authorization; generic unavailability must not disclose an ineligible identity/existence.
- Other Users receive only the consented name and permitted racing statistics, without the Driver's
  raw Customer ID, Uploaded Laps or private personal history. Visitors receive approved aggregate
  output and no opted-in names.
- Hidden contributions use the accepted fixed whole-cohort band hierarchy and at least five
  distinct hidden Drivers per displayed joint group, excluding owner/disclosed contributions.
  Emit group summaries without exact counts or Driver-addressing keys. Apply a reviewed catalog
  and combined-release gate; neither coarsening nor aggregate fallback proves anonymity.
- Preserve exact internal Field evidence and owner percentile arithmetic. Safe projection is
  separate from internal computation; additional published fields require assessment.
- Shared mapped caches contain name-free evidence. Names/private payloads remain separately
  scoped, and current checks cover fresh retrieval, warm caches, history, and stale writes.
- Durable admission/drain coordinates publication with withdrawal. Unknown writer status keeps
  acknowledgement pending; lease expiry is not terminal-writer proof. Independently protected
  enforcement and copy reconciliation are prerequisites for reopening restored data.
- Initial visitor/fallback aggregates contain only approved band-group ranges from the same
  assessed artifact. Review/reserve admission atomically against prior, pending and possibly
  dispatched releases, with current authorization/cohort/provenance/catalog dependencies.
- Demo/live provenance and namespaces remain separate, including on cache miss.

## Considered alternatives

Unrestricted raw Customer ID lookups are rejected under the accepted product direction. Restricting
only to followed Drivers is insufficient because following is not permission. Removing names
only in React, using stable aliases, waiting for TTL, or checking consent before an unlocked send
does not implement the accepted current-authorization and publication requirements.

The [decision brief](../design/driver-disclosure-ownership.md) owns the settled detailed decisions
and implementation proof obligations. [#362](https://github.com/jwh3times/apexracers/issues/362)
selects acceptance evidence and
rollout gates. No deployed exposure, anonymity proof, processing permission, implementation or live
activation is established here.
