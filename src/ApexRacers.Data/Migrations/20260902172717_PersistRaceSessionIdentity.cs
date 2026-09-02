using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistRaceSessionIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RaceSessionId",
                schema: "iracing",
                table: "Subsessions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subsessions_RaceSessionId",
                schema: "iracing",
                table: "Subsessions",
                column: "RaceSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Subsessions_RaceSessionId",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.DropColumn(
                name: "RaceSessionId",
                schema: "iracing",
                table: "Subsessions");
        }
    }
}
