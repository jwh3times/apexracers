using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class IsolateDemoProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CarPercentileResults_Cars_CarId",
                schema: "iracing",
                table: "CarPercentileResults");

            migrationBuilder.DropForeignKey(
                name: "FK_CarPercentileResults_Series_SeriesId",
                schema: "iracing",
                table: "CarPercentileResults");

            migrationBuilder.DropForeignKey(
                name: "FK_CarPercentileResults_Users_UserId",
                schema: "iracing",
                table: "CarPercentileResults");

            migrationBuilder.DropForeignKey(
                name: "FK_CarPercentileResults_Weeks_WeekId",
                schema: "iracing",
                table: "CarPercentileResults");

            migrationBuilder.DropForeignKey(
                name: "FK_Rivals_Users_UserId",
                schema: "iracing",
                table: "Rivals");

            migrationBuilder.DropForeignKey(
                name: "FK_SubsessionResults_CarClasses_CarClassId",
                schema: "iracing",
                table: "SubsessionResults");

            migrationBuilder.DropForeignKey(
                name: "FK_SubsessionResults_Cars_CarId",
                schema: "iracing",
                table: "SubsessionResults");

            migrationBuilder.DropForeignKey(
                name: "FK_SubsessionResults_Subsessions_SubsessionId",
                schema: "iracing",
                table: "SubsessionResults");

            migrationBuilder.DropForeignKey(
                name: "FK_Subsessions_Seasons_SeasonId",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.DropForeignKey(
                name: "FK_Subsessions_Tracks_TrackId",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.DropForeignKey(
                name: "FK_Subsessions_Weeks_WeekId",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Subsessions",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SubsessionResults",
                schema: "iracing",
                table: "SubsessionResults");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SeasonCarBops",
                schema: "iracing",
                table: "SeasonCarBops");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Rivals",
                schema: "iracing",
                table: "Rivals");

            migrationBuilder.DropIndex(
                name: "IX_Rivals_UserId_RivalCustId",
                schema: "iracing",
                table: "Rivals");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ExternalDataCaches",
                schema: "iracing",
                table: "ExternalDataCaches");

            migrationBuilder.DropIndex(
                name: "IX_ExternalDataCaches_CacheKey",
                schema: "iracing",
                table: "ExternalDataCaches");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CarPercentileResults",
                schema: "iracing",
                table: "CarPercentileResults");

            migrationBuilder.DropIndex(
                name: "IX_CarPercentileResults_UserId_CarId_SeriesId_WeekId",
                schema: "iracing",
                table: "CarPercentileResults");

            migrationBuilder.RenameTable(
                name: "Subsessions",
                schema: "iracing",
                newName: "RaceEvidenceSubsessions",
                newSchema: "iracing");

            migrationBuilder.RenameTable(
                name: "SubsessionResults",
                schema: "iracing",
                newName: "RaceEvidenceResults",
                newSchema: "iracing");

            migrationBuilder.RenameTable(
                name: "SeasonCarBops",
                schema: "iracing",
                newName: "ScopedSeasonCarBops",
                newSchema: "iracing");

            migrationBuilder.RenameTable(
                name: "Rivals",
                schema: "iracing",
                newName: "ScopedRivals",
                newSchema: "iracing");

            migrationBuilder.RenameTable(
                name: "ExternalDataCaches",
                schema: "iracing",
                newName: "MappedDataCaches",
                newSchema: "iracing");

            migrationBuilder.RenameTable(
                name: "CarPercentileResults",
                schema: "iracing",
                newName: "ScopedCarPercentileResults",
                newSchema: "iracing");

            migrationBuilder.RenameColumn(
                name: "WeatherSummaryJson",
                schema: "iracing",
                table: "Weeks",
                newName: "RealWeatherSummaryJson");

            migrationBuilder.RenameIndex(
                name: "IX_Subsessions_WeekId",
                schema: "iracing",
                table: "RaceEvidenceSubsessions",
                newName: "IX_RaceEvidenceSubsessions_WeekId");

            migrationBuilder.RenameIndex(
                name: "IX_Subsessions_TrackId",
                schema: "iracing",
                table: "RaceEvidenceSubsessions",
                newName: "IX_RaceEvidenceSubsessions_TrackId");

            migrationBuilder.RenameIndex(
                name: "IX_Subsessions_SeasonId_RaceWeekIndex",
                schema: "iracing",
                table: "RaceEvidenceSubsessions",
                newName: "IX_RaceEvidenceSubsessions_SeasonId_RaceWeekIndex");

            migrationBuilder.RenameIndex(
                name: "IX_Subsessions_RaceSessionId",
                schema: "iracing",
                table: "RaceEvidenceSubsessions",
                newName: "IX_RaceEvidenceSubsessions_RaceSessionId");

            migrationBuilder.RenameIndex(
                name: "IX_SubsessionResults_CustId",
                schema: "iracing",
                table: "RaceEvidenceResults",
                newName: "IX_RaceEvidenceResults_CustId");

            migrationBuilder.RenameIndex(
                name: "IX_SubsessionResults_CarId_SubsessionId",
                schema: "iracing",
                table: "RaceEvidenceResults",
                newName: "IX_RaceEvidenceResults_CarId_SubsessionId");

            migrationBuilder.RenameIndex(
                name: "IX_SubsessionResults_CarClassId",
                schema: "iracing",
                table: "RaceEvidenceResults",
                newName: "IX_RaceEvidenceResults_CarClassId");

            migrationBuilder.RenameIndex(
                name: "IX_SeasonCarBops_SeasonId_RaceWeekIndex",
                schema: "iracing",
                table: "ScopedSeasonCarBops",
                newName: "IX_ScopedSeasonCarBops_SeasonId_RaceWeekIndex");

            migrationBuilder.RenameIndex(
                name: "IX_CarPercentileResults_WeekId",
                schema: "iracing",
                table: "ScopedCarPercentileResults",
                newName: "IX_ScopedCarPercentileResults_WeekId");

            migrationBuilder.RenameIndex(
                name: "IX_CarPercentileResults_SeriesId",
                schema: "iracing",
                table: "ScopedCarPercentileResults",
                newName: "IX_ScopedCarPercentileResults_SeriesId");

            migrationBuilder.RenameIndex(
                name: "IX_CarPercentileResults_CarId",
                schema: "iracing",
                table: "ScopedCarPercentileResults",
                newName: "IX_ScopedCarPercentileResults_CarId");

            migrationBuilder.AddColumn<string>(
                name: "DemoWeatherSummaryJson",
                schema: "iracing",
                table: "Weeks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeatherProvenance",
                schema: "iracing",
                table: "Weeks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Provenance",
                schema: "iracing",
                table: "RaceEvidenceSubsessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Provenance",
                schema: "iracing",
                table: "RaceEvidenceResults",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Provenance",
                schema: "iracing",
                table: "ScopedSeasonCarBops",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Provenance",
                schema: "iracing",
                table: "ScopedRivals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Provenance",
                schema: "iracing",
                table: "MappedDataCaches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Provenance",
                schema: "iracing",
                table: "ScopedCarPercentileResults",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_RaceEvidenceSubsessions",
                schema: "iracing",
                table: "RaceEvidenceSubsessions",
                columns: new[] { "Provenance", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_RaceEvidenceResults",
                schema: "iracing",
                table: "RaceEvidenceResults",
                columns: new[] { "Provenance", "SubsessionId", "CustId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_ScopedSeasonCarBops",
                schema: "iracing",
                table: "ScopedSeasonCarBops",
                columns: new[] { "Provenance", "SeasonId", "RaceWeekIndex", "CarId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_ScopedRivals",
                schema: "iracing",
                table: "ScopedRivals",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MappedDataCaches",
                schema: "iracing",
                table: "MappedDataCaches",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ScopedCarPercentileResults",
                schema: "iracing",
                table: "ScopedCarPercentileResults",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "ProvenanceMigrationInventory",
                schema: "iracing",
                columns: table => new
                {
                    StorageKind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    UnknownRows = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvenanceMigrationInventory", x => x.StorageKind);
                });

            migrationBuilder.CreateTable(
                name: "QuarantinedDataCaches",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CacheKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuarantinedDataCaches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScopedRivals_Provenance_UserId_RivalCustId",
                schema: "iracing",
                table: "ScopedRivals",
                columns: new[] { "Provenance", "UserId", "RivalCustId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScopedRivals_UserId",
                schema: "iracing",
                table: "ScopedRivals",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MappedDataCaches_Provenance_CacheKey",
                schema: "iracing",
                table: "MappedDataCaches",
                columns: new[] { "Provenance", "CacheKey" },
                unique: true);

            // All pre-cutover cache rows are ambiguous, including apparent Demo IDs and
            // far-future expiry. Preserve their original clocks in quarantine, never relabel.
            migrationBuilder.Sql("""
                INSERT INTO iracing."QuarantinedDataCaches"
                    ("Id", "CacheKey", "Payload", "FetchedAt", "ExpiresAt")
                SELECT "Id", "CacheKey", "Payload", "FetchedAt", "ExpiresAt"
                FROM iracing."MappedDataCaches" WHERE "Provenance" = 0;
                DELETE FROM iracing."MappedDataCaches" WHERE "Provenance" = 0;
                INSERT INTO iracing."ProvenanceMigrationInventory" ("StorageKind", "UnknownRows", "RecordedAt")
                SELECT 'mapped-cache', COUNT(*), CURRENT_TIMESTAMP FROM iracing."QuarantinedDataCaches"
                UNION ALL SELECT 'subsession', COUNT(*), CURRENT_TIMESTAMP FROM iracing."RaceEvidenceSubsessions"
                UNION ALL SELECT 'race-result', COUNT(*), CURRENT_TIMESTAMP FROM iracing."RaceEvidenceResults"
                UNION ALL SELECT 'percentile', COUNT(*), CURRENT_TIMESTAMP FROM iracing."ScopedCarPercentileResults"
                UNION ALL SELECT 'follow', COUNT(*), CURRENT_TIMESTAMP FROM iracing."ScopedRivals"
                UNION ALL SELECT 'bop', COUNT(*), CURRENT_TIMESTAMP FROM iracing."ScopedSeasonCarBops"
                UNION ALL SELECT 'weather', COUNT(*), CURRENT_TIMESTAMP FROM iracing."Weeks"
                    WHERE "RealWeatherSummaryJson" IS NOT NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MappedDataCaches_KnownProvenance",
                schema: "iracing",
                table: "MappedDataCaches",
                sql: "\"Provenance\" IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_ScopedCarPercentileResults_Provenance_UserId_CarId_SeriesId~",
                schema: "iracing",
                table: "ScopedCarPercentileResults",
                columns: new[] { "Provenance", "UserId", "CarId", "SeriesId", "WeekId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScopedCarPercentileResults_UserId",
                schema: "iracing",
                table: "ScopedCarPercentileResults",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_QuarantinedDataCaches_CacheKey",
                schema: "iracing",
                table: "QuarantinedDataCaches",
                column: "CacheKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RaceEvidenceResults_CarClasses_CarClassId",
                schema: "iracing",
                table: "RaceEvidenceResults",
                column: "CarClassId",
                principalSchema: "iracing",
                principalTable: "CarClasses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RaceEvidenceResults_Cars_CarId",
                schema: "iracing",
                table: "RaceEvidenceResults",
                column: "CarId",
                principalSchema: "iracing",
                principalTable: "Cars",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RaceEvidenceResults_RaceEvidenceSubsessions_Provenance_Subs~",
                schema: "iracing",
                table: "RaceEvidenceResults",
                columns: new[] { "Provenance", "SubsessionId" },
                principalSchema: "iracing",
                principalTable: "RaceEvidenceSubsessions",
                principalColumns: new[] { "Provenance", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RaceEvidenceSubsessions_Seasons_SeasonId",
                schema: "iracing",
                table: "RaceEvidenceSubsessions",
                column: "SeasonId",
                principalSchema: "iracing",
                principalTable: "Seasons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RaceEvidenceSubsessions_Tracks_TrackId",
                schema: "iracing",
                table: "RaceEvidenceSubsessions",
                column: "TrackId",
                principalSchema: "iracing",
                principalTable: "Tracks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RaceEvidenceSubsessions_Weeks_WeekId",
                schema: "iracing",
                table: "RaceEvidenceSubsessions",
                column: "WeekId",
                principalSchema: "iracing",
                principalTable: "Weeks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ScopedCarPercentileResults_Cars_CarId",
                schema: "iracing",
                table: "ScopedCarPercentileResults",
                column: "CarId",
                principalSchema: "iracing",
                principalTable: "Cars",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ScopedCarPercentileResults_Series_SeriesId",
                schema: "iracing",
                table: "ScopedCarPercentileResults",
                column: "SeriesId",
                principalSchema: "iracing",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ScopedCarPercentileResults_Users_UserId",
                schema: "iracing",
                table: "ScopedCarPercentileResults",
                column: "UserId",
                principalSchema: "identity",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ScopedCarPercentileResults_Weeks_WeekId",
                schema: "iracing",
                table: "ScopedCarPercentileResults",
                column: "WeekId",
                principalSchema: "iracing",
                principalTable: "Weeks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ScopedRivals_Users_UserId",
                schema: "iracing",
                table: "ScopedRivals",
                column: "UserId",
                principalSchema: "identity",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Provenance cutover requires forward recovery; rollback cannot remove namespace or legacy-writer fences.");
    }
}