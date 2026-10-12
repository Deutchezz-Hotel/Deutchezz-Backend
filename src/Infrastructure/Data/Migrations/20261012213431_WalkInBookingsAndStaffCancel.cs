using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deutchezz.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class WalkInBookingsAndStaffCancel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Guests",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Bookings",
                type: "text",
                nullable: false,
                defaultValue: "Online");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "Bookings");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Guests",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
