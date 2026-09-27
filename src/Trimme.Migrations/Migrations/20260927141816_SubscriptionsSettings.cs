using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionsSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "subscriptions");

            migrationBuilder.CreateTable(
                name: "platform_settings",
                schema: "administration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_lead_time_minutes = table.Column<int>(type: "integer", nullable: false),
                    booking_horizon_days = table.Column<int>(type: "integer", nullable: false),
                    slot_step_minutes = table.Column<int>(type: "integer", nullable: false),
                    cancellation_cutoff_minutes = table.Column<int>(type: "integer", nullable: false),
                    review_window_days = table.Column<int>(type: "integer", nullable: false),
                    reminder_offset_minutes = table.Column<int>(type: "integer", nullable: false),
                    expiring_soon_threshold_days = table.Column<int>(type: "integer", nullable: false),
                    expired_subscription_enforcement = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    hide_paused_shops_from_discovery = table.Column<bool>(type: "boolean", nullable: false),
                    default_locale = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    map_default_latitude = table.Column<double>(type: "double precision", nullable: false),
                    map_default_longitude = table.Column<double>(type: "double precision", nullable: false),
                    map_default_zoom = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_settings", x => x.id);
                    table.CheckConstraint("ck_platform_settings_ranges", "min_lead_time_minutes BETWEEN 0 AND 1440 AND booking_horizon_days BETWEEN 1 AND 365 AND cancellation_cutoff_minutes BETWEEN 0 AND 10080 AND review_window_days BETWEEN 1 AND 90 AND reminder_offset_minutes BETWEEN 5 AND 1440 AND expiring_soon_threshold_days BETWEEN 1 AND 90 AND map_default_zoom BETWEEN 3 AND 18");
                    table.CheckConstraint("ck_platform_settings_singleton", "id = '0199a0de-0000-7000-8000-00000000c0de'");
                });

            migrationBuilder.CreateTable(
                name: "subscription_coverage",
                schema: "subscriptions",
                columns: table => new
                {
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_suspended = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_coverage", x => x.shop_id);
                    table.ForeignKey(
                        name: "fk_subscription_coverage_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscription_plans",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name_en = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description_ar = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description_en = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    max_professionals = table.Column<int>(type: "integer", nullable: true),
                    max_services = table.Column<int>(type: "integer", nullable: true),
                    interval_unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    interval_count = table.Column<int>(type: "integer", nullable: false),
                    trial_days = table.Column<int>(type: "integer", nullable: true),
                    grace_days = table.Column<int>(type: "integer", nullable: true),
                    available_to_new_shops = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    features = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_plans", x => x.id);
                    table.CheckConstraint("ck_subscription_plans_interval", "interval_count >= 1");
                    table.CheckConstraint("ck_subscription_plans_limits", "(max_professionals IS NULL OR max_professionals >= 1) AND (max_services IS NULL OR max_services >= 1) AND (trial_days IS NULL OR trial_days BETWEEN 0 AND 365) AND (grace_days IS NULL OR grace_days BETWEEN 0 AND 365)");
                });

            migrationBuilder.CreateTable(
                name: "plan_prices",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_prices", x => x.id);
                    table.CheckConstraint("ck_plan_prices_amount", "amount >= 0");
                    table.ForeignKey(
                        name: "fk_plan_prices_subscription_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "subscriptions",
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shop_subscriptions",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_suspended = table.Column<bool>(type: "boolean", nullable: false),
                    suspension_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_subscriptions", x => x.id);
                    table.UniqueConstraint("ak_shop_subscriptions_shop_id_id", x => new { x.shop_id, x.id });
                    table.CheckConstraint("ck_shop_subscriptions_dates", "end_date >= start_date");
                    table.ForeignKey(
                        name: "fk_shop_subscriptions_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shop_subscriptions_subscription_plan_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "subscriptions",
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscription_periods",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_name_ar = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    plan_name_en = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    plan_price_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    is_overridden = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_periods", x => x.id);
                    table.UniqueConstraint("ak_subscription_period_shop_id_id", x => new { x.shop_id, x.id });
                    table.CheckConstraint("ck_subscription_periods_amount", "amount >= 0");
                    table.CheckConstraint("ck_subscription_periods_dates", "period_end >= period_start");
                    table.ForeignKey(
                        name: "fk_subscription_periods_plan_prices_plan_price_id",
                        column: x => x.plan_price_id,
                        principalSchema: "subscriptions",
                        principalTable: "plan_prices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscription_periods_shop_subscriptions_shop_id_subscriptio",
                        columns: x => new { x.shop_id, x.subscription_id },
                        principalSchema: "subscriptions",
                        principalTable: "shop_subscriptions",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscription_periods_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscription_periods_subscription_plan_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "subscriptions",
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscription_overrides",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    new_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    previous_end = table.Column<DateOnly>(type: "date", nullable: false),
                    new_end = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    overridden_by = table.Column<Guid>(type: "uuid", nullable: true),
                    overridden_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_overrides", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscription_overrides_shop_subscriptions_shop_id_subscript",
                        columns: x => new { x.shop_id, x.subscription_id },
                        principalSchema: "subscriptions",
                        principalTable: "shop_subscriptions",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscription_overrides_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscription_overrides_subscription_period_shop_id_period_id",
                        columns: x => new { x.shop_id, x.period_id },
                        principalSchema: "subscriptions",
                        principalTable: "subscription_periods",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_plan_prices_plan_id_effective_from",
                schema: "subscriptions",
                table: "plan_prices",
                columns: new[] { "plan_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_prices_plan_id_version_number",
                schema: "subscriptions",
                table: "plan_prices",
                columns: new[] { "plan_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_subscriptions_end_date",
                schema: "subscriptions",
                table: "shop_subscriptions",
                column: "end_date");

            migrationBuilder.CreateIndex(
                name: "ix_shop_subscriptions_plan_id",
                schema: "subscriptions",
                table: "shop_subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_shop_subscriptions_shop_id",
                schema: "subscriptions",
                table: "shop_subscriptions",
                column: "shop_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscription_coverage_end_date",
                schema: "subscriptions",
                table: "subscription_coverage",
                column: "end_date");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_overrides_shop_id_period_id",
                schema: "subscriptions",
                table: "subscription_overrides",
                columns: new[] { "shop_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_overrides_shop_id_subscription_id",
                schema: "subscriptions",
                table: "subscription_overrides",
                columns: new[] { "shop_id", "subscription_id" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_periods_plan_id",
                schema: "subscriptions",
                table: "subscription_periods",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_periods_plan_price_id",
                schema: "subscriptions",
                table: "subscription_periods",
                column: "plan_price_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_periods_shop_id_period_start",
                schema: "subscriptions",
                table: "subscription_periods",
                columns: new[] { "shop_id", "period_start" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_periods_shop_id_subscription_id",
                schema: "subscriptions",
                table: "subscription_periods",
                columns: new[] { "shop_id", "subscription_id" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_plans_display_order",
                schema: "subscriptions",
                table: "subscription_plans",
                column: "display_order");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_settings",
                schema: "administration");

            migrationBuilder.DropTable(
                name: "subscription_coverage",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "subscription_overrides",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "subscription_periods",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "plan_prices",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "shop_subscriptions",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "subscription_plans",
                schema: "subscriptions");
        }
    }
}
