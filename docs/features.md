# ApexRacers Features

ApexRacers helps iRacing drivers understand where they are competitive by combining
weekly-series data, personal telemetry, and synthetic/demo data for development.

## Core Workflows

- **Series and week browsing:** view active series, current race weeks, eligible cars,
  track context, and aggregate lap-time statistics.
- **Percentile calculation:** compare a driver's lap time against the field for a
  series, week, and car, with cached results for repeat visits.
- **Car recommendations:** rank cars by where the signed-in driver appears most
  competitive.
- **Strategy briefings:** summarize track, pit, weather, BoP, and personal context for
  a race week.
- **Telemetry upload:** parse `.ibt` files, store the laps they recorded, and surface the
  best of them by car and track.
- **Driver analytics:** show percentile history, progression, recent races, profile
  stats, achievements, and head-to-head comparison surfaces when iRacing data is
  available.
- **Catalog exploration:** browse cars and tracks, including personal-best overlays
  when the user has telemetry data.
- **Admin controls:** manage user roles and feature flags through an Admin-only panel.

## Access Model

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
