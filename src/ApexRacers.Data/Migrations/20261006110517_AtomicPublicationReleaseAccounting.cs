using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class AtomicPublicationReleaseAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublicationReleases",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    ContextHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RepresentationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DependencyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CatalogId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CatalogRevision = table.Column<long>(type: "bigint", nullable: false),
                    Provenance = table.Column<int>(type: "integer", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Incarnation = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DispatchStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TerminalAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProvenUnsent = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationReleases", x => x.Id);
                    table.CheckConstraint("CK_PublicationRelease_Clocks", "(\"DispatchStartedAt\" IS NULL OR \"DispatchStartedAt\" >= \"ReservedAt\") AND (\"TerminalAt\" IS NULL OR \"TerminalAt\" >= \"ReservedAt\") AND (\"TerminalAt\" IS NULL OR \"DispatchStartedAt\" IS NULL OR \"TerminalAt\" >= \"DispatchStartedAt\")");
                    table.CheckConstraint("CK_PublicationRelease_Demo", "\"Provenance\" = 2");
                    table.CheckConstraint("CK_PublicationRelease_Purpose", "\"Purpose\" IN (1,2,3)");
                    table.CheckConstraint("CK_PublicationRelease_Revision", "\"Sequence\" > 0 AND \"CatalogRevision\" > 0");
                    table.CheckConstraint("CK_PublicationRelease_Unsent", "NOT \"ProvenUnsent\" OR (\"TerminalAt\" IS NOT NULL AND \"DispatchStartedAt\" IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "PublicationReleaseDependencies",
                schema: "iracing",
                columns: table => new
                {
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    Provenance = table.Column<int>(type: "integer", nullable: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    RequiredPurpose = table.Column<int>(type: "integer", nullable: true),
                    AuthorityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationReleaseDependencies", x => new { x.ReleaseId, x.UserId, x.CustomerId, x.Provenance });
                    table.CheckConstraint("CK_PublicationDependency_Demo", "\"Provenance\" = 2");
                    table.CheckConstraint("CK_PublicationDependency_Purpose", "\"RequiredPurpose\" IS NULL OR \"RequiredPurpose\" IN (1,2)");
                    table.CheckConstraint("CK_PublicationDependency_Revision", "(\"GrantId\" IS NULL AND \"Revision\" = 0 AND \"RequiredPurpose\" IS NULL) OR (\"GrantId\" IS NOT NULL AND \"Revision\" > 0)");
                    table.ForeignKey(
                        name: "FK_PublicationReleaseDependencies_DriverAuthorizationGrants_Gr~",
                        column: x => x.GrantId,
                        principalSchema: "iracing",
                        principalTable: "DriverAuthorizationGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublicationReleaseDependencies_PublicationReleases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalSchema: "iracing",
                        principalTable: "PublicationReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationReleaseDependencies_GrantId",
                schema: "iracing",
                table: "PublicationReleaseDependencies",
                column: "GrantId");

            migrationBuilder.CreateIndex(
                name: "IX_PublicationReleaseDependencies_UserId_CustomerId_Provenance",
                schema: "iracing",
                table: "PublicationReleaseDependencies",
                columns: new[] { "UserId", "CustomerId", "Provenance" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationReleases_Sequence",
                schema: "iracing",
                table: "PublicationReleases",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicationReleases_TerminalAt_Incarnation",
                schema: "iracing",
                table: "PublicationReleases",
                columns: new[] { "TerminalAt", "Incarnation" });
            migrationBuilder.Sql(PublicationDatabaseFences.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new InvalidOperationException("Publication history and enforcement fences require forward-only recovery.");
        }
    }
}
