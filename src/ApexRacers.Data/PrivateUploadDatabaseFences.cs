namespace ApexRacers.Data;

internal static class PrivateUploadDatabaseFences
{
    internal const string UserErasureSql = """
        CREATE FUNCTION iracing.check_user_erasure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
            PERFORM pg_advisory_xact_lock(370);
            IF EXISTS (SELECT 1 FROM iracing."DriverAuthorizationGrants" g WHERE g."UserId" = OLD."Id"
                AND (g."BindingActive" OR g."ProofValid" OR g."PersonalConsentVersion" IS NOT NULL
                    OR g."SharingConsentVersion" IS NOT NULL OR g."AuthorizedDriverName" IS NOT NULL
                    OR NOT EXISTS (SELECT 1 FROM iracing."DriverLifecycleOperations" o
                        WHERE o."GrantId" = g."Id" AND o."Kind" = 5 AND o."CompletedAt" IS NOT NULL)))
                OR EXISTS (SELECT 1 FROM iracing."DriverPublicationAdmissions" a
                    JOIN iracing."DriverAuthorizationGrants" g ON g."Id" = a."GrantId"
                    WHERE g."UserId" = OLD."Id" AND a."TerminalAt" IS NULL)
            THEN RAISE EXCEPTION 'User erasure requires completed User-wide enforcement' USING ERRCODE = '23514'; END IF;
            UPDATE iracing."DriverProofReceipts" SET "Authority" = '' WHERE "UserId" = OLD."Id";
            RETURN OLD;
        END $$;
        CREATE TRIGGER user_erasure BEFORE DELETE ON identity."Users"
            FOR EACH ROW EXECUTE FUNCTION iracing.check_user_erasure();
        """;

    internal const string Sql = """
        ALTER TABLE iracing."EvidenceCopyMarkers" DROP CONSTRAINT "CK_EvidenceCopy_Clock";
        ALTER TABLE iracing."EvidenceCopyMarkers" ADD CONSTRAINT "CK_EvidenceCopy_Clock"
            CHECK ("Kind" BETWEEN 1 AND 8 AND "Generation" > 0 AND "Version" > 0 AND length("KeyHash") = 64
                AND ("RemovalDueAt" IS NULL OR "UnavailableAt" IS NULL OR "RemovalDueAt" >= "UnavailableAt"));

        CREATE FUNCTION iracing.check_private_upload_write() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE c iracing."EvidenceCopyMarkers"; p iracing."EvidencePurposes"; g iracing."DriverAuthorizationGrants";
            prior iracing."EvidenceCopyMarkers"; prior_purpose iracing."EvidencePurposes";
        BEGIN
            PERFORM pg_advisory_xact_lock(370);
            SELECT * INTO c FROM iracing."EvidenceCopyMarkers" WHERE "Id" = NEW."EvidenceCopyId";
            SELECT * INTO p FROM iracing."EvidencePurposes" WHERE "Id" = c."PurposeId";
            SELECT * INTO g FROM iracing."DriverAuthorizationGrants" WHERE "Id" = p."GrantId";
            IF c."Id" IS NULL OR c."CreatedTransaction" <> txid_current() OR c."Kind" <> 8
                OR c."UnavailableAt" IS NOT NULL OR c."VerifiedRemovedAt" IS NOT NULL
                OR c."Provenance" <> 2 OR NEW."Provenance" <> 2 OR p."Kind" <> 4
                OR p."OriginalEndedAt" IS NOT NULL OR p."Generation" <> c."Generation" OR p."Provenance" <> 2
                OR g."Id" IS NULL OR g."Revision" <> p."GrantRevision" OR g."Provenance" <> 2
                OR NOT g."BindingActive" OR NOT g."ProofValid" OR g."PersonalConsentVersion" IS NULL
                OR g."UserId" <> NEW."UserId" OR g."CustomerId" <> NEW."CustomerId"
                OR NOT EXISTS (SELECT 1 FROM identity."Users" u WHERE u."Id" = NEW."UserId")
                OR EXISTS (SELECT 1 FROM iracing."DriverLifecycleOperations" o
                    JOIN iracing."DriverAuthorizationGrants" a ON a."Id" = o."GrantId"
                    WHERE a."UserId" = NEW."UserId" AND o."Kind" = 5)
            THEN RAISE EXCEPTION 'Private upload authority is unavailable' USING ERRCODE = '23514'; END IF;
            IF TG_OP = 'UPDATE' THEN
                IF NEW."Id" <> OLD."Id" OR NEW."UserId" <> OLD."UserId" OR NEW."CustomerId" <> OLD."CustomerId"
                    OR NEW."Provenance" <> OLD."Provenance" OR NEW."CarId" <> OLD."CarId" OR NEW."TrackId" <> OLD."TrackId"
                    OR NEW."RecordedAt" <> OLD."RecordedAt" OR NEW."SessionType" <> OLD."SessionType"
                THEN RAISE EXCEPTION 'Original upload association is immutable' USING ERRCODE = '23514'; END IF;
                IF NEW."EvidenceCopyId" <> OLD."EvidenceCopyId" THEN
                    SELECT * INTO prior FROM iracing."EvidenceCopyMarkers" WHERE "Id" = OLD."EvidenceCopyId";
                    SELECT * INTO prior_purpose FROM iracing."EvidencePurposes" WHERE "Id" = prior."PurposeId";
                    IF prior."Kind" <> 8 OR prior_purpose."Kind" <> 4
                        OR prior_purpose."GrantId" IS DISTINCT FROM p."GrantId"
                        OR prior."UnavailableAt" IS NULL OR prior_purpose."OriginalEndedAt" IS NULL
                        OR prior."UnavailableAt" <> prior_purpose."OriginalEndedAt"
                        OR p."CreatedAt" < prior."UnavailableAt" OR p."CreatedAt" >= prior."UnavailableAt" + interval '90 days'
                        OR prior."RemovalDueAt" IS NULL OR prior."RemovalDueAt" <= p."CreatedAt"
                        OR c."OriginalAcquiredAt" <> prior."OriginalAcquiredAt" OR c."KeyHash" <> prior."KeyHash"
                        OR c."Version" <= prior."Version"
                    THEN RAISE EXCEPTION 'Dormant upload recovery is unavailable' USING ERRCODE = '23514'; END IF;
                    PERFORM iracing.invalidate_removed_copy(OLD."EvidenceCopyId");
                END IF;
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER private_upload_write BEFORE INSERT OR UPDATE ON iracing."PrivateUploadSessions"
            FOR EACH ROW EXECUTE FUNCTION iracing.check_private_upload_write();
        CREATE TRIGGER private_upload_delete AFTER DELETE ON iracing."PrivateUploadSessions"
            FOR EACH ROW EXECUTE FUNCTION iracing.fence_copy_delete();

        CREATE FUNCTION iracing.check_private_lap_write() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
            PERFORM pg_advisory_xact_lock(370);
            IF TG_OP = 'UPDATE' OR NOT EXISTS (
                SELECT 1 FROM iracing."PrivateUploadSessions" s
                JOIN iracing."EvidenceCopyMarkers" c ON c."Id" = s."EvidenceCopyId"
                JOIN iracing."EvidencePurposes" p ON p."Id" = c."PurposeId"
                JOIN iracing."DriverAuthorizationGrants" g ON g."Id" = p."GrantId"
                WHERE s."Id" = NEW."SessionId" AND c."CreatedTransaction" = txid_current()
                    AND c."Kind" = 8 AND c."UnavailableAt" IS NULL AND c."VerifiedRemovedAt" IS NULL
                    AND p."Kind" = 4 AND p."OriginalEndedAt" IS NULL AND p."Generation" = c."Generation"
                    AND p."GrantRevision" = g."Revision" AND g."BindingActive" AND g."ProofValid"
                    AND g."PersonalConsentVersion" IS NOT NULL
                    AND c."Provenance" = 2 AND p."Provenance" = 2 AND g."Provenance" = 2 AND s."Provenance" = 2
                    AND g."UserId" = s."UserId" AND g."CustomerId" = s."CustomerId"
                    AND EXISTS (SELECT 1 FROM identity."Users" u WHERE u."Id" = s."UserId")
                    AND NOT EXISTS (SELECT 1 FROM iracing."DriverLifecycleOperations" o
                        JOIN iracing."DriverAuthorizationGrants" a ON a."Id" = o."GrantId"
                        WHERE a."UserId" = s."UserId" AND o."Kind" = 5))
            THEN RAISE EXCEPTION 'Private lap write is unavailable' USING ERRCODE = '23514'; END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER private_lap_write BEFORE INSERT OR UPDATE ON iracing."PrivateUploadedLaps"
            FOR EACH ROW EXECUTE FUNCTION iracing.check_private_lap_write();
        CREATE FUNCTION iracing.invalidate_private_lap_delete() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE marker_id uuid;
        BEGIN
            SELECT "EvidenceCopyId" INTO marker_id FROM iracing."PrivateUploadSessions" WHERE "Id" = OLD."SessionId";
            IF marker_id IS NOT NULL THEN PERFORM iracing.invalidate_removed_copy(marker_id); END IF;
            RETURN OLD;
        END $$;
        CREATE TRIGGER private_lap_delete AFTER DELETE ON iracing."PrivateUploadedLaps"
            FOR EACH ROW EXECUTE FUNCTION iracing.invalidate_private_lap_delete();
        """;
}
