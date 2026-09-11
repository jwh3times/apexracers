using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSignInThrottleCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignInAccountFailures",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    WindowStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastFailureAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NoticeSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignInAccountFailures", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_SignInAccountFailures_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SignInAddressFailures",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(56)", maxLength: 56, nullable: false),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    WindowStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastFailureAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignInAddressFailures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignInAddressFailures_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignInAccountFailures_LastFailureAt",
                schema: "identity",
                table: "SignInAccountFailures",
                column: "LastFailureAt");

            migrationBuilder.CreateIndex(
                name: "IX_SignInAddressFailures_LastFailureAt",
                schema: "identity",
                table: "SignInAddressFailures",
                column: "LastFailureAt");

            migrationBuilder.CreateIndex(
                name: "IX_SignInAddressFailures_UserId_IpAddress",
                schema: "identity",
                table: "SignInAddressFailures",
                columns: new[] { "UserId", "IpAddress" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignInAccountFailures",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "SignInAddressFailures",
                schema: "identity");
        }
    }
}
