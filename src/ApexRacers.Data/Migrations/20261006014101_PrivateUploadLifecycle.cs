using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrivateUploadLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrivateUploadSessions",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    Provenance = table.Column<int>(type: "integer", nullable: false),
                    EvidenceCopyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarId = table.Column<int>(type: "integer", nullable: false),
                    TrackId = table.Column<int>(type: "integer", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SessionType = table.Column<byte>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateUploadSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrivateUploadSessions_Cars_CarId",
                        column: x => x.CarId,
                        principalSchema: "iracing",
                        principalTable: "Cars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateUploadSessions_EvidenceCopyMarkers_EvidenceCopyId",
                        column: x => x.EvidenceCopyId,
                        principalSchema: "iracing",
                        principalTable: "EvidenceCopyMarkers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateUploadSessions_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalSchema: "iracing",
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrivateUploadedLaps",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LapNumber = table.Column<int>(type: "integer", nullable: false),
                    LapTimeSeconds = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateUploadedLaps", x => x.Id);
                    table.CheckConstraint("CK_PrivateUploadedLap_Timed", "\"LapTimeSeconds\" > 0 AND \"LapTimeSeconds\" <= 1.7976931348623157E308");
                    table.ForeignKey(
                        name: "FK_PrivateUploadedLaps_PrivateUploadSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "iracing",
                        principalTable: "PrivateUploadSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateUploadedLaps_SessionId_LapNumber",
                schema: "iracing",
                table: "PrivateUploadedLaps",
                columns: new[] { "SessionId", "LapNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateUploadSessions_CarId",
                schema: "iracing",
                table: "PrivateUploadSessions",
                column: "CarId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateUploadSessions_EvidenceCopyId",
                schema: "iracing",
                table: "PrivateUploadSessions",
                column: "EvidenceCopyId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateUploadSessions_Provenance_UserId_CustomerId_CarId_Tr~",
                schema: "iracing",
                table: "PrivateUploadSessions",
                columns: new[] { "Provenance", "UserId", "CustomerId", "CarId", "TrackId", "RecordedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateUploadSessions_TrackId",
                schema: "iracing",
                table: "PrivateUploadSessions",
                column: "TrackId");
            migrationBuilder.Sql(PrivateUploadDatabaseFences.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Private upload fences and enforcement history require forward recovery.");
        }
    }
}
