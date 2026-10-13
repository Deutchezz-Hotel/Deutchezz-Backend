using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deutchezz.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class StayChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActualCheckOutAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EarlyCheckOutNotes",
                table: "Bookings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EarlyCheckOutReason",
                table: "Bookings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualCheckOutAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "EarlyCheckOutNotes",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "EarlyCheckOutReason",
                table: "Bookings");
        }
    }
}
