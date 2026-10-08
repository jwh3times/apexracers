namespace ApexRacers.Data;

internal static class DriverReferenceDatabaseFences
{
    internal const string Sql = """
        CREATE FUNCTION iracing.fence_scoped_driver_reference() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
            PERFORM pg_advisory_xact_lock(370);
            IF TG_OP = 'UPDATE' THEN RAISE EXCEPTION 'Reference binding and lifetime are immutable' USING ERRCODE='23514'; END IF;
            IF length(NEW."TokenHash") != 64 OR NEW."TokenHash" !~ '^[0-9A-F]{64}$'
                OR NEW."ExpiresAt" > NEW."CreatedAt" + INTERVAL '15 minutes'
                OR NOT EXISTS (SELECT 1 FROM iracing."DriverAuthorizationGrants" g WHERE g."Id"=NEW."RecipientGrantId"
                    AND g."Revision"=NEW."RecipientRevision" AND g."ProofReceiptId"=NEW."RecipientProofId"
                    AND g."Provenance"=NEW."Provenance" AND g."BindingActive" AND g."ProofValid" AND g."PersonalConsentVersion"='personal-v1'
                    AND EXISTS (SELECT 1 FROM identity."Users" u WHERE u."Id"=g."UserId"))
                OR NOT EXISTS (SELECT 1 FROM iracing."DriverAuthorizationGrants" g WHERE g."Id"=NEW."TargetGrantId"
                    AND g."Revision"=NEW."TargetRevision" AND g."ProofReceiptId"=NEW."TargetProofId"
                    AND g."Provenance"=NEW."Provenance" AND g."BindingActive" AND g."ProofValid"
                    AND g."PersonalConsentVersion"='personal-v1' AND g."SharingConsentVersion"='sharing-v1'
                    AND g."AuthorizedDriverName" IS NOT NULL AND EXISTS (SELECT 1 FROM identity."Users" u WHERE u."Id"=g."UserId"))
            THEN RAISE EXCEPTION 'Reference authority or lifetime is invalid' USING ERRCODE='23514'; END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER scoped_driver_reference_fence BEFORE INSERT OR UPDATE ON iracing."ScopedDriverReferences"
            FOR EACH ROW EXECUTE FUNCTION iracing.fence_scoped_driver_reference();

        CREATE FUNCTION iracing.fence_private_driver_follow() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
            PERFORM pg_advisory_xact_lock(370);
            IF TG_OP='UPDATE' AND (ROW(NEW."Id",NEW."RecipientGrantId",NEW."TargetGrantId",NEW."Provenance",NEW."CreatedAt")
                IS DISTINCT FROM ROW(OLD."Id",OLD."RecipientGrantId",OLD."TargetGrantId",OLD."Provenance",OLD."CreatedAt")
                OR OLD."OriginalLossAt" IS NOT NULL AND (NEW."OriginalLossAt" IS NULL OR NEW."OriginalLossAt">OLD."OriginalLossAt")
                OR OLD."RemoveBy" IS NOT NULL AND (NEW."RemoveBy" IS NULL OR NEW."RemoveBy">OLD."RemoveBy"))
            THEN RAISE EXCEPTION 'Follow binding and original clocks are immutable' USING ERRCODE='23514'; END IF;
            IF NEW."OriginalLossAt" IS NOT NULL AND (NEW."ReactivateBefore" IS DISTINCT FROM NEW."OriginalLossAt"+INTERVAL '90 days'
                OR NEW."RemoveBy" IS NULL OR NEW."RemoveBy">NEW."OriginalLossAt"+INTERVAL '97 days')
            THEN RAISE EXCEPTION 'Follow dormant bounds are invalid' USING ERRCODE='23514'; END IF;
            IF NEW."Active" AND (TG_OP='INSERT' OR NOT OLD."Active") AND (
                NEW."ReactivateBefore" IS NOT NULL AND NEW."ReactivateBefore"<=CURRENT_TIMESTAMP
                OR NOT EXISTS (SELECT 1 FROM iracing."DriverAuthorizationGrants" g WHERE g."Id"=NEW."RecipientGrantId"
                    AND g."Provenance"=NEW."Provenance" AND g."BindingActive" AND g."ProofValid" AND g."PersonalConsentVersion"='personal-v1')
                OR NOT EXISTS (SELECT 1 FROM iracing."DriverAuthorizationGrants" g WHERE g."Id"=NEW."TargetGrantId"
                    AND g."Provenance"=NEW."Provenance" AND g."BindingActive" AND g."ProofValid" AND g."PersonalConsentVersion"='personal-v1'
                    AND g."SharingConsentVersion"='sharing-v1'))
            THEN RAISE EXCEPTION 'Follow requires current authority and an open original recovery window' USING ERRCODE='23514'; END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER private_driver_follow_fence BEFORE INSERT OR UPDATE ON iracing."PrivateDriverFollows"
            FOR EACH ROW EXECUTE FUNCTION iracing.fence_private_driver_follow();

        CREATE FUNCTION iracing.close_scoped_driver_associations() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE loss_at timestamptz;
        BEGIN
            IF ROW(NEW."Revision",NEW."ProofReceiptId",NEW."BindingActive",NEW."ProofValid",NEW."PersonalConsentVersion",NEW."SharingConsentVersion")
                IS DISTINCT FROM ROW(OLD."Revision",OLD."ProofReceiptId",OLD."BindingActive",OLD."ProofValid",OLD."PersonalConsentVersion",OLD."SharingConsentVersion")
            THEN DELETE FROM iracing."ScopedDriverReferences" WHERE "RecipientGrantId"=NEW."Id" OR "TargetGrantId"=NEW."Id"; END IF;
            IF (OLD."SharingConsentVersion" IS NOT NULL AND NEW."SharingConsentVersion" IS NULL)
                OR (OLD."PersonalConsentVersion" IS NOT NULL AND NEW."PersonalConsentVersion" IS NULL)
                OR (OLD."ProofValid" AND NOT NEW."ProofValid") OR (OLD."BindingActive" AND NOT NEW."BindingActive") THEN
                loss_at := CASE WHEN NEW."PersonalConsentVersion" IS NULL OR NOT NEW."BindingActive" OR NOT NEW."ProofValid" THEN NEW."PersonalClosedAt" ELSE NEW."SharingClosedAt" END;
                IF loss_at IS NULL AND EXISTS (SELECT 1 FROM iracing."PrivateDriverFollows" WHERE "Active"
                    AND ("TargetGrantId"=NEW."Id" OR "RecipientGrantId"=NEW."Id"))
                THEN RAISE EXCEPTION 'Follow closure requires an original durable loss time' USING ERRCODE='23514'; END IF;
                UPDATE iracing."PrivateDriverFollows" SET "Active"=FALSE,
                    "OriginalLossAt"=LEAST("OriginalLossAt",loss_at), "ReactivateBefore"=LEAST("OriginalLossAt",loss_at)+INTERVAL '90 days',
                    "RemoveBy"=LEAST("RemoveBy",LEAST("OriginalLossAt",loss_at)+INTERVAL '97 days')
                WHERE "TargetGrantId"=NEW."Id" OR "RecipientGrantId"=NEW."Id" AND (NEW."PersonalConsentVersion" IS NULL OR NOT NEW."BindingActive" OR NOT NEW."ProofValid");
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER scoped_driver_association_closure AFTER UPDATE ON iracing."DriverAuthorizationGrants"
            FOR EACH ROW EXECUTE FUNCTION iracing.close_scoped_driver_associations();
        """;
}
