using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyntheticDriverOperatingControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyntheticDriverOperatingControls",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Snapshot = table.Column<string>(type: "text", nullable: false),
                    WindowStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcquisitionUsed = table.Column<int>(type: "integer", nullable: false),
                    PublicationUsed = table.Column<int>(type: "integer", nullable: false),
                    Reservations = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyntheticDriverOperatingControls", x => x.Id);
                    table.CheckConstraint("CK_Operating_Singleton", "\"Id\" = 1");
                    table.CheckConstraint("CK_Operating_Usage", "\"AcquisitionUsed\" >= 0 AND \"PublicationUsed\" >= 0");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Operating safety history and spent budgets are forward-only; preserve them during recovery.");
    }
}
