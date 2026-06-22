using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleRolePerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Repair any pre-existing violations before adding the constraint: collapse each user
            // to their highest-tier role (Admin > Alpha > Beta > Standard) and delete the rest.
            // The app maintains one role per user via Remove-then-Add, but an older code path or a
            // manual edit could have left duplicates in an existing database. Migrations run at
            // startup (Program.cs: db.Database.MigrateAsync()), so without this repair the unique
            // index below would throw on deploy and block API boot. Irreversible by design.
            migrationBuilder.Sql(@"
                DELETE FROM identity.""UserRoles""
                WHERE (""UserId"", ""RoleId"") IN (
                    SELECT ""UserId"", ""RoleId"" FROM (
                        SELECT ur.""UserId"", ur.""RoleId"",
                               ROW_NUMBER() OVER (
                                   PARTITION BY ur.""UserId""
                                   ORDER BY CASE r.""Name""
                                       WHEN 'Admin'    THEN 3
                                       WHEN 'Alpha'    THEN 2
                                       WHEN 'Beta'     THEN 1
                                       WHEN 'Standard' THEN 0
                                       ELSE -1
                                   END DESC, r.""Id"" ASC
                               ) AS rn
                        FROM identity.""UserRoles"" ur
                        JOIN identity.""Roles"" r ON r.""Id"" = ur.""RoleId""
                    ) ranked
                    WHERE ranked.rn > 1
                );
            ");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId",
                schema: "identity",
                table: "UserRoles",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserRoles_UserId",
                schema: "identity",
                table: "UserRoles");
        }
    }
}
