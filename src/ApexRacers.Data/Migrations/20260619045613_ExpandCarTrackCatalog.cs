using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpandCarTrackCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssetFolder",
                schema: "iracing",
                table: "Tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CornersPerLap",
                schema: "iracing",
                table: "Tracks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasSvgMap",
                schema: "iracing",
                table: "Tracks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LargeImageFile",
                schema: "iracing",
                table: "Tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                schema: "iracing",
                table: "Tracks",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                schema: "iracing",
                table: "Tracks",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NightLighting",
                schema: "iracing",
                table: "Tracks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "NumberPitstalls",
                schema: "iracing",
                table: "Tracks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PitRoadSpeedLimit",
                schema: "iracing",
                table: "Tracks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmallImageFile",
                schema: "iracing",
                table: "Tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackMapUrl",
                schema: "iracing",
                table: "Tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssetFolder",
                schema: "iracing",
                table: "Cars",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarMake",
                schema: "iracing",
                table: "Cars",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarModel",
                schema: "iracing",
                table: "Cars",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarTypesJson",
                schema: "iracing",
                table: "Cars",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoriesJson",
                schema: "iracing",
                table: "Cars",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LargeImageFile",
                schema: "iracing",
                table: "Cars",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoPath",
                schema: "iracing",
                table: "Cars",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RainEnabled",
                schema: "iracing",
                table: "Cars",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SmallImageFile",
                schema: "iracing",
                table: "Cars",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssetFolder",
                schema: "iracing",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "CornersPerLap",
                schema: "iracing",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "HasSvgMap",
                schema: "iracing",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "LargeImageFile",
                schema: "iracing",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "Latitude",
                schema: "iracing",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "Longitude",
                schema: "iracing",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "NightLighting",
                schema: "iracing",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "NumberPitstalls",
                schema: "iracing",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "PitRoadSpeedLimit",
                schema: "iracing",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "SmallImageFile",
                schema: "iracing",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "TrackMapUrl",
                schema: "iracing",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "AssetFolder",
                schema: "iracing",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "CarMake",
                schema: "iracing",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "CarModel",
                schema: "iracing",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "CarTypesJson",
                schema: "iracing",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "CategoriesJson",
                schema: "iracing",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "LargeImageFile",
                schema: "iracing",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "LogoPath",
                schema: "iracing",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "RainEnabled",
                schema: "iracing",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "SmallImageFile",
                schema: "iracing",
                table: "Cars");
        }
    }
}
