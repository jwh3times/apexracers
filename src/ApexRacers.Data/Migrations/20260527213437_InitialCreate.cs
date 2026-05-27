using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "iracing");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "Cars",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameAbbreviated = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Series",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IRacingCustomerId = table.Column<long>(type: "bigint", nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoleClaims",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleClaims_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "identity",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    SeriesId = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Quarter = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Seasons_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalSchema: "iracing",
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonalLaps",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarId = table.Column<int>(type: "integer", nullable: false),
                    IracingTrackId = table.Column<int>(type: "integer", nullable: false),
                    TrackName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConfigName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LapTimeSeconds = table.Column<double>(type: "double precision", nullable: false),
                    IsValidLap = table.Column<bool>(type: "boolean", nullable: false),
                    AirTempCelsius = table.Column<float>(type: "real", nullable: false),
                    TrackTempCelsius = table.Column<float>(type: "real", nullable: false),
                    TrackWetness = table.Column<byte>(type: "smallint", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalLaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonalLaps_Cars_CarId",
                        column: x => x.CarId,
                        principalSchema: "iracing",
                        principalTable: "Cars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonalLaps_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserClaims",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserClaims_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLogins",
                schema: "identity",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_UserLogins_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "identity",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTokens",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_UserTokens_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeasonCars",
                schema: "iracing",
                columns: table => new
                {
                    SeasonId = table.Column<int>(type: "integer", nullable: false),
                    CarId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonCars", x => new { x.SeasonId, x.CarId });
                    table.ForeignKey(
                        name: "FK_SeasonCars_Cars_CarId",
                        column: x => x.CarId,
                        principalSchema: "iracing",
                        principalTable: "Cars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeasonCars_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalSchema: "iracing",
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Weeks",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<int>(type: "integer", nullable: false),
                    WeekNumber = table.Column<int>(type: "integer", nullable: false),
                    IracingTrackId = table.Column<int>(type: "integer", nullable: false),
                    TrackName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConfigName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Weeks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Weeks_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalSchema: "iracing",
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CarPercentileResults",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarId = table.Column<int>(type: "integer", nullable: false),
                    WeekId = table.Column<Guid>(type: "uuid", nullable: false),
                    PercentileRank = table.Column<double>(type: "double precision", nullable: false),
                    SampleSize = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarPercentileResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarPercentileResults_Cars_CarId",
                        column: x => x.CarId,
                        principalSchema: "iracing",
                        principalTable: "Cars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarPercentileResults_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CarPercentileResults_Weeks_WeekId",
                        column: x => x.WeekId,
                        principalSchema: "iracing",
                        principalTable: "Weeks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LapTimeEntries",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverCustomerId = table.Column<long>(type: "bigint", nullable: false),
                    CarId = table.Column<int>(type: "integer", nullable: false),
                    WeekId = table.Column<Guid>(type: "uuid", nullable: false),
                    LapTimeSeconds = table.Column<double>(type: "double precision", nullable: false),
                    AirTempCelsius = table.Column<float>(type: "real", nullable: true),
                    TrackTempCelsius = table.Column<float>(type: "real", nullable: true),
                    TrackWetness = table.Column<byte>(type: "smallint", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LapTimeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LapTimeEntries_Cars_CarId",
                        column: x => x.CarId,
                        principalSchema: "iracing",
                        principalTable: "Cars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LapTimeEntries_Weeks_WeekId",
                        column: x => x.WeekId,
                        principalSchema: "iracing",
                        principalTable: "Weeks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarPercentileResults_CarId",
                schema: "iracing",
                table: "CarPercentileResults",
                column: "CarId");

            migrationBuilder.CreateIndex(
                name: "IX_CarPercentileResults_UserId",
                schema: "iracing",
                table: "CarPercentileResults",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CarPercentileResults_WeekId",
                schema: "iracing",
                table: "CarPercentileResults",
                column: "WeekId");

            migrationBuilder.CreateIndex(
                name: "IX_LapTimeEntries_CarId_WeekId",
                schema: "iracing",
                table: "LapTimeEntries",
                columns: new[] { "CarId", "WeekId" });

            migrationBuilder.CreateIndex(
                name: "IX_LapTimeEntries_DriverCustomerId_WeekId",
                schema: "iracing",
                table: "LapTimeEntries",
                columns: new[] { "DriverCustomerId", "WeekId" });

            migrationBuilder.CreateIndex(
                name: "IX_LapTimeEntries_WeekId",
                schema: "iracing",
                table: "LapTimeEntries",
                column: "WeekId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalLaps_CarId",
                schema: "iracing",
                table: "PersonalLaps",
                column: "CarId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalLaps_UserId_CarId_IracingTrackId",
                schema: "iracing",
                table: "PersonalLaps",
                columns: new[] { "UserId", "CarId", "IracingTrackId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleClaims_RoleId",
                schema: "identity",
                table: "RoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "identity",
                table: "Roles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeasonCars_CarId",
                schema: "iracing",
                table: "SeasonCars",
                column: "CarId");

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_SeriesId_Year_Quarter",
                schema: "iracing",
                table: "Seasons",
                columns: new[] { "SeriesId", "Year", "Quarter" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserClaims_UserId",
                schema: "identity",
                table: "UserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLogins_UserId",
                schema: "identity",
                table: "UserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                schema: "identity",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "identity",
                table: "Users",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Users_IRacingCustomerId",
                schema: "identity",
                table: "Users",
                column: "IRacingCustomerId",
                unique: true,
                filter: "\"IRacingCustomerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "identity",
                table: "Users",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Weeks_SeasonId_WeekNumber",
                schema: "iracing",
                table: "Weeks",
                columns: new[] { "SeasonId", "WeekNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarPercentileResults",
                schema: "iracing");

            migrationBuilder.DropTable(
                name: "LapTimeEntries",
                schema: "iracing");

            migrationBuilder.DropTable(
                name: "PersonalLaps",
                schema: "iracing");

            migrationBuilder.DropTable(
                name: "RoleClaims",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "SeasonCars",
                schema: "iracing");

            migrationBuilder.DropTable(
                name: "UserClaims",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "UserLogins",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "UserRoles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "UserTokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "Weeks",
                schema: "iracing");

            migrationBuilder.DropTable(
                name: "Cars",
                schema: "iracing");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "Seasons",
                schema: "iracing");

            migrationBuilder.DropTable(
                name: "Series",
                schema: "iracing");
        }
    }
}
