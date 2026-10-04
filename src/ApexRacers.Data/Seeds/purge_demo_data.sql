-- Disable iracing-demo and stop seed processes before running this script.
-- Purges only explicitly synthetic copies; identifiers and expiry dates never classify origin.
-- DataProvenance.Demo = 2 is the owned Core contract used by these namespace predicates.
-- Unknown legacy copies remain quarantined/unavailable and require separate reconciliation.
-- Real evidence, Real weather, reference catalogs, accounts and Uploaded Laps are preserved.
-- Run verify-teardown before any Real acquisition. Recovery retains these schema fences.
BEGIN;
LOCK TABLE iracing."FeatureFlags", iracing."MappedDataCaches", iracing."RaceEvidenceSubsessions",
    iracing."RaceEvidenceResults", iracing."ScopedCarPercentileResults", iracing."ScopedRivals",
    iracing."ScopedSeasonCarBops", iracing."Weeks" IN ACCESS EXCLUSIVE MODE;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM iracing."FeatureFlags" WHERE "Key" = 'iracing-demo' AND "IsEnabled") THEN
        RAISE EXCEPTION 'Disable iracing-demo before teardown';
    END IF;
END $$;
DELETE FROM iracing."RaceEvidenceResults" WHERE "Provenance" = 2;
DELETE FROM iracing."RaceEvidenceSubsessions" WHERE "Provenance" = 2;
DELETE FROM iracing."ScopedCarPercentileResults" WHERE "Provenance" = 2;
DELETE FROM iracing."ScopedRivals" WHERE "Provenance" = 2;
DELETE FROM iracing."MappedDataCaches" WHERE "Provenance" = 2;
DELETE FROM iracing."ScopedSeasonCarBops" WHERE "Provenance" = 2;
UPDATE iracing."Weeks" SET "DemoWeatherSummaryJson" = NULL WHERE "DemoWeatherSummaryJson" IS NOT NULL;
COMMIT;
