using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class EvidenceCopyFencing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM iracing."FeatureFlags" WHERE "Key" = 'iracing-demo' AND "IsEnabled")
                        OR EXISTS (SELECT 1 FROM iracing."MappedDataCaches" WHERE "Provenance" = 2)
                        OR EXISTS (SELECT 1 FROM iracing."RaceEvidenceSubsessions" WHERE "Provenance" = 2)
                        OR EXISTS (SELECT 1 FROM iracing."RaceEvidenceResults" WHERE "Provenance" = 2)
                        OR EXISTS (SELECT 1 FROM iracing."ScopedRivals" WHERE "Provenance" = 2)
                        OR EXISTS (SELECT 1 FROM iracing."ScopedCarPercentileResults" WHERE "Provenance" = 2)
                        OR EXISTS (SELECT 1 FROM iracing."ScopedSeasonCarBops" WHERE "Provenance" = 2)
                        OR EXISTS (SELECT 1 FROM iracing."Weeks" WHERE "DemoWeatherSummaryJson" IS NOT NULL)
                    THEN RAISE EXCEPTION 'Disable and physically tear down Demo before copy-fence migration'; END IF;
                END $$;
                """);
            migrationBuilder.AddColumn<Guid>(
                name: "DemoWeatherEvidenceCopyId",
                schema: "iracing",
                table: "Weeks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WeatherEvidenceCopyId",
                schema: "iracing",
                table: "Weeks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceCopyId",
                schema: "iracing",
                table: "ScopedSeasonCarBops",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceCopyId",
                schema: "iracing",
                table: "ScopedRivals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceCopyId",
                schema: "iracing",
                table: "ScopedCarPercentileResults",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceCopyId",
                schema: "iracing",
                table: "RaceEvidenceSubsessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceCopyId",
                schema: "iracing",
                table: "RaceEvidenceResults",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceCopyId",
                schema: "iracing",
                table: "MappedDataCaches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EvidencePurposes",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provenance = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SeasonId = table.Column<int>(type: "integer", nullable: true),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantRevision = table.Column<long>(type: "bigint", nullable: true),
                    PublicationAdmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    HistoricalRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    HistoricalRequestEndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    EvidenceVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OriginalEndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidencePurposes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EvidenceCopyMarkers",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurposeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    Provenance = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    OriginalAcquiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UnavailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemovalDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerifiedRemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceCopyMarkers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvidenceCopyMarkers_EvidencePurposes_PurposeId",
                        column: x => x.PurposeId,
                        principalSchema: "iracing",
                        principalTable: "EvidencePurposes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuthorizedDriverNameCopies",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provenance = table.Column<int>(type: "integer", nullable: false),
                    EvidenceCopyId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizedDriverNameCopies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthorizedDriverNameCopies_EvidenceCopyMarkers_EvidenceCopy~",
                        column: x => x.EvidenceCopyId,
                        principalSchema: "iracing",
                        principalTable: "EvidenceCopyMarkers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvidenceCopyDependencies",
                schema: "iracing",
                columns: table => new
                {
                    CopyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceCopyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceCopyDependencies", x => new { x.CopyId, x.SourceCopyId });
                    table.ForeignKey(
                        name: "FK_EvidenceCopyDependencies_EvidenceCopyMarkers_CopyId",
                        column: x => x.CopyId,
                        principalSchema: "iracing",
                        principalTable: "EvidenceCopyMarkers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvidenceCopyDependencies_EvidenceCopyMarkers_SourceCopyId",
                        column: x => x.SourceCopyId,
                        principalSchema: "iracing",
                        principalTable: "EvidenceCopyMarkers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Weeks_DemoWeatherEvidenceCopyId",
                schema: "iracing",
                table: "Weeks",
                column: "DemoWeatherEvidenceCopyId");

            migrationBuilder.CreateIndex(
                name: "IX_Weeks_WeatherEvidenceCopyId",
                schema: "iracing",
                table: "Weeks",
                column: "WeatherEvidenceCopyId");

            migrationBuilder.CreateIndex(
                name: "IX_ScopedSeasonCarBops_EvidenceCopyId",
                schema: "iracing",
                table: "ScopedSeasonCarBops",
                column: "EvidenceCopyId");

            migrationBuilder.CreateIndex(
                name: "IX_ScopedRivals_EvidenceCopyId",
                schema: "iracing",
                table: "ScopedRivals",
                column: "EvidenceCopyId");

            migrationBuilder.CreateIndex(
                name: "IX_ScopedCarPercentileResults_EvidenceCopyId",
                schema: "iracing",
                table: "ScopedCarPercentileResults",
                column: "EvidenceCopyId");

            migrationBuilder.CreateIndex(
                name: "IX_RaceEvidenceSubsessions_EvidenceCopyId",
                schema: "iracing",
                table: "RaceEvidenceSubsessions",
                column: "EvidenceCopyId");

            migrationBuilder.CreateIndex(
                name: "IX_RaceEvidenceResults_EvidenceCopyId",
                schema: "iracing",
                table: "RaceEvidenceResults",
                column: "EvidenceCopyId");

            migrationBuilder.CreateIndex(
                name: "IX_MappedDataCaches_EvidenceCopyId",
                schema: "iracing",
                table: "MappedDataCaches",
                column: "EvidenceCopyId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizedDriverNameCopies_EvidenceCopyId",
                schema: "iracing",
                table: "AuthorizedDriverNameCopies",
                column: "EvidenceCopyId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizedDriverNameCopies_GrantId",
                schema: "iracing",
                table: "AuthorizedDriverNameCopies",
                column: "GrantId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceCopyDependencies_SourceCopyId",
                schema: "iracing",
                table: "EvidenceCopyDependencies",
                column: "SourceCopyId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceCopyMarkers_PurposeId_Kind_KeyHash_Version",
                schema: "iracing",
                table: "EvidenceCopyMarkers",
                columns: new[] { "PurposeId", "Kind", "KeyHash", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceCopyMarkers_VerifiedRemovedAt_RemovalDueAt",
                schema: "iracing",
                table: "EvidenceCopyMarkers",
                columns: new[] { "VerifiedRemovedAt", "RemovalDueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidencePurposes_Provenance_Kind_SeasonId",
                schema: "iracing",
                table: "EvidencePurposes",
                columns: new[] { "Provenance", "Kind", "SeasonId" });

            migrationBuilder.AddForeignKey(
                name: "FK_MappedDataCaches_EvidenceCopyMarkers_EvidenceCopyId",
                schema: "iracing",
                table: "MappedDataCaches",
                column: "EvidenceCopyId",
                principalSchema: "iracing",
                principalTable: "EvidenceCopyMarkers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RaceEvidenceResults_EvidenceCopyMarkers_EvidenceCopyId",
                schema: "iracing",
                table: "RaceEvidenceResults",
                column: "EvidenceCopyId",
                principalSchema: "iracing",
                principalTable: "EvidenceCopyMarkers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RaceEvidenceSubsessions_EvidenceCopyMarkers_EvidenceCopyId",
                schema: "iracing",
                table: "RaceEvidenceSubsessions",
                column: "EvidenceCopyId",
                principalSchema: "iracing",
                principalTable: "EvidenceCopyMarkers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ScopedCarPercentileResults_EvidenceCopyMarkers_EvidenceCopy~",
                schema: "iracing",
                table: "ScopedCarPercentileResults",
                column: "EvidenceCopyId",
                principalSchema: "iracing",
                principalTable: "EvidenceCopyMarkers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ScopedRivals_EvidenceCopyMarkers_EvidenceCopyId",
                schema: "iracing",
                table: "ScopedRivals",
                column: "EvidenceCopyId",
                principalSchema: "iracing",
                principalTable: "EvidenceCopyMarkers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ScopedSeasonCarBops_EvidenceCopyMarkers_EvidenceCopyId",
                schema: "iracing",
                table: "ScopedSeasonCarBops",
                column: "EvidenceCopyId",
                principalSchema: "iracing",
                principalTable: "EvidenceCopyMarkers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Weeks_EvidenceCopyMarkers_DemoWeatherEvidenceCopyId",
                schema: "iracing",
                table: "Weeks",
                column: "DemoWeatherEvidenceCopyId",
                principalSchema: "iracing",
                principalTable: "EvidenceCopyMarkers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Weeks_EvidenceCopyMarkers_WeatherEvidenceCopyId",
                schema: "iracing",
                table: "Weeks",
                column: "WeatherEvidenceCopyId",
                principalSchema: "iracing",
                principalTable: "EvidenceCopyMarkers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(EvidenceCopyDatabaseFences.Sql);
        }

        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Copy fences require forward recovery; an old writer cannot be restored.");
    }
}
