namespace ApexRacers.Data;

/// <summary>Installed by migration, not by a process-local session variable. Old binaries omit
/// copy IDs, and reusing an old marker cannot satisfy the current-transaction requirement.</summary>
internal static class EvidenceCopyDatabaseFences
{
    internal const string Sql = """
        ALTER TABLE iracing."EvidenceCopyMarkers" ADD COLUMN "CreatedTransaction" bigint NOT NULL DEFAULT txid_current();
        -- Legacy names have no current scoped authority. Remove snapshots while retaining Field IDs
        -- and all recorded acquisition/expiry clocks. Unknown-purpose payload cleanup is separate.
        UPDATE iracing."RaceEvidenceResults" SET "DisplayName" = NULL WHERE "Provenance" <> 2;
        UPDATE iracing."ScopedRivals" SET "DisplayName" = '' WHERE "Provenance" <> 2;
        UPDATE iracing."DriverCopyCleanups" SET "DueAt" = LEAST("DueAt", "OriginalLossAt" + interval '24 hours');
        ALTER TABLE iracing."EvidencePurposes" ADD CONSTRAINT "CK_EvidencePurpose_Scope"
            CHECK ("Provenance" = 2 AND "Kind" BETWEEN 1 AND 5 AND "Generation" > 0 AND "EvidenceVersion" >= 0);
        ALTER TABLE iracing."EvidenceCopyMarkers" ADD CONSTRAINT "CK_EvidenceCopy_Clock"
            CHECK ("Kind" BETWEEN 1 AND 7 AND "Generation" > 0 AND "Version" > 0 AND length("KeyHash") = 64
                AND ("RemovalDueAt" IS NULL OR "UnavailableAt" IS NULL OR "RemovalDueAt" >= "UnavailableAt"));

        CREATE FUNCTION iracing.preserve_copy_clocks() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
            IF NEW."Id" <> OLD."Id" OR NEW."PurposeId" <> OLD."PurposeId" OR NEW."Generation" <> OLD."Generation"
                OR NEW."Provenance" <> OLD."Provenance" OR NEW."Kind" <> OLD."Kind" OR NEW."Version" <> OLD."Version"
                OR NEW."KeyHash" <> OLD."KeyHash" OR NEW."OriginalAcquiredAt" <> OLD."OriginalAcquiredAt"
                OR NEW."ExpiresAt" IS DISTINCT FROM OLD."ExpiresAt" OR NEW."CreatedTransaction" <> OLD."CreatedTransaction"
                OR OLD."UnavailableAt" IS NOT NULL AND (NEW."UnavailableAt" IS NULL OR NEW."UnavailableAt" > OLD."UnavailableAt")
                OR OLD."RemovalDueAt" IS NOT NULL AND (NEW."RemovalDueAt" IS NULL OR NEW."RemovalDueAt" > OLD."RemovalDueAt")
                OR OLD."VerifiedRemovedAt" IS NOT NULL AND NEW."VerifiedRemovedAt" IS DISTINCT FROM OLD."VerifiedRemovedAt"
            THEN RAISE EXCEPTION 'Original copy clocks and scope are immutable' USING ERRCODE = '23514'; END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER copy_clocks BEFORE UPDATE ON iracing."EvidenceCopyMarkers" FOR EACH ROW EXECUTE FUNCTION iracing.preserve_copy_clocks();

        CREATE FUNCTION iracing.preserve_purpose_clock() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
            PERFORM pg_advisory_xact_lock(370);
            IF NEW."Id" <> OLD."Id" OR NEW."Provenance" <> OLD."Provenance" OR NEW."Kind" <> OLD."Kind"
                OR NEW."CreatedAt" <> OLD."CreatedAt" OR NEW."SeasonId" IS DISTINCT FROM OLD."SeasonId"
                OR NEW."GrantId" IS DISTINCT FROM OLD."GrantId" OR NEW."PublicationAdmissionId" IS DISTINCT FROM OLD."PublicationAdmissionId"
                OR NEW."HistoricalRequestId" IS DISTINCT FROM OLD."HistoricalRequestId"
                OR OLD."HistoricalRequestEndedAt" IS NOT NULL AND NEW."HistoricalRequestEndedAt" IS DISTINCT FROM OLD."HistoricalRequestEndedAt"
                OR NEW."Generation" < OLD."Generation"
                OR OLD."OriginalEndedAt" IS NOT NULL AND (NEW."OriginalEndedAt" IS NULL OR NEW."OriginalEndedAt" > OLD."OriginalEndedAt")
            THEN RAISE EXCEPTION 'Original purpose scope and closure are immutable' USING ERRCODE = '23514'; END IF;
            NEW."EvidenceVersion" := GREATEST(NEW."EvidenceVersion", OLD."EvidenceVersion");
            RETURN NEW;
        END $$;
        CREATE TRIGGER purpose_clock BEFORE UPDATE ON iracing."EvidencePurposes" FOR EACH ROW EXECUTE FUNCTION iracing.preserve_purpose_clock();

        CREATE FUNCTION iracing.check_copy_write() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE c iracing."EvidenceCopyMarkers"; p iracing."EvidencePurposes";
        BEGIN
            PERFORM pg_advisory_xact_lock(370);
            SELECT * INTO c FROM iracing."EvidenceCopyMarkers" WHERE "Id" = NEW."EvidenceCopyId";
            SELECT * INTO p FROM iracing."EvidencePurposes" WHERE "Id" = c."PurposeId";
            IF c."Id" IS NULL OR c."CreatedTransaction" <> txid_current()
                OR c."Provenance" <> NEW."Provenance" OR c."Provenance" <> 2
                OR c."Kind" <> TG_ARGV[0]::int OR c."UnavailableAt" IS NOT NULL OR c."VerifiedRemovedAt" IS NOT NULL
                OR p."OriginalEndedAt" IS NOT NULL OR p."Generation" <> c."Generation"
                OR p."Provenance" <> c."Provenance" OR p."Kind" NOT BETWEEN 1 AND 5
            THEN RAISE EXCEPTION 'Evidence copy write is unavailable' USING ERRCODE = '23514'; END IF;
            IF p."Kind" IN (3,4,5) AND NOT EXISTS (
                SELECT 1 FROM iracing."DriverAuthorizationGrants" g
                WHERE g."Id" = p."GrantId" AND g."Revision" = p."GrantRevision" AND g."Provenance" = p."Provenance"
                    AND g."BindingActive" AND g."ProofValid" AND g."PersonalConsentVersion" IS NOT NULL
                    AND (p."Kind" <> 5 OR g."SharingConsentVersion" IS NOT NULL))
            THEN RAISE EXCEPTION 'Evidence grant is unavailable' USING ERRCODE = '23514'; END IF;
            IF p."Kind" = 2 AND c."Kind" NOT IN (1,2,3,4) OR p."Kind" = 3 AND (c."Kind" NOT IN (2,3,4)
                OR p."HistoricalRequestId" IS NULL OR p."HistoricalRequestEndedAt" IS NOT NULL)
                OR p."Kind" = 1 AND c."Kind" = 7
                OR p."Kind" = 4 AND c."Kind" NOT IN (1,5,6,7) OR p."Kind" = 5 AND c."Kind" <> 7
            THEN RAISE EXCEPTION 'Evidence purpose family is unavailable' USING ERRCODE = '23514'; END IF;
            IF p."Kind" = 2 AND NOT EXISTS (SELECT 1 FROM iracing."Seasons" WHERE "Id" = p."SeasonId" AND "Active")
            THEN RAISE EXCEPTION 'Season collection is unavailable' USING ERRCODE = '23514'; END IF;
            IF TG_TABLE_NAME = 'RaceEvidenceResults' THEN
                IF p."Kind" <> 1 AND NEW."DisplayName" IS NOT NULL
                THEN RAISE EXCEPTION 'Shared names are unavailable' USING ERRCODE = '23514'; END IF;
                IF NOT EXISTS (SELECT 1 FROM iracing."RaceEvidenceSubsessions" s WHERE s."Id" = NEW."SubsessionId"
                    AND s."Provenance" = NEW."Provenance" AND s."EvidenceCopyId" = NEW."EvidenceCopyId")
                THEN RAISE EXCEPTION 'A Field requires its matching batch header' USING ERRCODE = '23514'; END IF;
            END IF;
            IF TG_TABLE_NAME = 'AuthorizedDriverNameCopies' THEN
                IF p."Kind" NOT IN (4,5) OR NEW."GrantId" <> p."GrantId" OR NOT EXISTS (
                    SELECT 1 FROM iracing."DriverAuthorizationGrants" g WHERE g."Id" = p."GrantId" AND g."AuthorizedDriverName" = NEW."DriverName")
                THEN RAISE EXCEPTION 'Scoped name is unavailable' USING ERRCODE = '23514'; END IF;
            END IF;
            IF TG_TABLE_NAME IN ('ScopedCarPercentileResults','ScopedRivals') THEN
                IF p."Kind" <> 1 AND NOT EXISTS (SELECT 1 FROM iracing."DriverAuthorizationGrants" g
                    WHERE g."Id" = p."GrantId" AND g."UserId" = NEW."UserId")
                THEN RAISE EXCEPTION 'Private copy owner is unavailable' USING ERRCODE = '23514'; END IF;
            END IF;
            RETURN NEW;
        END $$;

        CREATE FUNCTION iracing.invalidate_removed_copy(copy_id uuid) RETURNS void LANGUAGE plpgsql AS $$
        DECLARE affected_purpose uuid; original_loss timestamptz;
        BEGIN
            PERFORM pg_advisory_xact_lock(370);
            UPDATE iracing."EvidenceCopyMarkers" SET "UnavailableAt" = COALESCE("UnavailableAt",
                    CASE WHEN "ExpiresAt" <= clock_timestamp() THEN "ExpiresAt" ELSE clock_timestamp() END),
                "RemovalDueAt" = COALESCE("RemovalDueAt", clock_timestamp() + CASE WHEN "Kind" = 7 THEN interval '24 hours' ELSE interval '7 days' END)
            WHERE "Id" = copy_id RETURNING "PurposeId", "UnavailableAt" INTO affected_purpose, original_loss;
            UPDATE iracing."EvidencePurposes" SET "EvidenceVersion" = "EvidenceVersion" + 1 WHERE "Id" = affected_purpose;
            WITH RECURSIVE dependents(id) AS (
                SELECT "CopyId" FROM iracing."EvidenceCopyDependencies" WHERE "SourceCopyId" = copy_id
                UNION SELECT d."CopyId" FROM iracing."EvidenceCopyDependencies" d JOIN dependents x ON d."SourceCopyId" = x.id
            ) UPDATE iracing."EvidenceCopyMarkers" SET "UnavailableAt" = LEAST("UnavailableAt", original_loss),
                "RemovalDueAt" = LEAST("RemovalDueAt", original_loss + CASE WHEN "Kind" = 7 THEN interval '24 hours' ELSE interval '7 days' END)
            WHERE "Id" IN (SELECT id FROM dependents);
        END $$;
        CREATE FUNCTION iracing.fence_copy_delete() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
            PERFORM iracing.invalidate_removed_copy(OLD."EvidenceCopyId");
            RETURN OLD;
        END $$;

        CREATE TRIGGER copy_write BEFORE INSERT OR UPDATE ON iracing."MappedDataCaches" FOR EACH ROW EXECUTE FUNCTION iracing.check_copy_write('1');
        CREATE TRIGGER copy_write BEFORE INSERT OR UPDATE ON iracing."RaceEvidenceSubsessions" FOR EACH ROW EXECUTE FUNCTION iracing.check_copy_write('2');
        CREATE TRIGGER copy_write BEFORE INSERT OR UPDATE ON iracing."RaceEvidenceResults" FOR EACH ROW EXECUTE FUNCTION iracing.check_copy_write('2');
        CREATE TRIGGER copy_write BEFORE INSERT OR UPDATE ON iracing."ScopedSeasonCarBops" FOR EACH ROW EXECUTE FUNCTION iracing.check_copy_write('3');
        CREATE TRIGGER copy_write BEFORE INSERT OR UPDATE ON iracing."ScopedCarPercentileResults" FOR EACH ROW EXECUTE FUNCTION iracing.check_copy_write('5');
        CREATE TRIGGER copy_write BEFORE INSERT OR UPDATE ON iracing."ScopedRivals" FOR EACH ROW EXECUTE FUNCTION iracing.check_copy_write('6');
        CREATE TRIGGER copy_write BEFORE INSERT OR UPDATE ON iracing."AuthorizedDriverNameCopies" FOR EACH ROW EXECUTE FUNCTION iracing.check_copy_write('7');
        CREATE TRIGGER copy_delete AFTER DELETE ON iracing."MappedDataCaches" FOR EACH ROW EXECUTE FUNCTION iracing.fence_copy_delete();
        CREATE TRIGGER copy_delete AFTER DELETE ON iracing."RaceEvidenceSubsessions" FOR EACH ROW EXECUTE FUNCTION iracing.fence_copy_delete();
        CREATE TRIGGER copy_delete AFTER DELETE ON iracing."RaceEvidenceResults" FOR EACH ROW EXECUTE FUNCTION iracing.fence_copy_delete();
        CREATE TRIGGER copy_delete AFTER DELETE ON iracing."ScopedSeasonCarBops" FOR EACH ROW EXECUTE FUNCTION iracing.fence_copy_delete();
        CREATE TRIGGER copy_delete AFTER DELETE ON iracing."ScopedCarPercentileResults" FOR EACH ROW EXECUTE FUNCTION iracing.fence_copy_delete();
        CREATE TRIGGER copy_delete AFTER DELETE ON iracing."ScopedRivals" FOR EACH ROW EXECUTE FUNCTION iracing.fence_copy_delete();
        CREATE TRIGGER copy_delete AFTER DELETE ON iracing."AuthorizedDriverNameCopies" FOR EACH ROW EXECUTE FUNCTION iracing.fence_copy_delete();

        CREATE FUNCTION iracing.check_weather_copy() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE c iracing."EvidenceCopyMarkers"; p iracing."EvidencePurposes";
        BEGIN
            IF (NEW."RealWeatherSummaryJson" IS DISTINCT FROM OLD."RealWeatherSummaryJson"
                OR NEW."WeatherEvidenceCopyId" IS DISTINCT FROM OLD."WeatherEvidenceCopyId"
                OR NEW."WeatherProvenance" IS DISTINCT FROM OLD."WeatherProvenance") AND NEW."RealWeatherSummaryJson" IS NOT NULL
            THEN RAISE EXCEPTION 'Real weather collection is unavailable' USING ERRCODE = '23514'; END IF;
            IF (NEW."DemoWeatherSummaryJson" IS DISTINCT FROM OLD."DemoWeatherSummaryJson"
                OR NEW."DemoWeatherEvidenceCopyId" IS DISTINCT FROM OLD."DemoWeatherEvidenceCopyId") AND NEW."DemoWeatherSummaryJson" IS NOT NULL THEN
                PERFORM pg_advisory_xact_lock(370);
                SELECT * INTO c FROM iracing."EvidenceCopyMarkers" WHERE "Id" = NEW."DemoWeatherEvidenceCopyId";
                SELECT * INTO p FROM iracing."EvidencePurposes" WHERE "Id" = c."PurposeId";
                IF c."Id" IS NULL OR c."Kind" <> 4 OR c."Provenance" <> 2 OR c."CreatedTransaction" <> txid_current()
                    OR c."UnavailableAt" IS NOT NULL OR c."VerifiedRemovedAt" IS NOT NULL
                    OR p."OriginalEndedAt" IS NOT NULL OR p."Generation" <> c."Generation"
                    OR p."Provenance" <> 2 OR p."Kind" NOT IN (1,2,3)
                    OR p."SeasonId" IS NOT NULL AND p."SeasonId" <> NEW."SeasonId"
                    OR p."Kind" = 2 AND NOT EXISTS (SELECT 1 FROM iracing."Seasons" WHERE "Id" = p."SeasonId" AND "Active")
                    OR p."Kind" = 3 AND (p."HistoricalRequestId" IS NULL OR p."HistoricalRequestEndedAt" IS NOT NULL
                        OR NOT EXISTS (SELECT 1 FROM iracing."DriverAuthorizationGrants" g WHERE g."Id" = p."GrantId"
                            AND g."Revision" = p."GrantRevision" AND g."Provenance" = 2 AND g."BindingActive"
                            AND g."ProofValid" AND g."PersonalConsentVersion" IS NOT NULL))
                THEN RAISE EXCEPTION 'Weather copy write is unavailable' USING ERRCODE = '23514'; END IF;
            END IF;
            IF (OLD."DemoWeatherEvidenceCopyId" IS DISTINCT FROM NEW."DemoWeatherEvidenceCopyId"
                OR OLD."DemoWeatherSummaryJson" IS NOT NULL AND NEW."DemoWeatherSummaryJson" IS NULL) AND OLD."DemoWeatherEvidenceCopyId" IS NOT NULL THEN
                PERFORM iracing.invalidate_removed_copy(OLD."DemoWeatherEvidenceCopyId");
            END IF;
            IF (OLD."WeatherEvidenceCopyId" IS DISTINCT FROM NEW."WeatherEvidenceCopyId"
                OR OLD."RealWeatherSummaryJson" IS NOT NULL AND NEW."RealWeatherSummaryJson" IS NULL) AND OLD."WeatherEvidenceCopyId" IS NOT NULL THEN
                PERFORM iracing.invalidate_removed_copy(OLD."WeatherEvidenceCopyId");
            END IF;
            IF NEW."DemoWeatherSummaryJson" IS NULL THEN NEW."DemoWeatherEvidenceCopyId" := NULL; END IF;
            IF NEW."RealWeatherSummaryJson" IS NULL THEN NEW."WeatherEvidenceCopyId" := NULL; END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER weather_copy BEFORE INSERT OR UPDATE ON iracing."Weeks" FOR EACH ROW EXECUTE FUNCTION iracing.check_weather_copy();
        CREATE FUNCTION iracing.fence_weather_delete() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
            PERFORM iracing.invalidate_removed_copy(OLD."DemoWeatherEvidenceCopyId");
            PERFORM iracing.invalidate_removed_copy(OLD."WeatherEvidenceCopyId");
            RETURN OLD;
        END $$;
        CREATE TRIGGER weather_delete AFTER DELETE ON iracing."Weeks" FOR EACH ROW EXECUTE FUNCTION iracing.fence_weather_delete();
        """;
}
