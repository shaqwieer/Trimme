using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class LoyaltyFirstVisitStamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // D-133: a first visit can earn several stamps, numbered from 1. Existing stamps are each their booking's
            // only one, and existing programmes have no welcome bonus, so both new columns start at 1.
            migrationBuilder.DropIndex(
                name: "ix_stamps_booking_id",
                schema: "loyalty",
                table: "stamps");

            migrationBuilder.AddColumn<int>(
                name: "number",
                schema: "loyalty",
                table: "stamps",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "first_visit_stamps",
                schema: "loyalty",
                table: "programs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ix_stamps_booking_id_number",
                schema: "loyalty",
                table: "stamps",
                columns: new[] { "booking_id", "number" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_stamps_number",
                schema: "loyalty",
                table: "stamps",
                sql: "number >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_programs_first_visit_stamps",
                schema: "loyalty",
                table: "programs",
                sql: "first_visit_stamps >= 1 AND first_visit_stamps < required_visits");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // One stamp per booking again: the extra first-visit stamps are dropped.
            migrationBuilder.Sql("DELETE FROM loyalty.stamps WHERE number > 1;");

            migrationBuilder.DropIndex(
                name: "ix_stamps_booking_id_number",
                schema: "loyalty",
                table: "stamps");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stamps_number",
                schema: "loyalty",
                table: "stamps");

            migrationBuilder.DropCheckConstraint(
                name: "ck_programs_first_visit_stamps",
                schema: "loyalty",
                table: "programs");

            migrationBuilder.DropColumn(
                name: "number",
                schema: "loyalty",
                table: "stamps");

            migrationBuilder.DropColumn(
                name: "first_visit_stamps",
                schema: "loyalty",
                table: "programs");

            migrationBuilder.CreateIndex(
                name: "ix_stamps_booking_id",
                schema: "loyalty",
                table: "stamps",
                column: "booking_id",
                unique: true);
        }
    }
}
