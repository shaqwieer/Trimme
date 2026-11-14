using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class BookingProfessionalChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "professional_changed_at",
                schema: "bookings",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "previous_professional_id",
                table: "booking_history",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "previous_professional_name_ar",
                table: "booking_history",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "previous_professional_name_en",
                table: "booking_history",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "professional_changed_at",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "previous_professional_id",
                table: "booking_history");

            migrationBuilder.DropColumn(
                name: "previous_professional_name_ar",
                table: "booking_history");

            migrationBuilder.DropColumn(
                name: "previous_professional_name_en",
                table: "booking_history");
        }
    }
}
