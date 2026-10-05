-- Disable iracing-demo and stop seed processes before running this script.
-- Purges only explicitly synthetic copies; identifiers and expiry dates never classify origin.
-- DataProvenance.Demo = 2 is the owned Core contract used by these namespace predicates.
-- Unknown legacy copies remain quarantined/unavailable and require separate reconciliation.
-- Real evidence, Real weather, reference catalogs, accounts and Uploaded Laps are preserved.
-- Run verify-teardown before any Real acquisition. Recovery retains these schema fences.
BEGIN;
SELECT pg_advisory_xact_lock(370);
LOCK TABLE iracing."FeatureFlags", iracing."MappedDataCaches", iracing."RaceEvidenceSubsessions",
    iracing."RaceEvidenceResults", iracing."ScopedCarPercentileResults", iracing."ScopedRivals",
    iracing."ScopedSeasonCarBops", iracing."Weeks" IN ACCESS EXCLUSIVE MODE;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM iracing."FeatureFlags" WHERE "Key" = 'iracing-demo' AND "IsEnabled") THEN
        RAISE EXCEPTION 'Disable iracing-demo before teardown';
    END IF;
    IF to_regclass('iracing."DriverAuthorizationGrants"') IS NOT NULL THEN
        IF EXISTS (SELECT 1 FROM iracing."DriverAuthorizationGrants" WHERE "Provenance" = 2
            AND ("BindingActive" OR "ProofValid" OR "PersonalConsentVersion" IS NOT NULL OR "SharingConsentVersion" IS NOT NULL))
            OR EXISTS (SELECT 1 FROM iracing."DriverPublicationAdmissions" a JOIN iracing."DriverAuthorizationGrants" g
                ON g."Id" = a."GrantId" WHERE g."Provenance" = 2 AND a."TerminalAt" IS NULL)
        THEN RAISE EXCEPTION 'Unlink and drain controlled synthetic Driver scopes through the journal before teardown'; END IF;
        DELETE FROM iracing."DriverTrackedCopies" WHERE "GrantId" IN (SELECT "Id" FROM iracing."DriverAuthorizationGrants" WHERE "Provenance" = 2);
    END IF;
END $$;
-- Also works before the copy-fence migration, when these stores do not yet exist.
DO $$ BEGIN
    IF to_regclass('iracing."EvidencePurposes"') IS NOT NULL THEN
        UPDATE iracing."EvidencePurposes" SET "Generation" = "Generation" + 1,
            "EvidenceVersion" = "EvidenceVersion" + 1,
            "OriginalEndedAt" = LEAST("OriginalEndedAt", CURRENT_TIMESTAMP) WHERE "Provenance" = 2;
        UPDATE iracing."EvidenceCopyMarkers" SET "UnavailableAt" = LEAST("UnavailableAt", CURRENT_TIMESTAMP),
            "RemovalDueAt" = LEAST("RemovalDueAt", CURRENT_TIMESTAMP) WHERE "Provenance" = 2;
        DELETE FROM iracing."AuthorizedDriverNameCopies" WHERE "Provenance" = 2;
    END IF;
END $$;
DELETE FROM iracing."RaceEvidenceResults" WHERE "Provenance" = 2;
DELETE FROM iracing."RaceEvidenceSubsessions" WHERE "Provenance" = 2;
DELETE FROM iracing."ScopedCarPercentileResults" WHERE "Provenance" = 2;
DELETE FROM iracing."ScopedRivals" WHERE "Provenance" = 2;
DELETE FROM iracing."MappedDataCaches" WHERE "Provenance" = 2;
DELETE FROM iracing."ScopedSeasonCarBops" WHERE "Provenance" = 2;
DO $$ BEGIN
    IF to_regclass('iracing."EvidencePurposes"') IS NOT NULL THEN
        UPDATE iracing."Weeks" SET "DemoWeatherSummaryJson" = NULL, "DemoWeatherEvidenceCopyId" = NULL WHERE "DemoWeatherSummaryJson" IS NOT NULL;
    ELSE
        UPDATE iracing."Weeks" SET "DemoWeatherSummaryJson" = NULL WHERE "DemoWeatherSummaryJson" IS NOT NULL;
    END IF;
END $$;
COMMIT;
