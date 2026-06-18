using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubsessionRaceContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CornersPerLap",
                schema: "iracing",
                table: "Subsessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "EventAverageLapSeconds",
                schema: "iracing",
                table: "Subsessions",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "EventBestLapSeconds",
                schema: "iracing",
                table: "Subsessions",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "EventLapsComplete",
                schema: "iracing",
                table: "Subsessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NumCautionLaps",
                schema: "iracing",
                table: "Subsessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NumCautions",
                schema: "iracing",
                table: "Subsessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NumLeadChanges",
                schema: "iracing",
                table: "Subsessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TrackStateJson",
                schema: "iracing",
                table: "Subsessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeatherJson",
                schema: "iracing",
                table: "Subsessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "iracing",
                table: "SubsessionResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NewSubLevel",
                schema: "iracing",
                table: "SubsessionResults",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NewTtRating",
                schema: "iracing",
                table: "SubsessionResults",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OldSubLevel",
                schema: "iracing",
                table: "SubsessionResults",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OldTtRating",
                schema: "iracing",
                table: "SubsessionResults",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "QualLapSeconds",
                schema: "iracing",
                table: "SubsessionResults",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CornersPerLap",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.DropColumn(
                name: "EventAverageLapSeconds",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.DropColumn(
                name: "EventBestLapSeconds",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.DropColumn(
                name: "EventLapsComplete",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.DropColumn(
                name: "NumCautionLaps",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.DropColumn(
                name: "NumCautions",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.DropColumn(
                name: "NumLeadChanges",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.DropColumn(
                name: "TrackStateJson",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.DropColumn(
                name: "WeatherJson",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "iracing",
                table: "SubsessionResults");

            migrationBuilder.DropColumn(
                name: "NewSubLevel",
                schema: "iracing",
                table: "SubsessionResults");

            migrationBuilder.DropColumn(
                name: "NewTtRating",
                schema: "iracing",
                table: "SubsessionResults");

            migrationBuilder.DropColumn(
                name: "OldSubLevel",
                schema: "iracing",
                table: "SubsessionResults");

            migrationBuilder.DropColumn(
                name: "OldTtRating",
                schema: "iracing",
                table: "SubsessionResults");

            migrationBuilder.DropColumn(
                name: "QualLapSeconds",
                schema: "iracing",
                table: "SubsessionResults");
        }
    }
}
