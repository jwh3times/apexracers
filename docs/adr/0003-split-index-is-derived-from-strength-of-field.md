# Split Index is derived from Strength of Field, and an unknown Split Index is not index 0

A Subsession's Split Index — its position among the Splits of its Race Session — is computed at
ingest by ranking the Splits iRacing reports on their own `event_strength_of_field`, descending, with
equal Strength of Field broken by ascending subsession identifier. It is stored as a nullable value,
and null means the position is unknown: iRacing named no Splits, or named a set the Subsession being
ingested was absent from. Nothing may read null as "the strongest Split".

## Considered options

**Trusting the order of iRacing's `session_splits` array.** This is what ApexRacers did first, and it
reads as the obvious interpretation, which is why the rejection is recorded here. It was rejected
because no part of iRacing's contract states that the array arrives sorted by Strength of Field. The
only captured payload is consistent with sorted order — two Splits, 2509 before 1266 — but a single
two-element sample cannot distinguish "sorted" from "coincidence," and one out-of-order payload would
silently label a weak Split the strongest. The array's elements each carry their own Strength of
Field, so ranking on the value costs nothing and removes the assumption entirely rather than
deferring it to a payload that may never arrive.

**Keeping a non-nullable index and letting 0 mean "unknown too."** Rejected because index 0 is the
one value that already carries a strong claim — it is the top Split, the strongest field a Driver
could have been sorted into. Overloading it with two failure cases makes every reading of the value
unfalsifiable, and the overload is invisible at the point of use.

## Consequences

- Split Index and Split Count are established together from one payload, so a Split's position is
  never half-known. Either both are present or both are null.
- A Subsession absent from its own `session_splits` is stored with an unknown position and logged as
  a payload that disagrees with itself, rather than being quietly recorded as the top Split.
- Subsessions ingested before this decision have no recoverable Split Index — their stored `0`s
  conflated the three cases — so the migration drops the old column rather than carrying values
  across, and those rows read as unknown until they are re-ingested.
- Splits of one Race Session still cannot be queried together, because iRacing's session identity is
  not persisted. The Split Index is derived from the payload of each Subsession individually, and
  ApexRacers cannot reconstruct the sibling set from its own database.
- The index remains zero-based everywhere it is stored or sent. The one-based Split Number exists
  only in display labels, mirroring how Race Week Index and Race Week Number are separated.
