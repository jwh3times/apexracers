namespace ApexRacers.Data;

internal static class PublicationDatabaseFences
{
    internal const string Sql = """
        CREATE OR REPLACE FUNCTION iracing.check_user_erasure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
            PERFORM pg_advisory_xact_lock(370);
            IF EXISTS (SELECT 1 FROM iracing."DriverAuthorizationGrants" g WHERE g."UserId" = OLD."Id"
                AND (g."BindingActive" OR g."ProofValid" OR g."PersonalConsentVersion" IS NOT NULL
                    OR g."SharingConsentVersion" IS NOT NULL OR g."AuthorizedDriverName" IS NOT NULL
                    OR NOT EXISTS (SELECT 1 FROM iracing."DriverLifecycleOperations" o
                        WHERE o."GrantId" = g."Id" AND o."Kind" = 5 AND o."CompletedAt" IS NOT NULL)))
                OR EXISTS (SELECT 1 FROM iracing."DriverPublicationAdmissions" a
                    JOIN iracing."DriverAuthorizationGrants" g ON g."Id" = a."GrantId"
                    WHERE g."UserId" = OLD."Id" AND a."TerminalAt" IS NULL)
                OR EXISTS (SELECT 1 FROM iracing."PublicationReleases" r WHERE r."TerminalAt" IS NULL
                    AND (r."RecipientUserId" = OLD."Id" OR EXISTS (SELECT 1 FROM iracing."PublicationReleaseDependencies" d
                        WHERE d."ReleaseId" = r."Id" AND d."UserId" = OLD."Id")))
            THEN RAISE EXCEPTION 'User erasure requires completed User-wide enforcement' USING ERRCODE = '23514'; END IF;
            UPDATE iracing."DriverProofReceipts" SET "Authority" = '' WHERE "UserId" = OLD."Id";
            RETURN OLD;
        END $$;

        CREATE FUNCTION iracing.fence_publication_release() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
            PERFORM pg_advisory_xact_lock(370);
            IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Publication accounting cannot be forgotten' USING ERRCODE = '23514'; END IF;
            IF TG_OP = 'UPDATE' AND (ROW(NEW."Id",NEW."Sequence",NEW."ContextHash",NEW."RepresentationHash",NEW."DependencyHash",
                NEW."CatalogId",NEW."CatalogRevision",NEW."Provenance",NEW."RecipientUserId",NEW."Purpose",NEW."Incarnation",NEW."ReservedAt")
                IS DISTINCT FROM ROW(OLD."Id",OLD."Sequence",OLD."ContextHash",OLD."RepresentationHash",OLD."DependencyHash",
                OLD."CatalogId",OLD."CatalogRevision",OLD."Provenance",OLD."RecipientUserId",OLD."Purpose",OLD."Incarnation",OLD."ReservedAt")
                OR OLD."DispatchStartedAt" IS NOT NULL AND NEW."DispatchStartedAt" IS DISTINCT FROM OLD."DispatchStartedAt"
                OR OLD."TerminalAt" IS NOT NULL AND (NEW."TerminalAt" IS DISTINCT FROM OLD."TerminalAt" OR NEW."ProvenUnsent" != OLD."ProvenUnsent"))
            THEN RAISE EXCEPTION 'Publication identity/checkpoint is immutable' USING ERRCODE = '23514'; END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER publication_release_fence BEFORE UPDATE OR DELETE ON iracing."PublicationReleases"
            FOR EACH ROW EXECUTE FUNCTION iracing.fence_publication_release();

        CREATE FUNCTION iracing.fence_publication_dependency() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
            PERFORM pg_advisory_xact_lock(370);
            IF TG_OP != 'INSERT' THEN RAISE EXCEPTION 'Publication dependencies are immutable' USING ERRCODE = '23514'; END IF;
            IF NEW."GrantId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM iracing."DriverAuthorizationGrants" g
                WHERE g."Id" = NEW."GrantId" AND g."UserId" = NEW."UserId" AND g."CustomerId" = NEW."CustomerId"
                    AND g."Provenance" = NEW."Provenance")
            THEN RAISE EXCEPTION 'Publication grant binding is invalid' USING ERRCODE = '23514'; END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER publication_dependency_fence BEFORE INSERT OR UPDATE OR DELETE ON iracing."PublicationReleaseDependencies"
            FOR EACH ROW EXECUTE FUNCTION iracing.fence_publication_dependency();

        CREATE FUNCTION iracing.coordinate_publication_grant() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE opens boolean;
        BEGIN
            PERFORM pg_advisory_xact_lock(370);
            IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
            opens := TG_OP = 'INSERT';
            IF TG_OP = 'UPDATE' THEN
                IF ROW(NEW."UserId",NEW."CustomerId",NEW."Provenance") IS DISTINCT FROM ROW(OLD."UserId",OLD."CustomerId",OLD."Provenance")
                THEN RAISE EXCEPTION 'Original Driver grant binding is immutable' USING ERRCODE = '23514'; END IF;
                opens := (NOT OLD."BindingActive" AND NEW."BindingActive") OR (NOT OLD."ProofValid" AND NEW."ProofValid")
                    OR (NEW."PersonalConsentVersion" IS NOT NULL AND NEW."PersonalConsentVersion" IS DISTINCT FROM OLD."PersonalConsentVersion")
                    OR (NEW."SharingConsentVersion" IS NOT NULL AND NEW."SharingConsentVersion" IS DISTINCT FROM OLD."SharingConsentVersion")
                    OR (NEW."AuthorizedDriverName" IS NOT NULL AND NEW."AuthorizedDriverName" IS DISTINCT FROM OLD."AuthorizedDriverName")
                    OR NEW."ProofReceiptId" IS DISTINCT FROM OLD."ProofReceiptId"
                    OR (NEW."Revision" IS DISTINCT FROM OLD."Revision" AND NOT (
                        OLD."BindingActive" AND NOT NEW."BindingActive" OR OLD."ProofValid" AND NOT NEW."ProofValid"
                        OR OLD."PersonalConsentVersion" IS NOT NULL AND NEW."PersonalConsentVersion" IS NULL
                        OR OLD."SharingConsentVersion" IS NOT NULL AND NEW."SharingConsentVersion" IS NULL
                        OR OLD."AuthorizedDriverName" IS NOT NULL AND NEW."AuthorizedDriverName" IS NULL
                        OR NEW."PersonalClosedAt" IS NOT NULL AND (OLD."PersonalClosedAt" IS NULL OR NEW."PersonalClosedAt" <= OLD."PersonalClosedAt")
                        OR NEW."SharingClosedAt" IS NOT NULL AND (OLD."SharingClosedAt" IS NULL OR NEW."SharingClosedAt" <= OLD."SharingClosedAt")))
                    OR ROW(NEW."UserId",NEW."CustomerId",NEW."Provenance") IS DISTINCT FROM ROW(OLD."UserId",OLD."CustomerId",OLD."Provenance");
            END IF;
            IF opens AND EXISTS (SELECT 1 FROM iracing."PublicationReleaseDependencies" d
                JOIN iracing."PublicationReleases" r ON r."Id"=d."ReleaseId"
                WHERE r."TerminalAt" IS NULL AND d."CustomerId"=NEW."CustomerId" AND d."Provenance"=NEW."Provenance")
            THEN RAISE EXCEPTION 'Authorization opening requires cohort writer drain' USING ERRCODE = '23514'; END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER publication_grant_coordination BEFORE INSERT OR UPDATE OR DELETE ON iracing."DriverAuthorizationGrants"
            FOR EACH ROW EXECUTE FUNCTION iracing.coordinate_publication_grant();
        """;
}
