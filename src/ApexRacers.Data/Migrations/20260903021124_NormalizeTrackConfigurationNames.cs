using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeTrackConfigurationNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE iracing."Tracks"
                SET "ConfigName" = ''
                WHERE btrim("ConfigName") = ''
                   OR upper(btrim("ConfigName")) = 'N/A';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The upstream spelling that produced an absent Configuration Name is unknowable once
            // normalized, so there is no truthful reverse data migration.
        }
    }
}
