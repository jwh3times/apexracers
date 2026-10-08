using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScopedDriverReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrivateDriverFollows",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientGrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetGrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provenance = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    OriginalLossAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReactivateBefore = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemoveBy = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateDriverFollows", x => x.Id);
                    table.CheckConstraint("CK_PrivateFollow_Clocks", "(\"OriginalLossAt\" IS NULL AND \"ReactivateBefore\" IS NULL AND \"RemoveBy\" IS NULL AND \"Active\") OR (\"OriginalLossAt\" IS NOT NULL AND \"ReactivateBefore\" IS NOT NULL AND \"RemoveBy\" IS NOT NULL)");
                    table.CheckConstraint("CK_PrivateFollow_Demo", "\"Provenance\" = 2 AND \"TargetGrantId\" != \"RecipientGrantId\"");
                    table.ForeignKey(
                        name: "FK_PrivateDriverFollows_DriverAuthorizationGrants_RecipientGra~",
                        column: x => x.RecipientGrantId,
                        principalSchema: "iracing",
                        principalTable: "DriverAuthorizationGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateDriverFollows_DriverAuthorizationGrants_TargetGrantId",
                        column: x => x.TargetGrantId,
                        principalSchema: "iracing",
                        principalTable: "DriverAuthorizationGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScopedDriverReferences",
                schema: "iracing",
                columns: table => new
                {
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecipientGrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientRevision = table.Column<long>(type: "bigint", nullable: false),
                    RecipientProofId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetGrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetRevision = table.Column<long>(type: "bigint", nullable: false),
                    TargetProofId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Provenance = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScopedDriverReferences", x => x.TokenHash);
                    table.CheckConstraint("CK_DriverReference_Demo", "\"Provenance\" = 2 AND \"TargetGrantId\" != \"RecipientGrantId\"");
                    table.CheckConstraint("CK_DriverReference_Lifetime", "\"ExpiresAt\" > \"CreatedAt\"");
                    table.CheckConstraint("CK_DriverReference_Purpose", "\"Purpose\" IN (1,2,3)");
                    table.CheckConstraint("CK_DriverReference_Revision", "\"TargetRevision\" > 0 AND \"RecipientRevision\" > 0");
                    table.ForeignKey(
                        name: "FK_ScopedDriverReferences_DriverAuthorizationGrants_RecipientG~",
                        column: x => x.RecipientGrantId,
                        principalSchema: "iracing",
                        principalTable: "DriverAuthorizationGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScopedDriverReferences_DriverAuthorizationGrants_TargetGran~",
                        column: x => x.TargetGrantId,
                        principalSchema: "iracing",
                        principalTable: "DriverAuthorizationGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateDriverFollows_RecipientGrantId_TargetGrantId",
                schema: "iracing",
                table: "PrivateDriverFollows",
                columns: new[] { "RecipientGrantId", "TargetGrantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateDriverFollows_RemoveBy",
                schema: "iracing",
                table: "PrivateDriverFollows",
                column: "RemoveBy");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateDriverFollows_TargetGrantId",
                schema: "iracing",
                table: "PrivateDriverFollows",
                column: "TargetGrantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScopedDriverReferences_ExpiresAt",
                schema: "iracing",
                table: "ScopedDriverReferences",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ScopedDriverReferences_RecipientGrantId",
                schema: "iracing",
                table: "ScopedDriverReferences",
                column: "RecipientGrantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScopedDriverReferences_TargetGrantId",
                schema: "iracing",
                table: "ScopedDriverReferences",
                column: "TargetGrantId");
            migrationBuilder.Sql(DriverReferenceDatabaseFences.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new InvalidOperationException("Scoped Driver enforcement cannot be rolled back; recover forward.");
    }
}
