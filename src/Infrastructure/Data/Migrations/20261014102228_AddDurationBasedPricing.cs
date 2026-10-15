using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deutchezz.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDurationBasedPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SurchargeTier",
                table: "BookingRoomTypes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RoomTypeSurchargeTiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromHours = table.Column<int>(type: "integer", nullable: false),
                    ToHours = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomTypeSurchargeTiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoomTypeSurchargeTiers_RoomTypes_RoomTypeId",
                        column: x => x.RoomTypeId,
                        principalTable: "RoomTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomTypeSurchargeTiers_RoomTypeId",
                table: "RoomTypeSurchargeTiers",
                column: "RoomTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomTypeSurchargeTiers_RoomTypeId_FromHours",
                table: "RoomTypeSurchargeTiers",
                columns: new[] { "RoomTypeId", "FromHours" },
                unique: true);

            // Seed the v1.14 fixed-amount late-check-out tier table for every existing room type,
            // keyed by name so the amounts match the Standard/Deluxe/Executive defaults. Room types
            // created later are seeded at runtime instead. Remainders within the 2h grace window stay
            // free; a remainder past 15h is charged one additional full nightly rate (not a row here).
            migrationBuilder.Sql("""
                INSERT INTO "RoomTypeSurchargeTiers" ("Id", "RoomTypeId", "FromHours", "ToHours", "Amount")
                SELECT gen_random_uuid(), rt."Id", t."FromHours", t."ToHours",
                       CASE lower(rt."Name")
                           WHEN 'deluxe' THEN CASE t."FromHours" WHEN 2 THEN 5000 WHEN 5 THEN 15000 ELSE 20000 END
                           WHEN 'executive' THEN CASE t."FromHours" WHEN 2 THEN 5000 WHEN 5 THEN 20000 ELSE 30000 END
                           ELSE CASE t."FromHours" WHEN 2 THEN 5000 WHEN 5 THEN 10000 ELSE 15000 END
                       END
                FROM "RoomTypes" rt
                CROSS JOIN (VALUES (2, 5), (5, 10), (10, 15)) AS t("FromHours", "ToHours")
                WHERE rt."IsDeleted" = false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoomTypeSurchargeTiers");

            migrationBuilder.DropColumn(
                name: "SurchargeTier",
                table: "BookingRoomTypes");
        }
    }
}
