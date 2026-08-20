using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <summary>
    /// Renames the <c>PersonalLaps</c> table to <c>UploadedLaps</c>, following the entity rename.
    /// </summary>
    /// <remarks>
    /// <para><b>Hand-written on purpose.</b> Scaffolding this produced a <c>DropTable</c> followed
    /// by a <c>CreateTable</c> — EF sees an unfamiliar table name and a new one, not a rename — and
    /// warned that it "may result in the loss of data." It would have deleted every Uploaded Lap
    /// any driver had ever submitted. These rows come from telemetry files that live on the
    /// driver's own machine and are not re-fetchable from iRacing, so the loss would have been
    /// permanent.</para>
    ///
    /// <para>Renaming preserves every row. The column set is untouched — only the table, its
    /// indexes, and its constraint names move.</para>
    ///
    /// <para><b>Why the constraints are renamed explicitly.</b> PostgreSQL's
    /// <c>ALTER TABLE … RENAME TO</c> does not rename the table's constraints, so the primary key
    /// would have stayed <c>PK_PersonalLaps</c> on a table called <c>UploadedLaps</c>. That
    /// mismatch is invisible until some later migration addresses a constraint by the name EF
    /// expects and fails to find it. Renaming the primary key also renames its backing index, so
    /// that one is deliberately absent from the <c>RenameIndex</c> calls below.</para>
    /// </remarks>
    public partial class RenamePersonalLapToUploadedLap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "PersonalLaps",
                schema: "iracing",
                newName: "UploadedLaps",
                newSchema: "iracing");

            migrationBuilder.RenameIndex(
                name: "IX_PersonalLaps_CarId",
                schema: "iracing",
                table: "UploadedLaps",
                newName: "IX_UploadedLaps_CarId");

            migrationBuilder.RenameIndex(
                name: "IX_PersonalLaps_TrackId",
                schema: "iracing",
                table: "UploadedLaps",
                newName: "IX_UploadedLaps_TrackId");

            migrationBuilder.RenameIndex(
                name: "IX_PersonalLaps_UserId_CarId_TrackId",
                schema: "iracing",
                table: "UploadedLaps",
                newName: "IX_UploadedLaps_UserId_CarId_TrackId");

            migrationBuilder.Sql("""
                ALTER TABLE "iracing"."UploadedLaps"
                    RENAME CONSTRAINT "PK_PersonalLaps" TO "PK_UploadedLaps";
                ALTER TABLE "iracing"."UploadedLaps"
                    RENAME CONSTRAINT "FK_PersonalLaps_Cars_CarId" TO "FK_UploadedLaps_Cars_CarId";
                ALTER TABLE "iracing"."UploadedLaps"
                    RENAME CONSTRAINT "FK_PersonalLaps_Tracks_TrackId" TO "FK_UploadedLaps_Tracks_TrackId";
                ALTER TABLE "iracing"."UploadedLaps"
                    RENAME CONSTRAINT "FK_PersonalLaps_Users_UserId" TO "FK_UploadedLaps_Users_UserId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "iracing"."UploadedLaps"
                    RENAME CONSTRAINT "PK_UploadedLaps" TO "PK_PersonalLaps";
                ALTER TABLE "iracing"."UploadedLaps"
                    RENAME CONSTRAINT "FK_UploadedLaps_Cars_CarId" TO "FK_PersonalLaps_Cars_CarId";
                ALTER TABLE "iracing"."UploadedLaps"
                    RENAME CONSTRAINT "FK_UploadedLaps_Tracks_TrackId" TO "FK_PersonalLaps_Tracks_TrackId";
                ALTER TABLE "iracing"."UploadedLaps"
                    RENAME CONSTRAINT "FK_UploadedLaps_Users_UserId" TO "FK_PersonalLaps_Users_UserId";
                """);

            migrationBuilder.RenameIndex(
                name: "IX_UploadedLaps_UserId_CarId_TrackId",
                schema: "iracing",
                table: "UploadedLaps",
                newName: "IX_PersonalLaps_UserId_CarId_TrackId");

            migrationBuilder.RenameIndex(
                name: "IX_UploadedLaps_TrackId",
                schema: "iracing",
                table: "UploadedLaps",
                newName: "IX_PersonalLaps_TrackId");

            migrationBuilder.RenameIndex(
                name: "IX_UploadedLaps_CarId",
                schema: "iracing",
                table: "UploadedLaps",
                newName: "IX_PersonalLaps_CarId");

            migrationBuilder.RenameTable(
                name: "UploadedLaps",
                schema: "iracing",
                newName: "PersonalLaps",
                newSchema: "iracing");
        }
    }
}
