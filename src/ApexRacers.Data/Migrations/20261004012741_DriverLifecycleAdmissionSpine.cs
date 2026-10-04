using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class DriverLifecycleAdmissionSpine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_IRacingCustomerId",
                schema: "identity",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "IRacingCustomerId",
                schema: "identity",
                table: "Users",
                newName: "ClaimedIRacingCustomerId");

            migrationBuilder.CreateTable(
                name: "DriverProofReceipts",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    Provenance = table.Column<int>(type: "integer", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Authority = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverProofReceipts", x => x.Id);
                    table.UniqueConstraint("AK_DriverProofReceipts_Id_UserId_CustomerId_Provenance", x => new { x.Id, x.UserId, x.CustomerId, x.Provenance });
                    table.CheckConstraint("CK_DriverProof_Provenance", "\"Provenance\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_DriverProofReceipts_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DriverAuthorizationGrants",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    Provenance = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    BindingActive = table.Column<bool>(type: "boolean", nullable: false),
                    ProofReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProofValid = table.Column<bool>(type: "boolean", nullable: false),
                    PersonalConsentVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SharingConsentVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AuthorizedDriverName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PersonalClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SharingClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverAuthorizationGrants", x => x.Id);
                    table.CheckConstraint("CK_DriverGrant_ConsentProof", "\"ProofValid\" OR (\"PersonalConsentVersion\" IS NULL AND \"SharingConsentVersion\" IS NULL AND \"AuthorizedDriverName\" IS NULL)");
                    table.CheckConstraint("CK_DriverGrant_Provenance", "\"Provenance\" IN (1, 2)");
                    table.CheckConstraint("CK_DriverGrant_Revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_DriverGrant_SharingPersonal", "\"SharingConsentVersion\" IS NULL OR \"PersonalConsentVersion\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_DriverAuthorizationGrants_DriverProofReceipts_ProofReceiptI~",
                        columns: x => new { x.ProofReceiptId, x.UserId, x.CustomerId, x.Provenance },
                        principalSchema: "iracing",
                        principalTable: "DriverProofReceipts",
                        principalColumns: new[] { "Id", "UserId", "CustomerId", "Provenance" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DriverLifecycleOperations",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    OriginalLossAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedRevision = table.Column<long>(type: "bigint", nullable: false),
                    PrimaryAppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverLifecycleOperations", x => x.Id);
                    table.UniqueConstraint("AK_DriverLifecycleOperations_Id_GrantId_OriginalLossAt", x => new { x.Id, x.GrantId, x.OriginalLossAt });
                    table.CheckConstraint("CK_DriverOperation_Kind", "\"Kind\" IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_DriverOperation_Revision", "\"AppliedRevision\" > 0");
                    table.ForeignKey(
                        name: "FK_DriverLifecycleOperations_DriverAuthorizationGrants_GrantId",
                        column: x => x.GrantId,
                        principalSchema: "iracing",
                        principalTable: "DriverAuthorizationGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DriverPublicationAdmissions",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Incarnation = table.Column<Guid>(type: "uuid", nullable: false),
                    AdmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TerminalAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverPublicationAdmissions", x => x.Id);
                    table.CheckConstraint("CK_DriverAdmission_Purpose", "\"Purpose\" IN (1, 2)");
                    table.CheckConstraint("CK_DriverAdmission_Revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_DriverPublicationAdmissions_DriverAuthorizationGrants_Grant~",
                        column: x => x.GrantId,
                        principalSchema: "iracing",
                        principalTable: "DriverAuthorizationGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DriverTrackedCopies",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UnavailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverTrackedCopies", x => x.Id);
                    table.CheckConstraint("CK_DriverCopy_Purpose", "\"Purpose\" IN (1, 2)");
                    table.CheckConstraint("CK_DriverCopy_Revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_DriverTrackedCopies_DriverAuthorizationGrants_GrantId",
                        column: x => x.GrantId,
                        principalSchema: "iracing",
                        principalTable: "DriverAuthorizationGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DriverCopyCleanups",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    ThroughRevision = table.Column<long>(type: "bigint", nullable: false),
                    OriginalLossAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedRemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverCopyCleanups", x => x.Id);
                    table.CheckConstraint("CK_DriverCleanup_Deadline", "\"DueAt\" >= \"OriginalLossAt\"");
                    table.CheckConstraint("CK_DriverCleanup_Purpose", "\"Purpose\" IN (1, 2)");
                    table.CheckConstraint("CK_DriverCleanup_ThroughRevision", "\"ThroughRevision\" > 0");
                    table.ForeignKey(
                        name: "FK_DriverCopyCleanups_DriverAuthorizationGrants_GrantId",
                        column: x => x.GrantId,
                        principalSchema: "iracing",
                        principalTable: "DriverAuthorizationGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DriverCopyCleanups_DriverLifecycleOperations_OperationId_Gr~",
                        columns: x => new { x.OperationId, x.GrantId, x.OriginalLossAt },
                        principalSchema: "iracing",
                        principalTable: "DriverLifecycleOperations",
                        principalColumns: new[] { "Id", "GrantId", "OriginalLossAt" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_IRacingCustomerId",
                schema: "identity",
                table: "Users",
                column: "ClaimedIRacingCustomerId",
                unique: true,
                filter: "\"ClaimedIRacingCustomerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DriverAuthorizationGrants_ProofReceiptId_UserId_CustomerId_~",
                schema: "iracing",
                table: "DriverAuthorizationGrants",
                columns: new[] { "ProofReceiptId", "UserId", "CustomerId", "Provenance" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverAuthorizationGrants_Provenance_CustomerId",
                schema: "iracing",
                table: "DriverAuthorizationGrants",
                columns: new[] { "Provenance", "CustomerId" },
                unique: true,
                filter: "\"BindingActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_DriverAuthorizationGrants_Provenance_UserId",
                schema: "iracing",
                table: "DriverAuthorizationGrants",
                columns: new[] { "Provenance", "UserId" },
                unique: true,
                filter: "\"BindingActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_DriverAuthorizationGrants_Provenance_UserId_CustomerId",
                schema: "iracing",
                table: "DriverAuthorizationGrants",
                columns: new[] { "Provenance", "UserId", "CustomerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriverCopyCleanups_GrantId",
                schema: "iracing",
                table: "DriverCopyCleanups",
                column: "GrantId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverCopyCleanups_OperationId_GrantId_OriginalLossAt",
                schema: "iracing",
                table: "DriverCopyCleanups",
                columns: new[] { "OperationId", "GrantId", "OriginalLossAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverCopyCleanups_OperationId_Purpose",
                schema: "iracing",
                table: "DriverCopyCleanups",
                columns: new[] { "OperationId", "Purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriverCopyCleanups_VerifiedRemovedAt_DueAt",
                schema: "iracing",
                table: "DriverCopyCleanups",
                columns: new[] { "VerifiedRemovedAt", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverLifecycleOperations_GrantId_CompletedAt",
                schema: "iracing",
                table: "DriverLifecycleOperations",
                columns: new[] { "GrantId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverProofReceipts_UserId",
                schema: "iracing",
                table: "DriverProofReceipts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverPublicationAdmissions_GrantId_Purpose_TerminalAt",
                schema: "iracing",
                table: "DriverPublicationAdmissions",
                columns: new[] { "GrantId", "Purpose", "TerminalAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverPublicationAdmissions_Incarnation",
                schema: "iracing",
                table: "DriverPublicationAdmissions",
                column: "Incarnation");

            migrationBuilder.CreateIndex(
                name: "IX_DriverTrackedCopies_GrantId_Purpose_UnavailableAt",
                schema: "iracing",
                table: "DriverTrackedCopies",
                columns: new[] { "GrantId", "Purpose", "UnavailableAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Driver lifecycle cutover is forward-only: rollback would discard enforcement history and restore legacy identity writers.");
        }
    }
}
