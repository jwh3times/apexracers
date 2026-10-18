namespace ApexRacers.Data;

/// <summary>Forward replacement of copy-loss propagation; existing copy clocks are not rewritten.</summary>
internal static class EvidenceCopyClockFences
{
    internal const string Sql = """
        CREATE OR REPLACE FUNCTION iracing.invalidate_removed_copy(copy_id uuid) RETURNS void LANGUAGE plpgsql AS $$
        DECLARE affected_purpose uuid; original_loss timestamptz; observed_loss timestamptz;
        BEGIN
            PERFORM pg_advisory_xact_lock(370);
            observed_loss := clock_timestamp();
            UPDATE iracing."EvidenceCopyMarkers" SET
                "UnavailableAt" = LEAST("UnavailableAt", "ExpiresAt", observed_loss),
                "RemovalDueAt" = LEAST(COALESCE("RemovalDueAt",
                    LEAST("UnavailableAt", "ExpiresAt", observed_loss)
                        + CASE WHEN "Kind" = 7 THEN interval '24 hours' ELSE interval '7 days' END),
                    CASE WHEN "Kind" = 1 THEN "ExpiresAt" + interval '48 hours' END)
            WHERE "Id" = copy_id RETURNING "PurposeId", "UnavailableAt" INTO affected_purpose, original_loss;
            UPDATE iracing."EvidencePurposes" SET "EvidenceVersion" = "EvidenceVersion" + 1 WHERE "Id" = affected_purpose;
            WITH RECURSIVE losses(id, loss_at) AS (
                SELECT copy_id, original_loss WHERE original_loss IS NOT NULL
                UNION
                SELECT d."CopyId", LEAST(x.loss_at, c."UnavailableAt", c."ExpiresAt")
                FROM iracing."EvidenceCopyDependencies" d JOIN losses x ON d."SourceCopyId" = x.id
                JOIN iracing."EvidenceCopyMarkers" c ON c."Id" = d."CopyId"
            ), earliest AS (
                SELECT id, min(loss_at) AS loss_at FROM losses GROUP BY id
            ) UPDATE iracing."EvidenceCopyMarkers" c SET
                "UnavailableAt" = LEAST(c."UnavailableAt", e.loss_at),
                "RemovalDueAt" = LEAST(c."RemovalDueAt",
                    CASE WHEN c."Id" <> copy_id THEN e.loss_at
                        + CASE WHEN c."Kind" = 7 THEN interval '24 hours' ELSE interval '7 days' END END,
                    CASE WHEN c."Kind" = 1 THEN c."ExpiresAt" + interval '48 hours' END)
            FROM earliest e WHERE c."Id" = e.id;
        END $$;
        """;
}
