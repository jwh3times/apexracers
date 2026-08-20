using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecordUnrepresentedFieldEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AiEntryCount",
                schema: "iracing",
                table: "Subsessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeamEntryCount",
                schema: "iracing",
                table: "Subsessions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiEntryCount",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.DropColumn(
                name: "TeamEntryCount",
                schema: "iracing",
                table: "Subsessions");
        }
    }
}
