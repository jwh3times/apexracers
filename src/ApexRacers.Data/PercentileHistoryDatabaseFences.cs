namespace ApexRacers.Data;

/// <summary>Installed only by the classified history cutover migration. Historical SQL remains frozen.</summary>
internal static class PercentileHistoryDatabaseFences
{
    internal const string Sql = """
        CREATE OR REPLACE FUNCTION iracing.check_copy_write() RETURNS trigger LANGUAGE plpgsql AS $$
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
            IF TG_TABLE_NAME IN ('LegacyUnclassifiedPercentileResults','SelectionScopedCarPercentileResults','ScopedRivals') THEN
                IF p."Kind" <> 1 AND NOT EXISTS (SELECT 1 FROM iracing."DriverAuthorizationGrants" g
                    WHERE g."Id" = p."GrantId" AND g."UserId" = NEW."UserId")
                THEN RAISE EXCEPTION 'Private copy owner is unavailable' USING ERRCODE = '23514'; END IF;
            END IF;
            RETURN NEW;
        END $$;

        CREATE FUNCTION iracing.check_classified_percentile() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE c iracing."EvidenceCopyMarkers"; p iracing."EvidencePurposes"; expected_key text;
        BEGIN
            PERFORM pg_advisory_xact_lock(370);
            SELECT * INTO c FROM iracing."EvidenceCopyMarkers" WHERE "Id" = NEW."EvidenceCopyId";
            SELECT * INTO p FROM iracing."EvidencePurposes" WHERE "Id" = c."PurposeId";
            expected_key := 'percentile-history:v1:' || NEW."Provenance"::text || ':'
                || replace(NEW."UserId"::text, '-', '') || ':' || NEW."CustomerId"::text || ':'
                || NEW."OwnerKind"::text || ':' || COALESCE(replace(NEW."GrantId"::text, '-', ''), '-') || ':'
                || NEW."CarId"::text || ':' || NEW."SeriesId"::text || ':'
                || replace(NEW."WeekId"::text, '-', '') || ':' || NEW."SelectionKey";
            IF c."Id" IS NULL OR c."Kind" <> 5 OR c."CreatedTransaction" <> txid_current()
                OR c."KeyHash" IS DISTINCT FROM upper(encode(sha256(convert_to(expected_key, 'UTF8')), 'hex'))
                OR NEW."ReadingRevision" IS DISTINCT FROM c."Version"
                OR NEW."PreparationPurposeId" IS DISTINCT FROM p."Id"
                OR NEW."PreparationGeneration" IS DISTINCT FROM p."Generation"
                OR NEW."PreparationVersion" < 0 OR NEW."PreparationVersion" > p."EvidenceVersion"
                OR NOT EXISTS (SELECT 1 FROM iracing."Weeks" w JOIN iracing."Seasons" s ON s."Id" = w."SeasonId"
                    WHERE w."Id" = NEW."WeekId" AND s."SeriesId" = NEW."SeriesId")
            THEN RAISE EXCEPTION 'Classified percentile preparation is unavailable' USING ERRCODE = '23514'; END IF;
            IF NEW."OwnerKind" = 0 THEN
                IF p."Kind" <> 1 OR NEW."CustomerId" <> 100001 OR NEW."GrantId" IS NOT NULL
                    OR NEW."GrantRevision" IS NOT NULL OR NEW."Provenance" <> 2
                    OR NEW."PersonalBestLapEvidence" <> 1 OR NEW."UploadedSessionType" IS NOT NULL
                THEN RAISE EXCEPTION 'Demo percentile owner is unavailable' USING ERRCODE = '23514'; END IF;
            ELSIF NEW."OwnerKind" = 1 THEN
                IF p."Kind" <> 4 OR NEW."GrantId" IS DISTINCT FROM p."GrantId"
                    OR NEW."GrantRevision" IS DISTINCT FROM p."GrantRevision"
                    OR NOT EXISTS (SELECT 1 FROM iracing."DriverAuthorizationGrants" g
                        WHERE g."Id" = NEW."GrantId" AND g."UserId" = NEW."UserId" AND g."CustomerId" = NEW."CustomerId"
                            AND g."Provenance" = NEW."Provenance" AND g."Revision" = NEW."GrantRevision"
                            AND g."BindingActive" AND g."ProofValid" AND g."PersonalConsentVersion" IS NOT NULL)
                THEN RAISE EXCEPTION 'Personal percentile owner is unavailable' USING ERRCODE = '23514'; END IF;
            ELSE RAISE EXCEPTION 'Percentile owner kind is unavailable' USING ERRCODE = '23514'; END IF;
            IF NEW."PersonalBestLapEvidence" = 2 AND (NEW."SelectionKey" = 'v1:0:0'
                OR NEW."UploadedSessionType" IS NULL OR NEW."UploadedSessionType" NOT BETWEEN 0 AND 5
                OR (split_part(NEW."SelectionKey", ':', 3)::int & (1 << NEW."UploadedSessionType")) = 0)
            THEN RAISE EXCEPTION 'Selected uploaded evidence is unavailable' USING ERRCODE = '23514'; END IF;
            IF TG_OP = 'UPDATE' AND (NEW."Id" <> OLD."Id" OR NEW."UserId" <> OLD."UserId"
                OR NEW."CustomerId" <> OLD."CustomerId" OR NEW."Provenance" <> OLD."Provenance"
                OR NEW."OwnerKind" <> OLD."OwnerKind" OR NEW."GrantId" IS DISTINCT FROM OLD."GrantId"
                OR NEW."SelectionKey" <> OLD."SelectionKey" OR NEW."SelectionPolicyVersion" <> OLD."SelectionPolicyVersion"
                OR NEW."CarId" <> OLD."CarId" OR NEW."SeriesId" <> OLD."SeriesId" OR NEW."WeekId" <> OLD."WeekId")
            THEN RAISE EXCEPTION 'Percentile identity is immutable' USING ERRCODE = '23514'; END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER classified_identity BEFORE INSERT OR UPDATE ON iracing."SelectionScopedCarPercentileResults"
            FOR EACH ROW EXECUTE FUNCTION iracing.check_classified_percentile();
        CREATE TRIGGER copy_write BEFORE INSERT OR UPDATE ON iracing."SelectionScopedCarPercentileResults"
            FOR EACH ROW EXECUTE FUNCTION iracing.check_copy_write('5');
        CREATE TRIGGER copy_delete AFTER DELETE ON iracing."SelectionScopedCarPercentileResults"
            FOR EACH ROW EXECUTE FUNCTION iracing.fence_copy_delete();
        """;
}
