-- Removes all data inserted by seed_gt3_series.sql.
--
-- That script used:
--   Series  Id = 9001   (shared with the new seeder — NOT deleted here)
--   Season  Id = 9001
--   Car Ids     9001 – 9011
--   Driver pools 110001–1270499 (non-overlapping ranges per week/car)
--   Test driver  100100 (explicit personal laps for some cars)
--
-- The new ApexRacers.Seeder reuses Series 9001 under Season 90001 with
-- car IDs 3001–3011, so the Series row is preserved.

BEGIN;

-- 1. Lap times and percentile cache for the old car IDs
DELETE FROM iracing."CarPercentileResults"
WHERE "CarId" BETWEEN 9001 AND 9011;

DELETE FROM iracing."LapTimeEntries"
WHERE "CarId" BETWEEN 9001 AND 9011;

-- 2. Weeks owned by the old season (cascades via FK to LapTimeEntries above,
--    but we already deleted those; belt-and-suspenders for safety)
DELETE FROM iracing."LapTimeEntries"
WHERE "WeekId" IN (SELECT "Id" FROM iracing."Weeks" WHERE "SeasonId" = 9001);

DELETE FROM iracing."CarPercentileResults"
WHERE "WeekId" IN (SELECT "Id" FROM iracing."Weeks" WHERE "SeasonId" = 9001);

DELETE FROM iracing."Weeks"      WHERE "SeasonId" = 9001;
DELETE FROM iracing."SeasonCars" WHERE "SeasonId" = 9001;
DELETE FROM iracing."Seasons"    WHERE "Id"       = 9001;

-- 3. Old car records (not referenced by any remaining data after the deletes above)
DELETE FROM iracing."Cars" WHERE "Id" BETWEEN 9001 AND 9011;

-- Series 9001 is intentionally preserved — the new seeder owns it.

COMMIT;
