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
A scheduled round within a Season, associated with a start date and track configuration.
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
