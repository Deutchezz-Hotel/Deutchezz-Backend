using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deutchezz.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Data repair for stays created before the time-aware-stays migration (v1.12).
    ///
    /// <para><c>TimeAwareStays</c> added <c>CheckInAt</c>/<c>CheckOutAt</c> with a
    /// <c>0001-01-01</c> default and back-filled legacy rows with a WHERE clause matching
    /// <c>'0001-01-01 00:00:00+00'</c>. Npgsql persists <see cref="System.DateTime.MinValue"/>
    /// for a <c>timestamp with time zone</c> column as <c>-infinity</c>, so the predicate never
    /// matched: pre-v1.12 bookings kept <c>-infinity</c>, which rendered as "01:00" Lagos
    /// (<c>MinValue + 1h</c>) and made every one of them look permanently overdue.</para>
    ///
    /// <para>This migration repairs those rows to the same wall-clock defaults the original
    /// back-fill intended: check-in 14:00 Lagos (13:00 UTC), check-out 12:00 Lagos (11:00 UTC).</para>
    /// </summary>
    public partial class FixLegacyStayInstants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Bookings"
                SET "CheckInAt" = (("CheckInDate"::timestamp + interval '13 hours') AT TIME ZONE 'UTC'),
                    "CheckOutAt" = (("CheckOutDate"::timestamp + interval '11 hours') AT TIME ZONE 'UTC')
                WHERE "CheckInAt" = '-infinity'
                   OR "CheckOutAt" = '-infinity';
                """);

            // A guest still in house was checked in at their repaired scheduled moment.
            migrationBuilder.Sql("""
                UPDATE "Bookings"
                SET "ActualCheckInAt" = "CheckInAt"
                WHERE "Status" = 'CheckedIn' AND "ActualCheckInAt" = '-infinity';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible data repair: the original sentinel carried no information, so there is
            // nothing meaningful to restore.
        }
    }
}
