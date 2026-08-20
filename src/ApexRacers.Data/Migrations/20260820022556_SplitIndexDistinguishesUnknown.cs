using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitIndexDistinguishesUnknown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SplitNum is dropped rather than renamed: every stored 0 is ambiguous between "the
            // strongest Split", "iRacing supplied no splits", and "this subsession was absent from
            // its own splits", so no existing value can be carried across honestly. Rows ingested
            // before this migration keep an unknown (null) Split Index; new ingests fill it in.
            migrationBuilder.DropColumn(
                name: "SplitNum",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.AddColumn<int>(
                name: "SplitCount",
                schema: "iracing",
                table: "Subsessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SplitIndex",
                schema: "iracing",
                table: "Subsessions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SplitCount",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.DropColumn(
                name: "SplitIndex",
                schema: "iracing",
                table: "Subsessions");

            migrationBuilder.AddColumn<int>(
                name: "SplitNum",
                schema: "iracing",
                table: "Subsessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
