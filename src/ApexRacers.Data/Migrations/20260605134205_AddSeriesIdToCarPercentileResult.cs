using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesIdToCarPercentileResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CarPercentileResults_UserId",
                schema: "iracing",
                table: "CarPercentileResults");

            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                schema: "iracing",
                table: "CarPercentileResults",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Clear synthetic cached rows whose SeriesId = 0 would violate the new FK.
            migrationBuilder.Sql(@"DELETE FROM iracing.""CarPercentileResults"";");

            migrationBuilder.CreateIndex(
                name: "IX_CarPercentileResults_SeriesId",
                schema: "iracing",
                table: "CarPercentileResults",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_CarPercentileResults_UserId_CarId_SeriesId_WeekId",
                schema: "iracing",
                table: "CarPercentileResults",
                columns: new[] { "UserId", "CarId", "SeriesId", "WeekId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CarPercentileResults_Series_SeriesId",
                schema: "iracing",
                table: "CarPercentileResults",
                column: "SeriesId",
                principalSchema: "iracing",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CarPercentileResults_Series_SeriesId",
                schema: "iracing",
                table: "CarPercentileResults");

            migrationBuilder.DropIndex(
                name: "IX_CarPercentileResults_SeriesId",
                schema: "iracing",
                table: "CarPercentileResults");

            migrationBuilder.DropIndex(
                name: "IX_CarPercentileResults_UserId_CarId_SeriesId_WeekId",
                schema: "iracing",
                table: "CarPercentileResults");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                schema: "iracing",
                table: "CarPercentileResults");

            migrationBuilder.CreateIndex(
                name: "IX_CarPercentileResults_UserId",
                schema: "iracing",
                table: "CarPercentileResults",
                column: "UserId");
        }
    }
}
