using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubsessionResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LapTimeEntries",
                schema: "iracing");

            migrationBuilder.CreateTable(
                name: "Subsessions",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    SeasonId = table.Column<int>(type: "integer", nullable: false),
                    WeekNumber = table.Column<int>(type: "integer", nullable: false),
                    WeekId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrackId = table.Column<int>(type: "integer", nullable: false),
                    OfficialSession = table.Column<bool>(type: "boolean", nullable: false),
                    EventStrengthOfField = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SplitNum = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subsessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subsessions_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalSchema: "iracing",
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subsessions_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalSchema: "iracing",
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subsessions_Weeks_WeekId",
                        column: x => x.WeekId,
                        principalSchema: "iracing",
                        principalTable: "Weeks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SubsessionResults",
                schema: "iracing",
                columns: table => new
                {
                    SubsessionId = table.Column<int>(type: "integer", nullable: false),
                    CustId = table.Column<long>(type: "bigint", nullable: false),
                    CarId = table.Column<int>(type: "integer", nullable: false),
                    CarClassId = table.Column<int>(type: "integer", nullable: false),
                    FinishPosition = table.Column<int>(type: "integer", nullable: false),
                    FinishPositionInClass = table.Column<int>(type: "integer", nullable: false),
                    StartingPosition = table.Column<int>(type: "integer", nullable: false),
                    StartingPositionInClass = table.Column<int>(type: "integer", nullable: false),
                    Incidents = table.Column<int>(type: "integer", nullable: false),
                    BestLapSeconds = table.Column<double>(type: "double precision", nullable: false),
                    AverageLapSeconds = table.Column<double>(type: "double precision", nullable: false),
                    LapsComplete = table.Column<int>(type: "integer", nullable: false),
                    LapsLead = table.Column<int>(type: "integer", nullable: false),
                    ChampPoints = table.Column<int>(type: "integer", nullable: false),
                    AggregateChampPoints = table.Column<int>(type: "integer", nullable: false),
                    NewIRating = table.Column<int>(type: "integer", nullable: false),
                    OldIRating = table.Column<int>(type: "integer", nullable: false),
                    NewCpi = table.Column<double>(type: "double precision", nullable: false),
                    OldCpi = table.Column<double>(type: "double precision", nullable: false),
                    ReasonOut = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReasonOutId = table.Column<int>(type: "integer", nullable: false),
                    Division = table.Column<int>(type: "integer", nullable: false),
                    DropRace = table.Column<bool>(type: "boolean", nullable: false),
                    Interval = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubsessionResults", x => new { x.SubsessionId, x.CustId });
                    table.ForeignKey(
                        name: "FK_SubsessionResults_CarClasses_CarClassId",
                        column: x => x.CarClassId,
                        principalSchema: "iracing",
                        principalTable: "CarClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubsessionResults_Cars_CarId",
                        column: x => x.CarId,
                        principalSchema: "iracing",
                        principalTable: "Cars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubsessionResults_Subsessions_SubsessionId",
                        column: x => x.SubsessionId,
                        principalSchema: "iracing",
                        principalTable: "Subsessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubsessionResults_CarClassId",
                schema: "iracing",
                table: "SubsessionResults",
                column: "CarClassId");

            migrationBuilder.CreateIndex(
                name: "IX_SubsessionResults_CarId_SubsessionId",
                schema: "iracing",
                table: "SubsessionResults",
                columns: new[] { "CarId", "SubsessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubsessionResults_CustId",
                schema: "iracing",
                table: "SubsessionResults",
                column: "CustId");

            migrationBuilder.CreateIndex(
                name: "IX_Subsessions_SeasonId_WeekNumber",
                schema: "iracing",
                table: "Subsessions",
                columns: new[] { "SeasonId", "WeekNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Subsessions_TrackId",
                schema: "iracing",
                table: "Subsessions",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_Subsessions_WeekId",
                schema: "iracing",
                table: "Subsessions",
                column: "WeekId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubsessionResults",
                schema: "iracing");

            migrationBuilder.DropTable(
                name: "Subsessions",
                schema: "iracing");

            migrationBuilder.CreateTable(
                name: "LapTimeEntries",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CarId = table.Column<int>(type: "integer", nullable: false),
                    WeekId = table.Column<Guid>(type: "uuid", nullable: false),
                    AirTempCelsius = table.Column<float>(type: "real", nullable: true),
                    DriverCustomerId = table.Column<long>(type: "bigint", nullable: false),
                    LapTimeSeconds = table.Column<double>(type: "double precision", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TrackTempCelsius = table.Column<float>(type: "real", nullable: true),
                    TrackWetness = table.Column<byte>(type: "smallint", nullable: true)
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
        }
    }
}
