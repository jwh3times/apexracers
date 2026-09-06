# ApexRacers Features

ApexRacers helps iRacing drivers understand where they are competitive by combining
weekly-series data, personal telemetry, and synthetic/demo data for development.

## Core Workflows

- **Series and week browsing:** view active series, current race weeks, eligible cars,
  track context, and aggregate lap-time statistics.
- **Percentile calculation:** compare a driver's lap time against the field for a
  series, week, and car, with cached results for repeat visits. Personal Best surfaces
  consistently use official race laps by default and let the driver opt in uploaded laps
  with an optional session-type filter; that choice follows them between related pages.
  Each Personal Best is shown with the evidence that produced it, so a lap set in an
  official race can be told from one drawn from uploaded telemetry. An uploaded lap only
  counts toward that week's Personal Best if it was driven during that race week; a
  faster uploaded lap from outside the week is called out separately rather than
  silently dropped. Percentile and placement headlines appear only once five drivers
  have set a time; smaller fields report their driver count without presenting a
  coarse position as a meaningful competitiveness result.
- **Car recommendations:** order cars by the lap time the signed-in driver is projected
  to set in each, with that ordering shown as its Recommendation Rank. A car the driver entered that
  Race Week shows its Field Size and, when the field is large enough, the driver's Percentile Rank;
  a projected-only car instead labels the historical Expected Percentile used for its projection
  and claims no place in that Field.
  Undersized readings do not influence expected-percentile history or driver analytics.
- **Strategy briefings:** summarize track, pit, weather, BoP, and personal context for
  a race week.
- **Telemetry upload:** parse `.ibt` files, store the Timed Laps they recorded, and surface each
  driver's Uploaded Best per car and track. A file recorded by a driver other than the one
  linked to the uploading account is refused.
- **Driver analytics:** show percentile history, progression, recent races, profile
  stats, achievements, and head-to-head comparison surfaces when iRacing data is
  available.
- **iRacing standings:** report championship, Time Trial, qualifying, and category-leaderboard
  Standings without conflating those awarded positions with ApexRacers' Recommendation Rank.
- **Catalog exploration:** browse current cars and tracks, including Uploaded Best overlays
  when the user has telemetry data. Retired catalog entries stay available through their direct
  detail URLs so historical laps remain reachable without crowding the default lists.
- **Admin controls:** manage non-Admin user roles and feature flags through an Admin-only panel.
  Admin promotion uses startup seeding; the role-management API cannot grant or remove Admin.

## Access Model

Changing an email address or setting a new Claimed Identity Customer ID requires the current account
password. Ordinary display-name/theme edits and an unchanged Customer ID do not. This confirms access
to the local account; a Claimed Identity still does not prove ownership of an iRacing Driver.

**API clients upgrading to v8.0.0:** include `currentPassword` in the JSON body of
`POST /api/auth/request-email-change` alongside `newEmail`. Include it in
`PUT /api/auth/profile` when supplying an initial or changed non-null `iRacingCustomerId`.
An omitted or null Customer ID preserves the existing claim. Missing or incorrect passwords reject
these sensitive updates before any profile mutation or email delivery; response shapes and the
email confirmation-link step are unchanged. The Settings page supplies the password for both flows.

Successful password changes revoke the account's active refresh tokens on every device, including
the current one. Existing access tokens remain valid until their normal expiry (up to 15 minutes);
devices must then sign in again. A rejected password change leaves sessions unchanged.

The application uses local account authentication with JWT access tokens and rotating
refresh tokens. Users have one role at a time: `Standard`, `Beta`, `Alpha`, or `Admin`.
Feature flags can be enabled for a minimum role tier.

Most account, telemetry, recommendation, and personal analytics views require sign-in.
Selected series, schedule, standings, catalog, strategy, and race-detail views are
public or partially public.

## iRacing Data Modes

Some iRacing-backed features require service credentials that may not be available in
all environments. The app uses feature flags to keep those surfaces hidden or backed by
clearly labeled synthetic demo data until live data is available.

- `iracing-live` reveals the live iRacing-backed surface.
- `iracing-demo` reveals the same surface with synthetic data for preview/testing.

## Architecture At A Glance

- Backend: ASP.NET Core API, EF Core, PostgreSQL, background ingestion worker, and a
  seeder CLI.
- Frontend: Vite, React, TypeScript, Vitest, and Playwright.
- Data: persistent domain entities for queryable shared data, cached mapped DTOs for
  read-mostly iRacing responses, and user-owned data such as telemetry laps and rivals.
