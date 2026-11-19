using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <summary>
    /// D-151. The ready reason a barber taps to cancel a booking (StaffUnwell, Emergency, ShopClosed or Other), beside the
    /// typed one, so the owner's reports can count them. Additive only: a nullable column, no data changed; cancellations
    /// before it have none. Red at the booked end, colleagues' schedules, the add-service candidates and the barber's
    /// account need no schema change (a deleted barber login clears its existing nullable professional link).
    /// </summary>
    public partial class BarberDayAndAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason_code",
                schema: "bookings",
                table: "bookings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancellation_reason_code",
                schema: "bookings",
                table: "bookings");
        }
    }
}
