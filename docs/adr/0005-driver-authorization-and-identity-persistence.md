# Driver authorization owns participating identities and authorized names

Accepted target foundations on 2026-10-02 in [#361](https://github.com/jwh3times/apexracers/issues/361).
Supersedes [ADR 0001](0001-drivers-referenced-by-customer-id.md) as a target decision. Current
implementation still has claims and historical name snapshots; this record does not assert that
proof, consent, name minimization or copy enforcement has shipped.

Driver Authorization owns proof, claim conflicts, separate consent scopes, lifecycle invalidation,
and names retained for current verified personal/sharing purposes. Its participating User/Driver
associations are distinct from official racing evidence. This avoids distributing authorization
and name-copy rules across ingestion, cache callbacks, followers, and response callers.

## Preserved decisions and changed consequences

- Keep private Customer IDs for official Field deduplication, analytics, and authorized joins.
  Do not require a foreign key from every Race Result to a local Driver population mirror.
- An authorization record represents participation/proof/consent, not every Driver encountered
  upstream. One User per Driver and explicit verified/unverified conflict handling remain owned
  by Driver Authorization; no silent account merge, upload transfer or consent reuse is allowed.
- Remove Race Result and Rival name snapshots. Keep authorized names separately, with current
  purpose checks and tracked physical cleanup. A saved follow supplies no publication permission.
- User deletion erases personal-owned data under its policy while independently justified official
  racing evidence follows its separate continuing purpose. Neither bare identifiers nor copied
  names authorize disclosure.
- Explicit synthetic/live provenance applies to associations, stored evidence and caches.

## Considered alternatives

A complete local Driver mirror remains rejected for ADR 0001's reasons: it would be partial and
stale. Keeping snapshot names plus response-local checks was also rejected because it leaves copy
management, withdrawal and restore knowledge scattered among callers.

The [decision brief](../design/driver-disclosure-ownership.md) owns the accepted interfaces and
mechanisms, including journal-first lifecycle recovery and the day-90 personal-data reactivation
cutoff. No schema, registered-client wire contract, provider permission or production setting
is established by this record.
