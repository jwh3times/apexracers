using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexRacers.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmPreExistingAccountEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sign-in now refuses an account whose email was never confirmed, which is what stops
            // registration from being used to test who has an account. Registration never confirmed
            // anything before this release, so every existing row carries EmailConfirmed = false and
            // would be locked out of an account it could sign into yesterday. These addresses are
            // grandfathered: the users are already established, and the new requirement applies from
            // here forward. There is no schema change - this migration exists only for the backfill.
            migrationBuilder.Sql("""UPDATE identity."Users" SET "EmailConfirmed" = TRUE WHERE "EmailConfirmed" = FALSE;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. The backfill cannot be reversed: which rows it set is not recorded,
            // so undoing it would clear confirmations that were genuinely earned since - and lock
            // those users out. Rolling back the code is enough, because the old sign-in path ignores
            // EmailConfirmed entirely.
        }
    }
}
