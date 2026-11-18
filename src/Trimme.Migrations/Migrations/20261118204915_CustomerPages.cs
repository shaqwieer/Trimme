using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CustomerPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_demo",
                schema: "shops",
                table: "shops",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "reference",
                schema: "bookings",
                table: "bookings",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character(8)",
                oldFixedLength: true,
                oldMaxLength: 8);

            // D-148, the client's decision: the customer cancels or changes until the start (it was 2 hours, D-015). The
            // admin can still change it on the settings page.
            migrationBuilder.Sql("UPDATE administration.platform_settings SET cancellation_cutoff_minutes = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_demo",
                schema: "shops",
                table: "shops");

            migrationBuilder.AlterColumn<string>(
                name: "reference",
                schema: "bookings",
                table: "bookings",
                type: "character(8)",
                fixedLength: true,
                maxLength: 8,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(12)",
                oldMaxLength: 12);
        }
    }
}
