using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCarClassAndThinFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                schema: "iracing",
                table: "Series",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                schema: "iracing",
                table: "Series",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LicenseGroup",
                schema: "iracing",
                table: "Series",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Official",
                schema: "iracing",
                table: "Series",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Drops",
                schema: "iracing",
                table: "Seasons",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FixedSetup",
                schema: "iracing",
                table: "Seasons",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LicenseGroup",
                schema: "iracing",
                table: "Seasons",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Multiclass",
                schema: "iracing",
                table: "Seasons",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Official",
                schema: "iracing",
                table: "Seasons",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CarWeight",
                schema: "iracing",
                table: "Cars",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FreeWithSubscription",
                schema: "iracing",
                table: "Cars",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Hp",
                schema: "iracing",
                table: "Cars",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PackageId",
                schema: "iracing",
                table: "Cars",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Retired",
                schema: "iracing",
                table: "Cars",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CarClasses",
                schema: "iracing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShortName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RelativeSpeed = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarClasses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CarClassCars",
                schema: "iracing",
                columns: table => new
                {
                    CarClassId = table.Column<int>(type: "integer", nullable: false),
                    CarId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarClassCars", x => new { x.CarClassId, x.CarId });
                    table.ForeignKey(
                        name: "FK_CarClassCars_CarClasses_CarClassId",
                        column: x => x.CarClassId,
                        principalSchema: "iracing",
                        principalTable: "CarClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CarClassCars_Cars_CarId",
                        column: x => x.CarId,
                        principalSchema: "iracing",
                        principalTable: "Cars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeasonCarClasses",
                schema: "iracing",
                columns: table => new
                {
                    SeasonId = table.Column<int>(type: "integer", nullable: false),
                    CarClassId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonCarClasses", x => new { x.SeasonId, x.CarClassId });
                    table.ForeignKey(
                        name: "FK_SeasonCarClasses_CarClasses_CarClassId",
                        column: x => x.CarClassId,
                        principalSchema: "iracing",
                        principalTable: "CarClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeasonCarClasses_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalSchema: "iracing",
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarClassCars_CarId",
                schema: "iracing",
                table: "CarClassCars",
                column: "CarId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonCarClasses_CarClassId",
                schema: "iracing",
                table: "SeasonCarClasses",
                column: "CarClassId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarClassCars",
                schema: "iracing");

            migrationBuilder.DropTable(
                name: "SeasonCarClasses",
                schema: "iracing");

            migrationBuilder.DropTable(
                name: "CarClasses",
                schema: "iracing");

            migrationBuilder.DropColumn(
                name: "Category",
                schema: "iracing",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                schema: "iracing",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "LicenseGroup",
                schema: "iracing",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "Official",
                schema: "iracing",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "Drops",
                schema: "iracing",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "FixedSetup",
                schema: "iracing",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "LicenseGroup",
                schema: "iracing",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "Multiclass",
                schema: "iracing",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "Official",
                schema: "iracing",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "CarWeight",
                schema: "iracing",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "FreeWithSubscription",
                schema: "iracing",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "Hp",
                schema: "iracing",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "PackageId",
                schema: "iracing",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "Retired",
                schema: "iracing",
                table: "Cars");
        }
    }
}
