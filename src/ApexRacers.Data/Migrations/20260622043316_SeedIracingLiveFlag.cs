using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedIracingLiveFlag : Migration
    {
        // Fixed timestamp — migrations must be deterministic (no DateTime.UtcNow).
        private static readonly DateTime SeededAt = new(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "iracing",
                table: "FeatureFlags",
                columns: new[] { "Key", "Name", "Description", "IsEnabled", "MinimumRole", "CreatedAt", "UpdatedAt" },
                values: new object[]
                {
                    "iracing-live",
                    "Live iRacing data",
                    "Reveals every iRacing-data-backed surface (series, analytics, leaderboards, catalog, live race data). Off until the iRacing service-account credentials are configured.",
                    false,
                    "Admin",
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
                keyValue: "iracing-live");
        }
    }
}
