-- Removes ALL data from the iracing schema so the database can be re-seeded
-- from scratch. Identity tables, UploadedLaps, and FeatureFlags are preserved.
--
-- WARNING: CarPercentileResults is also erased. This table is written by both
-- the seeder and by live API calls (CarRecommendationService, PercentileCalculationService).
-- Running this script against a database with real users will destroy their
-- computed percentile history. Only run on dev/local databases.
--
-- Run via Docker:
--   Get-Content src\ApexRacers.Data\Seeds\truncate_seed_data.sql |
--     docker compose exec -T postgres psql -U apexracers -d apexracers
--
-- Run against a direct connection:
--   psql $DATABASE_CONNECTION_STRING -f src/ApexRacers.Data/Seeds/truncate_seed_data.sql

BEGIN;

-- ── 1. Derived / computed data ────────────────────────────────────────────────
DELETE FROM iracing."CarPercentileResults";

-- ── 2. Race results and subsessions ──────────────────────────────────────────
-- SubsessionResults cascade automatically when Subsessions are deleted.
DELETE FROM iracing."Subsessions";

-- ── 3. Schedule data ──────────────────────────────────────────────────────────
DELETE FROM iracing."Weeks";
DELETE FROM iracing."SeasonCars";
DELETE FROM iracing."SeasonCarClasses";

-- ── 4. Seasons and series ─────────────────────────────────────────────────────
DELETE FROM iracing."Seasons";
DELETE FROM iracing."Series";

-- Cars, Tracks, CarClasses, and CarClassCars are intentionally kept:
-- the seeder re-adds them idempotently, and Cars/Tracks are referenced
-- by UploadedLaps which must be preserved.

COMMIT;
