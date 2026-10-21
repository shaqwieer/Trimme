using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class WalkInContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // D-134: the mobile number the desk takes for a walk-in, encrypted, one row per walk-in booking. Additive:
            // earlier walk-ins have no row.
            migrationBuilder.CreateTable(
                name: "walk_in_contacts",
                schema: "bookings",
                columns: table => new
                {
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_phone = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    phone_lookup_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    phone_masked = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_walk_in_contacts", x => x.booking_id);
                    table.ForeignKey(
                        name: "fk_walk_in_contacts_bookings_shop_id_booking_id",
                        columns: x => new { x.shop_id, x.booking_id },
                        principalSchema: "bookings",
                        principalTable: "bookings",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_walk_in_contacts_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_walk_in_contacts_phone_lookup_hash",
                schema: "bookings",
                table: "walk_in_contacts",
                column: "phone_lookup_hash");

            migrationBuilder.CreateIndex(
                name: "ix_walk_in_contacts_shop_id_booking_id",
                schema: "bookings",
                table: "walk_in_contacts",
                columns: new[] { "shop_id", "booking_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "walk_in_contacts",
                schema: "bookings");
        }
    }
}
