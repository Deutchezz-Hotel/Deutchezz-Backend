using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deutchezz.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TimeAwareStays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_CheckInDate_CheckOutDate_Status",
                table: "Bookings");

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualCheckInAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckInAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckOutAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Backfill existing stays: check-in at 14:00 Lagos (13:00 UTC), check-out at 12:00 Lagos
            // (11:00 UTC). Idempotent because a ColumnAdded default of 0001-01-01 is only overwritten.
            migrationBuilder.Sql("""
                UPDATE "Bookings"
                SET "CheckInAt" = (("CheckInDate"::timestamp + interval '13 hours') AT TIME ZONE 'UTC'),
                    "CheckOutAt" = (("CheckOutDate"::timestamp + interval '11 hours') AT TIME ZONE 'UTC')
                WHERE "CheckInAt" = '0001-01-01 00:00:00+00'
                   OR "CheckOutAt" = '0001-01-01 00:00:00+00';
                """);

            // A guest who is currently checked in was checked in at their scheduled moment.
            migrationBuilder.Sql("""
                UPDATE "Bookings"
                SET "ActualCheckInAt" = "CheckInAt"
                WHERE "Status" = 'CheckedIn' AND "ActualCheckInAt" IS NULL;
                """);

            // Stay-policy settings: insert the new keys with defaults, keeping any existing value.
            migrationBuilder.Sql("""
                INSERT INTO "Settings" ("Id", "Key", "Value", "CreatedAt", "UpdatedAt")
                VALUES
                  (gen_random_uuid(), 'DefaultCheckInTime', COALESCE((SELECT "Value" FROM "Settings" WHERE "Key" = 'CheckInTime'), '14:00'), (now() AT TIME ZONE 'utc'), (now() AT TIME ZONE 'utc')),
                  (gen_random_uuid(), 'DefaultCheckOutTime', COALESCE((SELECT "Value" FROM "Settings" WHERE "Key" = 'CheckOutTime'), '12:00'), (now() AT TIME ZONE 'utc'), (now() AT TIME ZONE 'utc')),
                  (gen_random_uuid(), 'TimeSlotMinutes', '30', (now() AT TIME ZONE 'utc'), (now() AT TIME ZONE 'utc')),
                  (gen_random_uuid(), 'EarliestCheckInTime', '00:00', (now() AT TIME ZONE 'utc'), (now() AT TIME ZONE 'utc')),
                  (gen_random_uuid(), 'LatestCheckInTime', '23:30', (now() AT TIME ZONE 'utc'), (now() AT TIME ZONE 'utc')),
                  (gen_random_uuid(), 'EarliestCheckOutTime', '00:00', (now() AT TIME ZONE 'utc'), (now() AT TIME ZONE 'utc')),
                  (gen_random_uuid(), 'LatestCheckOutTime', '23:30', (now() AT TIME ZONE 'utc'), (now() AT TIME ZONE 'utc')),
                  (gen_random_uuid(), 'MaxExtraHoursPerStay', '6', (now() AT TIME ZONE 'utc'), (now() AT TIME ZONE 'utc')),
                  (gen_random_uuid(), 'TurnaroundMinutes', '0', (now() AT TIME ZONE 'utc'), (now() AT TIME ZONE 'utc'))
                ON CONFLICT ("Key") DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CheckInAt_CheckOutAt_Status",
                table: "Bookings",
                columns: new[] { "CheckInAt", "CheckOutAt", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_CheckInAt_CheckOutAt_Status",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ActualCheckInAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CheckInAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CheckOutAt",
                table: "Bookings");

            migrationBuilder.Sql("""
                DELETE FROM "Settings"
                WHERE "Key" IN ('DefaultCheckInTime', 'DefaultCheckOutTime', 'TimeSlotMinutes',
                                'EarliestCheckInTime', 'LatestCheckInTime', 'EarliestCheckOutTime',
                                'LatestCheckOutTime', 'MaxExtraHoursPerStay', 'TurnaroundMinutes');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CheckInDate_CheckOutDate_Status",
                table: "Bookings",
                columns: new[] { "CheckInDate", "CheckOutDate", "Status" });
        }
    }
}
