-- Surgically removes the SYNTHETIC demo data (negative-id subsessions + their results,
-- and computed percentile snapshots) while PRESERVING catalog reference data
-- (Series/Seasons/Weeks/Cars/Tracks/CarClasses) and all real user-owned data.
--
-- Safe to run against production as part of the M2 "real creds on" runbook, in this order:
--   1. In Admin -> Feature Flags, set iracing-demo IsEnabled=false.
--   2. Run THIS script.
--   3. Only then enable iracing-live with real credentials.
-- Truncating CarPercentileResults is safe ONLY because demo teardown happens before any
-- real ingestion exists -- at teardown time every percentile row is demo-derived.
--
-- Run via Docker:
--   Get-Content src\ApexRacers.Data\Seeds\purge_demo_data.sql |
--     docker compose exec -T postgres psql -U apexracers -d apexracers
--
-- Run against a direct connection:
--   psql $DATABASE_CONNECTION_STRING -f src/ApexRacers.Data/Seeds/purge_demo_data.sql

BEGIN;

-- Synthetic subsessions use negative ids; real ingested subsessions are positive.
-- SubsessionResults cascade automatically when Subsessions are deleted.
DELETE FROM iracing."Subsessions" WHERE "Id" < 0;

-- Computed percentile snapshots (demo-derived at teardown time -- see header).
DELETE FROM iracing."CarPercentileResults";

-- -- Extended by Plan 2 (cache-seeding) -----------------------------------------
-- DELETE FROM iracing."ExternalDataCaches" WHERE "ExpiresAt" >= '9000-01-01';
-- DELETE FROM iracing."SeasonCarBop" WHERE <seeded seasons>;
-- UPDATE iracing."Weeks" SET "WeatherSummaryJson" = NULL WHERE <seeded seasons>;

COMMIT;
