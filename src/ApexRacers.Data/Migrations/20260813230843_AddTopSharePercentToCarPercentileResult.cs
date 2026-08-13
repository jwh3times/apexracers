using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTopSharePercentToCarPercentileResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every existing row holds a percentile computed under the previous formula, which
            // divided strictly-slower drivers by the count of *other* drivers and reported 100 for
            // anyone who beat everyone they were compared with. Those readings are not comparable
            // with the ones written from now on, and CarRecommendationService averages readings
            // across race weeks — so leaving them would silently blend two conventions into every
            // running average rather than merely showing one stale number.
            //
            // This table is a cache: every row is recomputed and re-upserted the next time the
            // driver opens the percentile or recommendations page, so clearing it costs a
            // recomputation, not data.
            migrationBuilder.Sql(@"DELETE FROM iracing.""CarPercentileResults"";");

            migrationBuilder.AddColumn<int>(
                name: "TopSharePercent",
                schema: "iracing",
                table: "CarPercentileResults",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TopSharePercent",
                schema: "iracing",
                table: "CarPercentileResults");
        }
    }
}
