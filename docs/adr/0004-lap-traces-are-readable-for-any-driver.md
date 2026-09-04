# Per-lap pace traces are readable for any Driver, because official lap data is already public

`GET /api/subsessions/{id}/laps?customerId=` accepts any caller-supplied Customer ID and passes it to
`LapDataService` with no "is this you, or a rival you follow" check. Any signed-in User can read the
per-lap trace of any Driver in any Subsession. This is intended behavior, not an oversight, and the
endpoint stays that way unless the product requirement changes.

The decision is recorded here because the shape looks exactly like an access-control defect. A
security review flagged it as an IDOR candidate, and a reader arriving at `SubsessionController`
cold will reach for the same conclusion. Without this record the endpoint invites a "fix" that would
quietly remove a working product capability.

## Why it is not a data-exposure problem

The data is iRacing's, published for official sessions, and already readable by any iRacing member
through iRacing's own surfaces. ApexRacers re-presents public race results; it does not hold private
records behind this route. Nothing app-private is reachable through it — Uploaded Laps and rivals are
keyed on the User, not on a Customer ID supplied in a query string.

The capability is also load-bearing. Comparing your pace against a rival's on the same Subsession is
the point of the head-to-head surface, and that requires reading a Driver who is not the caller.

## Considered options

**Restricting `customerId` to the caller's own Claimed Identity.** Rejected because it would break
head-to-head comparison, which exists to read another Driver's laps. It would also protect nothing:
the same lap data stays available from iRacing directly.

**Restricting `customerId` to the caller plus their followed rivals.** Rejected as security theater
with a real cost. Following a rival is an unauthenticated-side action with no approval step, so an
attacker bypasses the control by following the target first. Meanwhile a genuine user browsing a
race they were in cannot open the trace of a Driver they have not followed.

## Consequences

- The endpoint keeps `[Authorize]` — this decision is about *which Driver* may be named, not about
  anonymous access. Rate limiting still applies.
- Reclassify only if the product requirement changes to self-or-rival-only traces. If that happens,
  this ADR is superseded rather than silently contradicted.
- Any future security review that rediscovers this route should land here rather than reopening it.
  It was assessed on 2026-06-23, rechecked on 2026-08-29, and accepted both times.
