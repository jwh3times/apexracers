# Drivers are referenced by iRacing Customer ID, not modelled as a local entity

ApexRacers stores race results, rivals, personal laps and percentile results against a bare
iRacing Customer ID rather than a foreign key to a local `Driver` table, and no such table exists.
iRacing owns driver identity: we cannot enumerate its driver population, we receive no change feed
for renames or account closures, and the overwhelming majority of Drivers we store data about will
never hold an ApexRacers account. A local mirror would therefore be permanently partial and
permanently stale, so a Driver is named by their Customer ID and nothing else.

## Considered options

A local `Driver` entity synced from iRacing, with every result table carrying a foreign key to it.
This is the conventional relational shape and is what a reader will expect, which is why the
rejection is recorded here. It was rejected because rows could only be created lazily on first
sighting, leaving a table that answers "which drivers have we happened to see?" rather than "who
races on iRacing" — the referential integrity would be cosmetic, and keeping display names fresh
would need a reconciliation job with no upstream signal to drive it.

## Consequences

- A Driver's display name is a **snapshot** wherever it is stored (`Rival.DisplayName`,
  `SubsessionResult`), not a lookup, and can drift from the driver's current iRacing name.
- There is no database-level guarantee that a Customer ID appearing in results corresponds to a real
  driver; validity comes from the ingestion source, not from a constraint.
- A User's Claimed Identity is a nullable `long` on the user row rather than a relationship, so the
  one-Driver-per-User rule is enforced by a filtered unique index rather than by the schema shape.
- Deleting a User leaves the Driver's ingested race data untouched, which is correct — that data
  belongs to the Driver, not to the account that happened to claim them.
