using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <summary>
    /// Removes the vacuous validity flag from Uploaded Laps. Every persisted Uploaded Lap is a
    /// Timed Lap: untimed telemetry laps are rejected before persistence, so the column carries no
    /// information and every historical value is <see langword="true"/>.
    /// </summary>
    public partial class RemoveUploadedLapIsValidLap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsValidLap",
                schema: "iracing",
                table: "UploadedLaps");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsValidLap",
                schema: "iracing",
                table: "UploadedLaps",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }
    }
}
