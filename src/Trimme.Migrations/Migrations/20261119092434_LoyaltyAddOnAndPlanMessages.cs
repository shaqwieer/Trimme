using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <summary>
    /// D-149, slices 4 and 5. The loyalty add-on: its versioned price, each shop's periods, and its first and last day on the
    /// coverage read model; the points ledger keyed by the mobile's keyed hash (rules, cards, history, rewards held for
    /// bookings) and the reward and discount on bookings. A plan's monthly WhatsApp allowance, the per-shop monthly usage
    /// counter and the slot a dispatch took. Additive only: no data is changed. D-132 coupons stay as they are (usable until
    /// they expire); D-132 stamps are not converted to points (the member key needs each customer's decrypted mobile, which
    /// SQL cannot compute). Existing plans keep an unlimited allowance (null).
    /// </summary>
    public partial class LoyaltyAddOnAndPlanMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "allowance_month",
                schema: "notifications",
                table: "whatsapp_dispatches",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "monthly_whats_app_messages",
                schema: "subscriptions",
                table: "subscription_plans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "loyalty_add_on_end",
                schema: "subscriptions",
                table: "subscription_coverage",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "loyalty_add_on_start",
                schema: "subscriptions",
                table: "subscription_coverage",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "loyalty_discount",
                schema: "bookings",
                table: "bookings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "loyalty_redemption_id",
                schema: "bookings",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "loyalty",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    last_visit_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decay_periods_applied = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_points", "points >= 0");
                    table.ForeignKey(
                        name: "fk_accounts_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "entries",
                schema: "loyalty",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_by_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_entries_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "loyalty_add_on_prices",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loyalty_add_on_prices", x => x.id);
                    table.CheckConstraint("ck_loyalty_add_on_prices_amount", "amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "redemptions",
                schema: "loyalty",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    member_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    reward_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    discount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    title_ar = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    title_en = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    held_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_redemptions", x => x.id);
                    table.CheckConstraint("ck_redemptions_discount", "discount >= 0");
                    table.CheckConstraint("ck_redemptions_points", "points >= 1");
                    table.ForeignKey(
                        name: "fk_redemptions_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "loyalty",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_visit_points = table.Column<int>(type: "integer", nullable: false),
                    visit_points = table.Column<int>(type: "integer", nullable: false),
                    points_for_reward = table.Column<int>(type: "integer", nullable: false),
                    reward_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reward_service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reward_service_name_ar = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    reward_service_name_en = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    reward_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    inactivity_rule = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    inactivity_days = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.id);
                    table.CheckConstraint("ck_settings_points", "first_visit_points BETWEEN 1 AND 100 AND visit_points BETWEEN 1 AND 100 AND points_for_reward BETWEEN 2 AND 1000");
                    table.CheckConstraint("ck_settings_reward", "(reward_type = 'FreeService' AND reward_service_id IS NOT NULL) OR (reward_type = 'AmountOff' AND reward_amount BETWEEN 5 AND 1000)");
                    table.ForeignKey(
                        name: "fk_settings_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_usage",
                schema: "notifications",
                columns: table => new
                {
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    used = table.Column<int>(type: "integer", nullable: false),
                    owner_told_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_whatsapp_usage", x => new { x.shop_id, x.month });
                    table.CheckConstraint("ck_whatsapp_usage_used", "used >= 0");
                    table.ForeignKey(
                        name: "fk_whatsapp_usage_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "loyalty_add_on_periods",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    price_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_version_number = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stopped_on = table.Column<DateOnly>(type: "date", nullable: true),
                    stopped_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loyalty_add_on_periods", x => x.id);
                    table.CheckConstraint("ck_loyalty_add_on_periods_amount", "amount >= 0");
                    table.CheckConstraint("ck_loyalty_add_on_periods_dates", "end_date >= start_date");
                    table.ForeignKey(
                        name: "fk_loyalty_add_on_periods_loyalty_add_on_price_price_id",
                        column: x => x.price_id,
                        principalSchema: "subscriptions",
                        principalTable: "loyalty_add_on_prices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_loyalty_add_on_periods_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_member_key",
                schema: "loyalty",
                table: "accounts",
                column: "member_key");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_shop_id_member_key",
                schema: "loyalty",
                table: "accounts",
                columns: new[] { "shop_id", "member_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entries_booking_id_kind",
                schema: "loyalty",
                table: "entries",
                columns: new[] { "booking_id", "kind" },
                unique: true,
                filter: "booking_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_entries_shop_id_at",
                schema: "loyalty",
                table: "entries",
                columns: new[] { "shop_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_entries_shop_id_member_key_at",
                schema: "loyalty",
                table: "entries",
                columns: new[] { "shop_id", "member_key", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_add_on_periods_price_id",
                schema: "subscriptions",
                table: "loyalty_add_on_periods",
                column: "price_id");

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_add_on_periods_shop_id_start_date",
                schema: "subscriptions",
                table: "loyalty_add_on_periods",
                columns: new[] { "shop_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_add_on_prices_effective_from",
                schema: "subscriptions",
                table: "loyalty_add_on_prices",
                column: "effective_from",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_add_on_prices_version_number",
                schema: "subscriptions",
                table: "loyalty_add_on_prices",
                column: "version_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_redemptions_booking_id",
                schema: "loyalty",
                table: "redemptions",
                column: "booking_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_redemptions_customer_id",
                schema: "loyalty",
                table: "redemptions",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_redemptions_shop_id_member_key_status",
                schema: "loyalty",
                table: "redemptions",
                columns: new[] { "shop_id", "member_key", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_settings_shop_id",
                schema: "loyalty",
                table: "settings",
                column: "shop_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "entries",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "loyalty_add_on_periods",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "redemptions",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "whatsapp_usage",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "loyalty_add_on_prices",
                schema: "subscriptions");

            migrationBuilder.DropColumn(
                name: "allowance_month",
                schema: "notifications",
                table: "whatsapp_dispatches");

            migrationBuilder.DropColumn(
                name: "monthly_whats_app_messages",
                schema: "subscriptions",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "loyalty_add_on_end",
                schema: "subscriptions",
                table: "subscription_coverage");

            migrationBuilder.DropColumn(
                name: "loyalty_add_on_start",
                schema: "subscriptions",
                table: "subscription_coverage");

            migrationBuilder.DropColumn(
                name: "loyalty_discount",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "loyalty_redemption_id",
                schema: "bookings",
                table: "bookings");
        }
    }
}
