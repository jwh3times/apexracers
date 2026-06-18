using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleWeatherAndBop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WeatherSummaryJson",
                schema: "iracing",
                table: "Weeks",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SeasonCarBops",
                schema: "iracing",
                columns: table => new
                {
                    SeasonId = table.Column<int>(type: "integer", nullable: false),
                    WeekNumber = table.Column<int>(type: "integer", nullable: false),
                    CarId = table.Column<int>(type: "integer", nullable: false),
                    WeightPenaltyKg = table.Column<double>(type: "double precision", nullable: false),
                    PowerAdjustPct = table.Column<double>(type: "double precision", nullable: false),
                    MaxPctFuelFill = table.Column<double>(type: "double precision", nullable: false),
                    MaxDryTireSets = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonCarBops", x => new { x.SeasonId, x.WeekNumber, x.CarId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeasonCarBops_SeasonId_WeekNumber",
                schema: "iracing",
                table: "SeasonCarBops",
                columns: new[] { "SeasonId", "WeekNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeasonCarBops",
                schema: "iracing");

            migrationBuilder.DropColumn(
                name: "WeatherSummaryJson",
                schema: "iracing",
                table: "Weeks");
        }
    }
}
