using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <summary>
    /// Rewrites cached ApexRacers response DTOs to the canonical Driver identity names. The JSON
    /// transformations are scoped by cache-key family and touch exact PascalCase property tokens,
    /// leaving cached upstream SDK shapes unchanged.
    /// </summary>
    public partial class UnifyCachedDriverIdentityNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH source AS (
                    SELECT "Id", "Payload"::jsonb AS payload
                    FROM "iracing"."ExternalDataCaches"
                    WHERE "CacheKey" LIKE 'laps:%'
                )
                UPDATE "iracing"."ExternalDataCaches" AS cache
                SET "Payload" = jsonb_set(
                    (source.payload - 'CustId')
                        || CASE
                            WHEN source.payload ? 'CustId'
                            THEN jsonb_build_object('CustomerId', source.payload -> 'CustId')
                            ELSE '{}'::jsonb
                        END,
                    '{Laps}',
                    COALESCE(
                        (
                            SELECT jsonb_agg(
                                (lap - 'Valid' - 'Timed')
                                    || jsonb_build_object(
                                        'Timed',
                                        COALESCE((lap ->> 'LapTimeSeconds')::double precision > 0, false))
                                ORDER BY ordinal)
                            FROM jsonb_array_elements(COALESCE(source.payload -> 'Laps', '[]'::jsonb))
                                WITH ORDINALITY AS laps(lap, ordinal)
                        ),
                        '[]'::jsonb),
                    true)::text
                FROM source
                WHERE cache."Id" = source."Id";

                UPDATE "iracing"."ExternalDataCaches"
                SET "Payload" = (
                    SELECT COALESCE(
                        jsonb_agg(
                            (item - 'CustId' - 'Driver')
                                || CASE
                                    WHEN item ? 'CustId'
                                    THEN jsonb_build_object('CustomerId', item -> 'CustId')
                                    ELSE '{}'::jsonb
                                END
                                || CASE
                                    WHEN item ? 'Driver'
                                    THEN jsonb_build_object('DriverName', item -> 'Driver')
                                    ELSE '{}'::jsonb
                                END
                            ORDER BY ordinal),
                        '[]'::jsonb)::text
                    FROM jsonb_array_elements("Payload"::jsonb)
                        WITH ORDINALITY AS entries(item, ordinal))
                WHERE "CacheKey" LIKE 'leaderboard:%';

                UPDATE "iracing"."ExternalDataCaches"
                SET "Payload" = (
                    SELECT COALESCE(
                        jsonb_agg(
                            (item - 'CustId')
                                || CASE
                                    WHEN item ? 'CustId'
                                    THEN jsonb_build_object('CustomerId', item -> 'CustId')
                                    ELSE '{}'::jsonb
                                END
                            ORDER BY ordinal),
                        '[]'::jsonb)::text
                    FROM jsonb_array_elements("Payload"::jsonb)
                        WITH ORDINALITY AS entries(item, ordinal))
                WHERE "CacheKey" LIKE 'standings:%'
                    OR "CacheKey" LIKE 'tt-standings:%'
                    OR "CacheKey" LIKE 'qual:%';

                UPDATE "iracing"."ExternalDataCaches"
                SET "Payload" = (
                    SELECT COALESCE(
                        jsonb_agg(
                            (item - 'CustId' - 'DisplayName')
                                || CASE
                                    WHEN item ? 'CustId'
                                    THEN jsonb_build_object('CustomerId', item -> 'CustId')
                                    ELSE '{}'::jsonb
                                END
                                || CASE
                                    WHEN item ? 'DisplayName'
                                    THEN jsonb_build_object('DriverName', item -> 'DisplayName')
                                    ELSE '{}'::jsonb
                                END
                            ORDER BY ordinal),
                        '[]'::jsonb)::text
                    FROM jsonb_array_elements("Payload"::jsonb)
                        WITH ORDINALITY AS entries(item, ordinal))
                WHERE "CacheKey" LIKE 'driversearch:%';
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH source AS (
                    SELECT "Id", "Payload"::jsonb AS payload
                    FROM "iracing"."ExternalDataCaches"
                    WHERE "CacheKey" LIKE 'laps:%'
                )
                UPDATE "iracing"."ExternalDataCaches" AS cache
                SET "Payload" = jsonb_set(
                    (source.payload - 'CustomerId')
                        || CASE
                            WHEN source.payload ? 'CustomerId'
                            THEN jsonb_build_object('CustId', source.payload -> 'CustomerId')
                            ELSE '{}'::jsonb
                        END,
                    '{Laps}',
                    COALESCE(
                        (
                            SELECT jsonb_agg(
                                (lap - 'Timed' - 'Valid')
                                    || jsonb_build_object(
                                        'Valid',
                                        COALESCE((lap ->> 'Timed')::boolean, false))
                                ORDER BY ordinal)
                            FROM jsonb_array_elements(COALESCE(source.payload -> 'Laps', '[]'::jsonb))
                                WITH ORDINALITY AS laps(lap, ordinal)
                        ),
                        '[]'::jsonb),
                    true)::text
                FROM source
                WHERE cache."Id" = source."Id";

                UPDATE "iracing"."ExternalDataCaches"
                SET "Payload" = (
                    SELECT COALESCE(
                        jsonb_agg(
                            (item - 'CustomerId' - 'DriverName')
                                || CASE
                                    WHEN item ? 'CustomerId'
                                    THEN jsonb_build_object('CustId', item -> 'CustomerId')
                                    ELSE '{}'::jsonb
                                END
                                || CASE
                                    WHEN item ? 'DriverName'
                                    THEN jsonb_build_object('Driver', item -> 'DriverName')
                                    ELSE '{}'::jsonb
                                END
                            ORDER BY ordinal),
                        '[]'::jsonb)::text
                    FROM jsonb_array_elements("Payload"::jsonb)
                        WITH ORDINALITY AS entries(item, ordinal))
                WHERE "CacheKey" LIKE 'leaderboard:%';

                UPDATE "iracing"."ExternalDataCaches"
                SET "Payload" = (
                    SELECT COALESCE(
                        jsonb_agg(
                            (item - 'CustomerId')
                                || CASE
                                    WHEN item ? 'CustomerId'
                                    THEN jsonb_build_object('CustId', item -> 'CustomerId')
                                    ELSE '{}'::jsonb
                                END
                            ORDER BY ordinal),
                        '[]'::jsonb)::text
                    FROM jsonb_array_elements("Payload"::jsonb)
                        WITH ORDINALITY AS entries(item, ordinal))
                WHERE "CacheKey" LIKE 'standings:%'
                    OR "CacheKey" LIKE 'tt-standings:%'
                    OR "CacheKey" LIKE 'qual:%';

                UPDATE "iracing"."ExternalDataCaches"
                SET "Payload" = (
                    SELECT COALESCE(
                        jsonb_agg(
                            (item - 'CustomerId' - 'DriverName')
                                || CASE
                                    WHEN item ? 'CustomerId'
                                    THEN jsonb_build_object('CustId', item -> 'CustomerId')
                                    ELSE '{}'::jsonb
                                END
                                || CASE
                                    WHEN item ? 'DriverName'
                                    THEN jsonb_build_object('DisplayName', item -> 'DriverName')
                                    ELSE '{}'::jsonb
                                END
                            ORDER BY ordinal),
                        '[]'::jsonb)::text
                    FROM jsonb_array_elements("Payload"::jsonb)
                        WITH ORDINALITY AS entries(item, ordinal))
                WHERE "CacheKey" LIKE 'driversearch:%';
                """);

        }
    }
}
