using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionCustomPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pricing_reason",
                schema: "subscriptions",
                table: "subscription_periods",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "standard_amount",
                schema: "subscriptions",
                table: "subscription_periods",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pricing_reason",
                schema: "subscriptions",
                table: "subscription_periods");

            migrationBuilder.DropColumn(
                name: "standard_amount",
                schema: "subscriptions",
                table: "subscription_periods");
        }
    }
}
