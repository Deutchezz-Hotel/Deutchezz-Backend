using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deutchezz.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeleteMaxExtraHoursPerStaySetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "Max extra hours per stay" was removed from the admin settings and the stay
            // policy (its default of 6 contradicted the v1.14 extension tiers, and no code
            // read it). Delete any stored row so it cannot linger in the database.
            migrationBuilder.Sql("""
                DELETE FROM "Settings" WHERE "Key" = 'MaxExtraHoursPerStay';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the seeded row so rolling back to the previous migration leaves the
            // database in the shape the TimeAwareStays migration created.
            migrationBuilder.Sql("""
                INSERT INTO "Settings" ("Id", "Key", "Value", "CreatedAt", "UpdatedAt")
                VALUES (gen_random_uuid(), 'MaxExtraHoursPerStay', '6', (now() AT TIME ZONE 'utc'), (now() AT TIME ZONE 'utc'))
                ON CONFLICT ("Key") DO NOTHING;
                """);
        }
    }
}
