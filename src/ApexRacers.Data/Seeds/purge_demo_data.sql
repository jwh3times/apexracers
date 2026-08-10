-- Surgically removes the SYNTHETIC demo data (negative-id subsessions + their results,
-- computed percentile snapshots, demo ExternalDataCache rows, and synthetic BoP/weather)
-- while PRESERVING catalog reference data (Series/Seasons/Weeks/Cars/Tracks/CarClasses)
-- and all real user-owned data.
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

-- SQL mirror of DemoData.CacheSentinelThreshold (exposed as DemoCache.SentinelThreshold).
-- The >= range operator, UTC type/value, and threshold are contract-load-bearing: demo rows use
-- a later Sentinel value, while real cache TTLs cannot reach this range.
DELETE FROM iracing."ExternalDataCaches"
WHERE "ExpiresAt" >= TIMESTAMPTZ '9000-01-01 00:00:00+00';

-- Synthetic BoP + per-week weather for active seasons (real ingestion re-fills these idempotently).
DELETE FROM iracing."SeasonCarBops"
 WHERE "SeasonId" IN (SELECT "Id" FROM iracing."Seasons" WHERE "Active" = true);
UPDATE iracing."Weeks" SET "WeatherSummaryJson" = NULL
 WHERE "SeasonId" IN (SELECT "Id" FROM iracing."Seasons" WHERE "Active" = true);

COMMIT;
