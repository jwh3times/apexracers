using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedIracingDemoFlag : Migration
    {
        // Fixed timestamp — migrations must be deterministic (no DateTime.UtcNow).
        private static readonly DateTime SeededAt = new(2026, 6, 23, 0, 0, 0, DateTimeKind.Utc);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "iracing",
                table: "FeatureFlags",
                columns: new[] { "Key", "Name", "Description", "IsEnabled", "MinimumRole", "CreatedAt", "UpdatedAt" },
                values: new object[]
                {
                    "iracing-demo",
                    "Demo iRacing data",
                    "Reveals the iRacing-data surface backed by clearly-labeled synthetic demo data for Alpha testers, while real iRacing credentials are unavailable. Shows the demo banner and resolves personalized data to the shared demo driver. Turn off and run purge_demo_data.sql before enabling iracing-live with real credentials.",
                    false,
                    "Alpha",
                    SeededAt,
                    SeededAt,
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "iracing",
                table: "FeatureFlags",
                keyColumn: "Key",
                keyValue: "iracing-demo");
        }
    }
}
