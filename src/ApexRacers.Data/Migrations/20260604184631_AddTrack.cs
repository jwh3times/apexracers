using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create Tracks first so we can backfill it before dropping the inline columns.
            migrationBuilder.CreateTable(
                name: "Tracks",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConfigName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: true),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TrackConfigLength = table.Column<double>(type: "double precision", nullable: true),
                    IsDirt = table.Column<bool>(type: "boolean", nullable: false),
                    IsOval = table.Column<bool>(type: "boolean", nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Retired = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tracks", x => x.Id);
                });

            // Backfill Tracks from existing Weeks rows while TrackName/ConfigName still exist.
            migrationBuilder.Sql("""
                INSERT INTO iracing."Tracks" ("Id", "Name", "ConfigName", "IsDirt", "IsOval", "Retired")
                SELECT DISTINCT "IracingTrackId", "TrackName", "ConfigName", false, false, false
                FROM iracing."Weeks"
                ON CONFLICT ("Id") DO NOTHING;
                """);

            // Backfill any additional tracks referenced only by PersonalLaps.
            migrationBuilder.Sql("""
                INSERT INTO iracing."Tracks" ("Id", "Name", "ConfigName", "IsDirt", "IsOval", "Retired")
                SELECT DISTINCT "IracingTrackId", "TrackName", "ConfigName", false, false, false
                FROM iracing."PersonalLaps"
                ON CONFLICT ("Id") DO NOTHING;
                """);

            migrationBuilder.DropColumn(
                name: "ConfigName",
                schema: "iracing",
                table: "Weeks");

            migrationBuilder.DropColumn(
                name: "TrackName",
                schema: "iracing",
                table: "Weeks");

            migrationBuilder.DropColumn(
                name: "ConfigName",
                schema: "iracing",
                table: "PersonalLaps");

            migrationBuilder.DropColumn(
                name: "TrackName",
                schema: "iracing",
                table: "PersonalLaps");

            migrationBuilder.RenameColumn(
                name: "IracingTrackId",
                schema: "iracing",
                table: "Weeks",
                newName: "TrackId");

            migrationBuilder.RenameColumn(
                name: "IracingTrackId",
                schema: "iracing",
                table: "PersonalLaps",
                newName: "TrackId");

            migrationBuilder.RenameIndex(
                name: "IX_PersonalLaps_UserId_CarId_IracingTrackId",
                schema: "iracing",
                table: "PersonalLaps",
                newName: "IX_PersonalLaps_UserId_CarId_TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_Weeks_TrackId",
                schema: "iracing",
                table: "Weeks",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalLaps_TrackId",
                schema: "iracing",
                table: "PersonalLaps",
                column: "TrackId");

            migrationBuilder.AddForeignKey(
                name: "FK_PersonalLaps_Tracks_TrackId",
                schema: "iracing",
                table: "PersonalLaps",
                column: "TrackId",
                principalSchema: "iracing",
                principalTable: "Tracks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Weeks_Tracks_TrackId",
                schema: "iracing",
                table: "Weeks",
                column: "TrackId",
                principalSchema: "iracing",
                principalTable: "Tracks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PersonalLaps_Tracks_TrackId",
                schema: "iracing",
                table: "PersonalLaps");

            migrationBuilder.DropForeignKey(
                name: "FK_Weeks_Tracks_TrackId",
                schema: "iracing",
                table: "Weeks");

            migrationBuilder.DropTable(
                name: "Tracks",
                schema: "iracing");

            migrationBuilder.DropIndex(
                name: "IX_Weeks_TrackId",
                schema: "iracing",
                table: "Weeks");

            migrationBuilder.DropIndex(
                name: "IX_PersonalLaps_TrackId",
                schema: "iracing",
                table: "PersonalLaps");

            migrationBuilder.RenameColumn(
                name: "TrackId",
                schema: "iracing",
                table: "Weeks",
                newName: "IracingTrackId");

            migrationBuilder.RenameColumn(
                name: "TrackId",
                schema: "iracing",
                table: "PersonalLaps",
                newName: "IracingTrackId");

            migrationBuilder.RenameIndex(
                name: "IX_PersonalLaps_UserId_CarId_TrackId",
                schema: "iracing",
                table: "PersonalLaps",
                newName: "IX_PersonalLaps_UserId_CarId_IracingTrackId");

            migrationBuilder.AddColumn<string>(
                name: "ConfigName",
                schema: "iracing",
                table: "Weeks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TrackName",
                schema: "iracing",
                table: "Weeks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ConfigName",
                schema: "iracing",
                table: "PersonalLaps",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TrackName",
                schema: "iracing",
                table: "PersonalLaps",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }
    }
}
