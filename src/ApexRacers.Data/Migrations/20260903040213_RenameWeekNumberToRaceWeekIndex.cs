using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <summary>
    /// Renames the three persisted zero-based Race Week columns without changing their values.
    /// </summary>
    public partial class RenameWeekNumberToRaceWeekIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "WeekNumber",
                schema: "iracing",
                table: "Weeks",
                newName: "RaceWeekIndex");

            migrationBuilder.RenameIndex(
                name: "IX_Weeks_SeasonId_WeekNumber",
                schema: "iracing",
                table: "Weeks",
                newName: "IX_Weeks_SeasonId_RaceWeekIndex");

            migrationBuilder.RenameColumn(
                name: "WeekNumber",
                schema: "iracing",
                table: "Subsessions",
                newName: "RaceWeekIndex");

            migrationBuilder.RenameIndex(
                name: "IX_Subsessions_SeasonId_WeekNumber",
                schema: "iracing",
                table: "Subsessions",
                newName: "IX_Subsessions_SeasonId_RaceWeekIndex");

            migrationBuilder.RenameColumn(
                name: "WeekNumber",
                schema: "iracing",
                table: "SeasonCarBops",
                newName: "RaceWeekIndex");

            migrationBuilder.RenameIndex(
                name: "IX_SeasonCarBops_SeasonId_WeekNumber",
                schema: "iracing",
                table: "SeasonCarBops",
                newName: "IX_SeasonCarBops_SeasonId_RaceWeekIndex");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RaceWeekIndex",
                schema: "iracing",
                table: "Weeks",
                newName: "WeekNumber");

            migrationBuilder.RenameIndex(
                name: "IX_Weeks_SeasonId_RaceWeekIndex",
                schema: "iracing",
                table: "Weeks",
                newName: "IX_Weeks_SeasonId_WeekNumber");

            migrationBuilder.RenameColumn(
                name: "RaceWeekIndex",
                schema: "iracing",
                table: "Subsessions",
                newName: "WeekNumber");

            migrationBuilder.RenameIndex(
                name: "IX_Subsessions_SeasonId_RaceWeekIndex",
                schema: "iracing",
                table: "Subsessions",
                newName: "IX_Subsessions_SeasonId_WeekNumber");

            migrationBuilder.RenameColumn(
                name: "RaceWeekIndex",
                schema: "iracing",
                table: "SeasonCarBops",
                newName: "WeekNumber");

            migrationBuilder.RenameIndex(
                name: "IX_SeasonCarBops_SeasonId_RaceWeekIndex",
                schema: "iracing",
                table: "SeasonCarBops",
                newName: "IX_SeasonCarBops_SeasonId_WeekNumber");
        }
    }
}
