using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeparateDriverEnforcementHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DriverProofReceipts_Users_UserId",
                schema: "iracing",
                table: "DriverProofReceipts");
            migrationBuilder.Sql(PrivateUploadDatabaseFences.UserErasureSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Erased accounts cannot be recreated to roll back enforcement history.");
        }
    }
}
