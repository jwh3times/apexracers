-- Removes ALL synthetic seed data from the database so it can be re-seeded
-- from scratch. This covers both the legacy seed_gt3_series.sql data and
-- everything written by ApexRacers.Seeder.
--
-- What is removed:
--   Old GT3 script  — Series/Season 9001, Cars 9001–9011, driver pools 110001+
--   New seeder      — Series 9001–9007, Seasons 90001–90007, Cars 3001–7002,
--                     drivers 100001–100200
--
-- What is preserved:
--   AspNet Identity tables (users, roles, etc.)
--   PersonalLaps uploaded by real users
--   Any data with IDs outside the synthetic ranges above
--
-- Run via Docker:
--   Get-Content src\ApexRacers.Data\Seeds\truncate_seed_data.sql |
--     docker compose exec -T postgres psql -U apexracers -d apexracers
--
-- Run against a direct connection:
--   psql $DATABASE_CONNECTION_STRING -f src/ApexRacers.Data/Seeds/truncate_seed_data.sql

BEGIN;

-- ── 1. Derived data ───────────────────────────────────────────────────────────

DELETE FROM iracing."CarPercentileResults"
WHERE "CarId" BETWEEN 3001 AND 7002    -- new seeder cars
   OR "CarId" BETWEEN 9001 AND 9011;   -- old GT3 script cars

-- ── 2. Lap time entries ───────────────────────────────────────────────────────

-- By synthetic car IDs (covers all weeks across all synthetic seasons)
DELETE FROM iracing."LapTimeEntries"
WHERE "CarId" BETWEEN 3001 AND 7002
   OR "CarId" BETWEEN 9001 AND 9011;

-- Belt-and-suspenders: by seeded driver pool (new seeder)
DELETE FROM iracing."LapTimeEntries"
WHERE "DriverCustomerId" BETWEEN 100001 AND 100200;

-- By old season's weeks (catches any driver IDs from the legacy script)
DELETE FROM iracing."LapTimeEntries"
WHERE "WeekId" IN (SELECT "Id" FROM iracing."Weeks" WHERE "SeasonId" = 9001);

-- ── 3. Weeks and season-car mappings ─────────────────────────────────────────

DELETE FROM iracing."CarPercentileResults"
WHERE "WeekId" IN (
    SELECT "Id" FROM iracing."Weeks"
    WHERE "SeasonId" IN (9001, 90001, 90002, 90003, 90004, 90005, 90006, 90007)
);

DELETE FROM iracing."Weeks"
WHERE "SeasonId" IN (9001, 90001, 90002, 90003, 90004, 90005, 90006, 90007);

DELETE FROM iracing."SeasonCars"
WHERE "SeasonId" IN (9001, 90001, 90002, 90003, 90004, 90005, 90006, 90007);

-- ── 4. Seasons and cars ───────────────────────────────────────────────────────

DELETE FROM iracing."Seasons"
WHERE "Id" IN (9001, 90001, 90002, 90003, 90004, 90005, 90006, 90007);

DELETE FROM iracing."Cars"
WHERE "Id" BETWEEN 3001 AND 7002
   OR "Id" BETWEEN 9001 AND 9011;

-- ── 5. Series ─────────────────────────────────────────────────────────────────

DELETE FROM iracing."Series"
WHERE "Id" BETWEEN 9001 AND 9007;

COMMIT;
