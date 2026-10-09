using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class FenceLegacyUploadedLapReaders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UploadedLaps_Cars_CarId",
                schema: "iracing",
                table: "UploadedLaps");

            migrationBuilder.DropForeignKey(
                name: "FK_UploadedLaps_Tracks_TrackId",
                schema: "iracing",
                table: "UploadedLaps");

            migrationBuilder.DropForeignKey(
                name: "FK_UploadedLaps_Users_UserId",
                schema: "iracing",
                table: "UploadedLaps");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UploadedLaps",
                schema: "iracing",
                table: "UploadedLaps");

            migrationBuilder.RenameTable(
                name: "UploadedLaps",
                schema: "iracing",
                newName: "QuarantinedUploadedLaps",
                newSchema: "iracing");

            migrationBuilder.RenameIndex(
                name: "IX_UploadedLaps_UserId_CarId_TrackId",
                schema: "iracing",
                table: "QuarantinedUploadedLaps",
                newName: "IX_QuarantinedUploadedLaps_UserId_CarId_TrackId");

            migrationBuilder.RenameIndex(
                name: "IX_UploadedLaps_TrackId",
                schema: "iracing",
                table: "QuarantinedUploadedLaps",
                newName: "IX_QuarantinedUploadedLaps_TrackId");

            migrationBuilder.RenameIndex(
                name: "IX_UploadedLaps_CarId",
                schema: "iracing",
                table: "QuarantinedUploadedLaps",
                newName: "IX_QuarantinedUploadedLaps_CarId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_QuarantinedUploadedLaps",
                schema: "iracing",
                table: "QuarantinedUploadedLaps",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_QuarantinedUploadedLaps_Cars_CarId",
                schema: "iracing",
                table: "QuarantinedUploadedLaps",
                column: "CarId",
                principalSchema: "iracing",
                principalTable: "Cars",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_QuarantinedUploadedLaps_Tracks_TrackId",
                schema: "iracing",
                table: "QuarantinedUploadedLaps",
                column: "TrackId",
                principalSchema: "iracing",
                principalTable: "Tracks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_QuarantinedUploadedLaps_Users_UserId",
                schema: "iracing",
                table: "QuarantinedUploadedLaps",
                column: "UserId",
                principalSchema: "identity",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
            migrationBuilder.Sql("""
                INSERT INTO iracing."ProvenanceMigrationInventory" ("StorageKind", "UnknownRows", "RecordedAt")
                SELECT 'uploaded-lap', COUNT(*), CURRENT_TIMESTAMP FROM iracing."QuarantinedUploadedLaps";
                """);
        }

        /// <summary>Recover forward: restoring the old table name reopens insecure binaries.</summary>
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("The legacy upload reader fence is forward-only; reconcile restored data with current enforcement.");
    }
}
