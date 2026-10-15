using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deutchezz.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckOutReminderSentAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CheckOutReminderSentAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckOutReminderSentAt",
                table: "Bookings");
        }
    }
}
