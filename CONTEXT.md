# ApexRacers Domain Language

ApexRacers helps iRacing drivers understand their pace and competitiveness within recurring racing series. This glossary defines the racing concepts shared across the product.

## Series Calendar

**Series**:
A recurring iRacing competition whose identity continues across multiple seasons.
_Avoid_: Season

**Season**:
A time-bound edition of a Series, identified by its iRacing season identity and year-and-quarter designation.
_Avoid_: Series, championship

**Active Season**:
A Season that iRacing currently designates as active. This is a status and does not imply that only one Active Season exists for a Series.
_Avoid_: Current Season

**Current Season**:
The Season for a Series whose first Race Week began most recently. It remains current until a later Season's first Race Week begins, regardless of Active Season status.
_Avoid_: Active Season, latest Season

**Upcoming Season**:
A Season whose first Race Week has not begun. An Upcoming Season does not replace the Current Season merely because it is active or has a newer year-and-quarter designation.
_Avoid_: Current Season, next Current Season

**Race Week**:
A scheduled round within a Season, associated with a start date and a Track.
_Avoid_: Calendar Week, Schedule Week

**Race Week Index**:
The zero-based position iRacing assigns to a Race Week within its Season; the first Race Week has index 0.
_Avoid_: Week Number

**Race Week Number**:
The one-based ordinal shown to drivers for a Race Week; Race Week 1 has Race Week Index 0.
_Avoid_: Race Week Index

**Current Race Week**:
The Race Week in the Current Season with the latest start date that has arrived; when multiple Race Weeks share that date, the one with the highest Race Week Index is current. The final Race Week remains current through the inter-season gap and is replaced when the later Season's first Race Week begins.
_Avoid_: Latest Week, Active Week

## Tracks

iRacing identifies what is driven, not where it is. Its track identifier addresses one layout at one facility as currently scanned, and several of those layouts share a single display name — so the name is the one thing that must never stand in for the identity.

**Venue**:
A physical racing facility, which several Tracks may share. iRacing identifies no Venue — ApexRacers sees one only as the name its Tracks have in common, so a Venue never establishes that two laps are comparable.
_Avoid_: Track, Circuit, Location, Facility

**Track**:
One drivable configuration at a Venue, addressed by its iRacing track identifier — the only thing that makes lap times comparable. A rebuilt or rescanned Track receives a new identifier and is a different Track.
_Avoid_: Circuit, Layout, Course, Track Configuration

**Track Name**:
The display name a Track carries. It is shared with every other Track at the same Venue, so it names the Venue rather than the Track and is never an identity.
_Avoid_: Track, Venue name, Circuit name

**Configuration Name**:
The label distinguishing a Track from the others at its Venue. It is frequently absent, which costs no identity, since a Track is addressed by its identifier rather than by its labels.
_Avoid_: Config, Layout name, Variant

**Retired Track**:
A Track iRacing no longer schedules. It keeps its identifier and everything driven at it, and its labels are prefixed to say so.
_Avoid_: Inactive track, Removed track, Legacy track

## Race Sessions

A Race Week schedules Race Sessions; each Race Session divides into one or more Splits; each Split is exactly one Subsession; and each Subsession runs one or more Sim Sessions in sequence. **Session** on its own is never ApexRacers language — it names three different levels of that hierarchy. **Event** is likewise avoided: it is iRacing's word for two of them at once.

**Race Session**:
One scheduled timeslot within a Race Week that Drivers register for, identified by iRacing's session identity. It divides into one or more Splits.
_Avoid_: Session, Event, Race, Timeslot

**Split**:
One division of a Race Session, grouping Drivers of comparable Strength of Field so they race each other rather than the whole entry list. A Race Session always has at least one Split, even when it was never divided.
_Avoid_: Session, Heat, Tier, Bracket

**Split Index**:
The zero-based position of a Split within its Race Session, ordered by Strength of Field descending; Split Index 0 is the strongest Split. There is no one-based counterpart, because Splits are not shown to Drivers.
_Avoid_: Split Number, Split Num

**Subsession**:
The running of one Split and the results it produced, addressed by its iRacing subsession identity. Exactly one Subsession per Split — a Subsession never contains Splits.
_Avoid_: Session, Event, Split

**Event Type**:
What a Subsession was held for — practice, qualifying, time trial, or race. It describes the whole Subsession, not any one segment of it.
_Avoid_: Session type, Sim Session Type, Category

**Sim Session**:
One timed segment run in sequence within a Subsession, addressed by its simulation session number. The race segment is number 0; segments preceding it count down from -1.
_Avoid_: Session, Subsession, Stint, Segment

**Sim Session Type**:
What a Sim Session is — practice, qualifying, or racing. A lap recorded from uploaded telemetry carries a Sim Session Type without belonging to any Subsession.
_Avoid_: Event Type, Session type, Lap type

**Race**:
A Subsession whose Event Type is a race, and the word for one in URLs and driver-facing copy. Every Race is a Subsession; not every Subsession is a Race.
_Avoid_: Event, Session, Subsession, Heat

**Official Subsession**:
A Subsession run under iRacing's own series scheduling, counting toward a Driver's iRating, Safety Rating, and championship points. A hosted or league Subsession carries the same identity shape but is not official.
_Avoid_: Ranked session, Sanctioned session, Public session

**Race Result**:
One Driver's classified finishing record in a Race. A Race Result names exactly one Driver, so a team entry — which races under no Customer ID — produces none.
_Avoid_: Result, Finish, Classification, Standing

**Strength of Field**:
iRacing's measure of the average skill of the Drivers entered in a Subsession. It is what Splits are ordered by.
_Avoid_: SOF, Field strength, Field rating

## Identity

ApexRacers accounts and iRacing racing identities are separate populations that mostly do not overlap. **Member** and **Customer** are iRacing's own words and are not ApexRacers language; they survive only where a name mirrors iRacing's API directly.

**User**:
Someone who holds an ApexRacers account. A User need not have any iRacing racing identity.
_Avoid_: Member, Customer, Account, Driver

**Driver**:
Someone with an iRacing racing identity, addressed by their Customer ID. Drivers exist independently of ApexRacers, and most are not Users.
_Avoid_: Member, Customer, Racer, Competitor

**Customer ID**:
iRacing's numeric identifier for a Driver. It is the only identifier by which a Driver can be named.
_Avoid_: Member ID, Driver ID, User ID

**Claimed Identity**:
The Driver a User asserts is them. A Driver may be claimed by at most one User, and a User may claim at most one Driver. A claim is asserted rather than proven.
_Avoid_: Linked account, Connected account, Verified identity

**Verified Identity**:
A Claimed Identity whose ownership has been proven by the Driver signing in to iRacing. No identity is verified today.
_Avoid_: Confirmed identity, Authenticated driver, Validated identity

**Subject Driver**:
The Driver whose data a page or calculation represents. Frequently, but not necessarily, the requesting User's Claimed Identity — any Driver may be the Subject Driver of a lookup.
_Avoid_: Current driver, Target user, Member, Requesting user

**Demo Driver**:
A synthetic Driver resolved as the Subject Driver for Users viewing the demo surface. It stands in for a Claimed Identity without becoming one, and takes precedence over a User's real claim.
_Avoid_: Demo user, Impersonated driver, Fake member, Linked demo account

**Rival**:
A Driver a User follows for head-to-head comparison against their own Subject Driver. A Rival need not be a User.
_Avoid_: Friend, Opponent, Competitor, Followed member

## Lap Evidence

A Lap reaches ApexRacers as one of two kinds of evidence — a Race Lap from iRacing's results, or an Uploaded Lap from a User's telemetry — and the two are never interchangeable. Any best drawn from them names the evidence it came from. **Personal Lap** is not ApexRacers language: it reads as a lap that is a personal best, which is not what it names. **Best Lap** is likewise avoided — it says nothing about whose evidence produced it or over what scope it was chosen.

**Lap**:
One completed circuit of a Track by a Driver in one Car. ApexRacers knows a Lap only through the evidence that recorded it.
_Avoid_: Tour, Run, Circuit

**Timed Lap**:
A Lap that produced a lap time. A Lap without one — an out-lap, an unfinished lap, a lap the sim did not time — can never be anyone's best.
_Avoid_: Valid lap, Complete lap, Scored lap

**Clean Lap**:
A Timed Lap driven without an incident. Pace is summarized over Clean Laps; best-lap selection is not, so a best lap may carry an incident.
_Avoid_: Green lap, Valid lap, Incident-free lap

**Race Lap**:
A Lap driven in the racing Sim Session of a Subsession, known to ApexRacers through iRacing's results and attributed to a Driver.
_Avoid_: Official lap, Session lap, Result lap

**Uploaded Lap**:
A Lap ApexRacers knows only because a User submitted the telemetry that recorded it. It is owned by that User; the Driver who drove it is claimed by the file rather than established.
_Avoid_: Personal Lap, Telemetry lap, Practice lap

**Telemetry Upload**:
One telemetry file a User submits, carrying the Laps of a single Sim Session and the conditions they were driven in.
_Avoid_: Session, Telemetry session, Ibt

**Pace**:
How quickly a Driver laps over a run, summarized from their Clean Laps. Pace describes a run; a best lap describes one Lap.
_Avoid_: Speed, Performance, Lap time

**Race Best**:
The fastest Race Lap a Driver set in one Car during one Race Week, across every Subsession they entered.
_Avoid_: Personal Best, Best Lap, Driver Best

**Uploaded Best**:
The fastest Uploaded Lap a User holds for one Car at one Track, across all their Telemetry Uploads.
_Avoid_: Personal Best, Telemetry best, Your best lap

**Personal Best**:
The fastest Lap known for a Subject Driver in one Car at one Track, drawn from whichever evidence the User has allowed to count. It is the lap ranked against a field.
_Avoid_: Best Lap, Driver Best, PB, Reference lap
