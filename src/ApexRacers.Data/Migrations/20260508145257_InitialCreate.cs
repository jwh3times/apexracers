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
            migrationBuilder.CreateTable(
                name: "Cars",
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
                name: "Series",
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
                name: "UserProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IRacingCustomerId = table.Column<long>(type: "bigint", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
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
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonalLaps",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
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
                        principalTable: "Cars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonalLaps_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeasonCars",
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
                        principalTable: "Cars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeasonCars_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Weeks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
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
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CarPercentileResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarId = table.Column<int>(type: "integer", nullable: false),
                    WeekId = table.Column<int>(type: "integer", nullable: false),
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
                        principalTable: "Cars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarPercentileResults_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CarPercentileResults_Weeks_WeekId",
                        column: x => x.WeekId,
                        principalTable: "Weeks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LapTimeEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DriverCustomerId = table.Column<long>(type: "bigint", nullable: false),
                    CarId = table.Column<int>(type: "integer", nullable: false),
                    WeekId = table.Column<int>(type: "integer", nullable: false),
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
                        principalTable: "Cars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LapTimeEntries_Weeks_WeekId",
                        column: x => x.WeekId,
                        principalTable: "Weeks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarPercentileResults_CarId",
                table: "CarPercentileResults",
                column: "CarId");

            migrationBuilder.CreateIndex(
                name: "IX_CarPercentileResults_UserId",
                table: "CarPercentileResults",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CarPercentileResults_WeekId",
                table: "CarPercentileResults",
                column: "WeekId");

            migrationBuilder.CreateIndex(
                name: "IX_LapTimeEntries_CarId_WeekId",
                table: "LapTimeEntries",
                columns: new[] { "CarId", "WeekId" });

            migrationBuilder.CreateIndex(
                name: "IX_LapTimeEntries_DriverCustomerId_WeekId",
                table: "LapTimeEntries",
                columns: new[] { "DriverCustomerId", "WeekId" });

            migrationBuilder.CreateIndex(
                name: "IX_LapTimeEntries_WeekId",
                table: "LapTimeEntries",
                column: "WeekId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalLaps_CarId",
                table: "PersonalLaps",
                column: "CarId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalLaps_UserId_CarId_IracingTrackId",
                table: "PersonalLaps",
                columns: new[] { "UserId", "CarId", "IracingTrackId" });

            migrationBuilder.CreateIndex(
                name: "IX_SeasonCars_CarId",
                table: "SeasonCars",
                column: "CarId");

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_SeriesId_Year_Quarter",
                table: "Seasons",
                columns: new[] { "SeriesId", "Year", "Quarter" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_IRacingCustomerId",
                table: "UserProfiles",
                column: "IRacingCustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Weeks_SeasonId_WeekNumber",
                table: "Weeks",
                columns: new[] { "SeasonId", "WeekNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarPercentileResults");

            migrationBuilder.DropTable(
                name: "LapTimeEntries");

            migrationBuilder.DropTable(
                name: "PersonalLaps");

            migrationBuilder.DropTable(
                name: "SeasonCars");

            migrationBuilder.DropTable(
                name: "Weeks");

            migrationBuilder.DropTable(
                name: "UserProfiles");

            migrationBuilder.DropTable(
                name: "Cars");

            migrationBuilder.DropTable(
                name: "Seasons");

            migrationBuilder.DropTable(
                name: "Series");
        }
    }
}
