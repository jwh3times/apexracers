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
