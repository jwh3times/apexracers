# Track identity follows iRacing's track identifier, so a rebuilt track is a different Track

A Track in ApexRacers is exactly what iRacing's track identifier addresses: one drivable
configuration at a venue, as currently scanned. When iRacing rebuilds or rescans a layout it issues a
**new** identifier and marks the old one retired rather than updating it in place — Richmond Raceway
is track `31` (`[Retired] Richmond - 2007`) and track `561` (the 2025 rebuild) at the same 0.75 miles,
and Barber Motorsports Park carries three retired configurations plus a 2026 rescan. ApexRacers keeps
those apart. Lap times are only ever compared within one identifier, so nothing is ranked against a
time set on different geometry.

## Considered options

Merging rescan generations of the same layout, so that a driver's history at a venue survives an
iRacing rebuild. This is what a reader will expect, since the split is visible and looks like an
oversight — which is why the rejection is recorded here. It was rejected because iRacing publishes no
field asserting that two identifiers are the same layout: the merge would have to be inferred from
package, length, and name, and each of those is unreliable. Length is equal across the Richmond pair
but is also equal across genuinely different layouts; names change across a rebuild
(`[Retired] Barber Motorsports Park` and `Barber Motorsports Park` live in one package); and a package
holds unrelated configurations anyway. A wrong merge is worse than a visible split, because it
silently compares laps driven on different surfaces.

## Consequences

- A driver's history at a venue splits when iRacing rebuilds a layout, and ApexRacers presents that
  split rather than hiding it. There is no relationship recorded between the old Track and its
  replacement.
- A Track's display name is shared with every other Track at its venue — 95 of the 463 known
  identifiers share a name with at least one other — so grouping or joining on the name silently
  merges unrelated layouts. Homestead Miami Speedway is one name over a 1.5-mile oval, two road
  courses, and an open-wheel oval.
- Venue is not a stored concept. Nothing addresses a venue, and any venue-level grouping is inferred
  from the shared name at the point of use.
- The configuration label is absent for many Tracks and carries no identity, so it can be shown or
  omitted freely without affecting what a lap is comparable to.
