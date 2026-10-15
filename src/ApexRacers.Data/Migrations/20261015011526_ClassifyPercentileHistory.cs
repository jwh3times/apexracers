using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClassifyPercentileHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScopedCarPercentileResults_Cars_CarId",
                schema: "iracing",
                table: "ScopedCarPercentileResults");

            migrationBuilder.DropForeignKey(
                name: "FK_ScopedCarPercentileResults_EvidenceCopyMarkers_EvidenceCopy~",
                schema: "iracing",
                table: "ScopedCarPercentileResults");

            migrationBuilder.DropForeignKey(
                name: "FK_ScopedCarPercentileResults_Series_SeriesId",
                schema: "iracing",
                table: "ScopedCarPercentileResults");

            migrationBuilder.DropForeignKey(
                name: "FK_ScopedCarPercentileResults_Users_UserId",
                schema: "iracing",
                table: "ScopedCarPercentileResults");

            migrationBuilder.DropForeignKey(
                name: "FK_ScopedCarPercentileResults_Weeks_WeekId",
                schema: "iracing",
                table: "ScopedCarPercentileResults");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ScopedCarPercentileResults",
                schema: "iracing",
                table: "ScopedCarPercentileResults");

            migrationBuilder.RenameTable(
                name: "ScopedCarPercentileResults",
                schema: "iracing",
                newName: "LegacyUnclassifiedPercentileResults",
                newSchema: "iracing");

            migrationBuilder.RenameIndex(
                name: "IX_ScopedCarPercentileResults_WeekId",
                schema: "iracing",
                table: "LegacyUnclassifiedPercentileResults",
                newName: "IX_LegacyUnclassifiedPercentileResults_WeekId");

            migrationBuilder.RenameIndex(
                name: "IX_ScopedCarPercentileResults_UserId",
                schema: "iracing",
                table: "LegacyUnclassifiedPercentileResults",
                newName: "IX_LegacyUnclassifiedPercentileResults_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_ScopedCarPercentileResults_SeriesId",
                schema: "iracing",
                table: "LegacyUnclassifiedPercentileResults",
                newName: "IX_LegacyUnclassifiedPercentileResults_SeriesId");

            migrationBuilder.RenameIndex(
                name: "IX_ScopedCarPercentileResults_Provenance_UserId_CarId_SeriesId~",
                schema: "iracing",
                table: "LegacyUnclassifiedPercentileResults",
                newName: "IX_LegacyUnclassifiedPercentileResults_Provenance_UserId_CarId~");

            migrationBuilder.RenameIndex(
                name: "IX_ScopedCarPercentileResults_EvidenceCopyId",
                schema: "iracing",
                table: "LegacyUnclassifiedPercentileResults",
                newName: "IX_LegacyUnclassifiedPercentileResults_EvidenceCopyId");

            migrationBuilder.RenameIndex(
                name: "IX_ScopedCarPercentileResults_CarId",
                schema: "iracing",
                table: "LegacyUnclassifiedPercentileResults",
                newName: "IX_LegacyUnclassifiedPercentileResults_CarId");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DriverAuthorizationGrants_Id_UserId_CustomerId_Provenance",
                schema: "iracing",
                table: "DriverAuthorizationGrants",
                columns: new[] { "Id", "UserId", "CustomerId", "Provenance" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_LegacyUnclassifiedPercentileResults",
                schema: "iracing",
                table: "LegacyUnclassifiedPercentileResults",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "SelectionScopedCarPercentileResults",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    Provenance = table.Column<int>(type: "integer", nullable: false),
                    OwnerKind = table.Column<int>(type: "integer", nullable: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantRevision = table.Column<long>(type: "bigint", nullable: true),
                    SelectionPolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    SelectionKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CarId = table.Column<int>(type: "integer", nullable: false),
                    SeriesId = table.Column<int>(type: "integer", nullable: false),
                    WeekId = table.Column<Guid>(type: "uuid", nullable: false),
                    PercentileRank = table.Column<double>(type: "double precision", nullable: false),
                    TopSharePercent = table.Column<int>(type: "integer", nullable: false),
                    SampleSize = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PersonalBestLapSeconds = table.Column<double>(type: "double precision", nullable: false),
                    PersonalBestLapEvidence = table.Column<byte>(type: "smallint", nullable: false),
                    UploadedSessionType = table.Column<byte>(type: "smallint", nullable: true),
                    ReadingRevision = table.Column<long>(type: "bigint", nullable: false),
                    EvidenceCopyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationPurposeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationGeneration = table.Column<long>(type: "bigint", nullable: false),
                    PreparationVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelectionScopedCarPercentileResults", x => x.Id);
                    table.CheckConstraint("CK_ClassifiedPercentile_Owner", "\"Provenance\" = 2 AND \"CustomerId\" > 0 AND ((\"OwnerKind\" = 0 AND \"CustomerId\" = 100001 AND \"GrantId\" IS NULL AND \"GrantRevision\" IS NULL AND \"PersonalBestLapEvidence\" = 1 AND \"UploadedSessionType\" IS NULL) OR (\"OwnerKind\" = 1 AND \"GrantId\" IS NOT NULL AND \"GrantRevision\" IS NOT NULL AND \"GrantRevision\" > 0))");
                    table.CheckConstraint("CK_ClassifiedPercentile_Reading", "\"PersonalBestLapSeconds\" > 0 AND \"PersonalBestLapSeconds\" <= 1.7976931348623157e308 AND \"PercentileRank\" > 0 AND \"PercentileRank\" < 100 AND \"TopSharePercent\" BETWEEN 1 AND 100 AND \"SampleSize\" > 0 AND \"ReadingRevision\" > 0 AND \"PreparationGeneration\" > 0 AND \"PreparationVersion\" >= 0 AND ((\"PersonalBestLapEvidence\" = 1 AND \"UploadedSessionType\" IS NULL) OR (\"PersonalBestLapEvidence\" = 2 AND \"UploadedSessionType\" IS NOT NULL AND \"UploadedSessionType\" BETWEEN 0 AND 5 AND \"SelectionKey\" <> 'v1:0:0'))");
                    table.CheckConstraint("CK_ClassifiedPercentile_Selection", "\"SelectionPolicyVersion\" = 1 AND (\"SelectionKey\" IN ('v1:0:0', 'v1:1:63') OR \"SelectionKey\" IN ('v1:2:1','v1:2:3','v1:2:5','v1:2:7','v1:2:9','v1:2:11','v1:2:13','v1:2:15','v1:2:17','v1:2:19','v1:2:21','v1:2:23','v1:2:25','v1:2:27','v1:2:29','v1:2:31','v1:2:33','v1:2:35','v1:2:37','v1:2:39','v1:2:41','v1:2:43','v1:2:45','v1:2:47','v1:2:49','v1:2:51','v1:2:53','v1:2:55','v1:2:57','v1:2:59','v1:2:61','v1:2:63'))");
                    table.ForeignKey(
                        name: "FK_SelectionScopedCarPercentileResults_Cars_CarId",
                        column: x => x.CarId,
                        principalSchema: "iracing",
                        principalTable: "Cars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SelectionScopedCarPercentileResults_DriverAuthorizationGran~",
                        columns: x => new { x.GrantId, x.UserId, x.CustomerId, x.Provenance },
                        principalSchema: "iracing",
                        principalTable: "DriverAuthorizationGrants",
                        principalColumns: new[] { "Id", "UserId", "CustomerId", "Provenance" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SelectionScopedCarPercentileResults_EvidenceCopyMarkers_Evi~",
                        column: x => x.EvidenceCopyId,
                        principalSchema: "iracing",
                        principalTable: "EvidenceCopyMarkers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SelectionScopedCarPercentileResults_EvidencePurposes_Prepar~",
                        column: x => x.PreparationPurposeId,
                        principalSchema: "iracing",
                        principalTable: "EvidencePurposes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SelectionScopedCarPercentileResults_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalSchema: "iracing",
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SelectionScopedCarPercentileResults_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SelectionScopedCarPercentileResults_Weeks_WeekId",
                        column: x => x.WeekId,
                        principalSchema: "iracing",
                        principalTable: "Weeks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassifiedPercentile_Identity",
                schema: "iracing",
                table: "SelectionScopedCarPercentileResults",
                columns: new[] { "Provenance", "UserId", "OwnerKind", "CustomerId", "SelectionKey", "SeriesId", "CarId", "WeekId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SelectionScopedCarPercentileResults_CarId",
                schema: "iracing",
                table: "SelectionScopedCarPercentileResults",
                column: "CarId");

            migrationBuilder.CreateIndex(
                name: "IX_SelectionScopedCarPercentileResults_EvidenceCopyId",
                schema: "iracing",
                table: "SelectionScopedCarPercentileResults",
                column: "EvidenceCopyId");

            migrationBuilder.CreateIndex(
                name: "IX_SelectionScopedCarPercentileResults_GrantId_UserId_Customer~",
                schema: "iracing",
                table: "SelectionScopedCarPercentileResults",
                columns: new[] { "GrantId", "UserId", "CustomerId", "Provenance" });

            migrationBuilder.CreateIndex(
                name: "IX_SelectionScopedCarPercentileResults_PreparationPurposeId",
                schema: "iracing",
                table: "SelectionScopedCarPercentileResults",
                column: "PreparationPurposeId");

            migrationBuilder.CreateIndex(
                name: "IX_SelectionScopedCarPercentileResults_SeriesId",
                schema: "iracing",
                table: "SelectionScopedCarPercentileResults",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_SelectionScopedCarPercentileResults_UserId",
                schema: "iracing",
                table: "SelectionScopedCarPercentileResults",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SelectionScopedCarPercentileResults_WeekId",
                schema: "iracing",
                table: "SelectionScopedCarPercentileResults",
                column: "WeekId");

            migrationBuilder.AddForeignKey(
                name: "FK_LegacyUnclassifiedPercentileResults_Cars_CarId",
                schema: "iracing",
                table: "LegacyUnclassifiedPercentileResults",
                column: "CarId",
                principalSchema: "iracing",
                principalTable: "Cars",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LegacyUnclassifiedPercentileResults_EvidenceCopyMarkers_Evi~",
                schema: "iracing",
                table: "LegacyUnclassifiedPercentileResults",
                column: "EvidenceCopyId",
                principalSchema: "iracing",
                principalTable: "EvidenceCopyMarkers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LegacyUnclassifiedPercentileResults_Series_SeriesId",
                schema: "iracing",
                table: "LegacyUnclassifiedPercentileResults",
                column: "SeriesId",
                principalSchema: "iracing",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LegacyUnclassifiedPercentileResults_Users_UserId",
                schema: "iracing",
                table: "LegacyUnclassifiedPercentileResults",
                column: "UserId",
                principalSchema: "identity",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LegacyUnclassifiedPercentileResults_Weeks_WeekId",
                schema: "iracing",
                table: "LegacyUnclassifiedPercentileResults",
                column: "WeekId",
                principalSchema: "iracing",
                principalTable: "Weeks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
            migrationBuilder.Sql(PercentileHistoryDatabaseFences.Sql);
        }

        /// <summary>Recover forward: removing the physical fence reopens selection-less writers.</summary>
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Classified history cutover is forward-only; reconcile restored history under current authority.");
    }
}
