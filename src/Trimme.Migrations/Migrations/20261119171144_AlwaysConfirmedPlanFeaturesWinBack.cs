using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlwaysConfirmedPlanFeaturesWinBack : Migration
    {
        /// <summary>
        /// Writes the coverage read model's periods (D-155) from the subscription periods, in the JSON the model reads (its
        /// keys are the property names). Public so a test runs this exact SQL against real rows.
        /// </summary>
        public const string CoveragePeriodsSql = """
            UPDATE subscriptions.subscription_coverage c
            SET periods = COALESCE((
                SELECT jsonb_agg(jsonb_build_object('Start', p.period_start, 'End', p.period_end, 'PlanId', p.plan_id) ORDER BY p.period_start)
                FROM subscriptions.subscription_periods p
                WHERE p.shop_id = c.shop_id), '[]'::jsonb);
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // D-155 «الحلاق يغيّر الموعد مباشرة»: postponement offers are gone. Their holds on the barber's time (D-147) go
            // first; open offers are dropped with their table, and each booking stays at its time. The history keeps the
            // offers' lines.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS tr_bookings_postponement_hold ON bookings.bookings;
                DROP TRIGGER IF EXISTS tr_postponement_offers_hold ON bookings.postponement_offers;
                DROP FUNCTION IF EXISTS bookings.check_booking_against_offers();
                DROP FUNCTION IF EXISTS bookings.check_offer_hold();
                DROP FUNCTION IF EXISTS bookings.live_offer_overlaps(uuid, timestamptz, timestamptz, uuid);
                DROP FUNCTION IF EXISTS bookings.hold_lock(uuid);
                """);

            // D-155 «التأكيد التلقائي دايم»: every online booking is confirmed at once. A booking still waiting for its shop
            // is confirmed now, by the system, with a line in its history and no message (no outbox row).
            migrationBuilder.Sql("""
                INSERT INTO public.booking_history (booking_id, kind, from_status, to_status, actor_type, reason, occurred_at)
                SELECT id, 'StatusChanged', 'Pending', 'Confirmed', 'System', NULL, now()
                FROM bookings.bookings WHERE status = 'Pending';

                UPDATE bookings.bookings SET status = 'Confirmed', updated_at = now() WHERE status = 'Pending';

                DELETE FROM notifications.shop_message_opt_outs WHERE event = 'BookingPending';
                """);

            // D-155: the monthly limit now counts the win-back message only, from the subscription's day. D-149's counts were
            // every customer message per calendar month: they no longer mean anything.
            migrationBuilder.Sql("DELETE FROM notifications.whatsapp_usage;");

            migrationBuilder.DropTable(
                name: "loyalty_add_on_periods",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "postponement_offers",
                schema: "bookings");

            migrationBuilder.DropTable(
                name: "loyalty_add_on_prices",
                schema: "subscriptions");

            migrationBuilder.DropColumn(
                name: "loyalty_add_on_end",
                schema: "subscriptions",
                table: "subscription_coverage");

            migrationBuilder.DropColumn(
                name: "loyalty_add_on_start",
                schema: "subscriptions",
                table: "subscription_coverage");

            migrationBuilder.DropColumn(
                name: "require_manual_confirmation",
                schema: "shops",
                table: "shops");

            migrationBuilder.RenameColumn(
                name: "monthly_whats_app_messages",
                schema: "subscriptions",
                table: "subscription_plans",
                newName: "monthly_marketing_messages");

            // Every existing customer gets the win-back message until they turn it off on «حسابي».
            migrationBuilder.AddColumn<bool>(
                name: "win_back_messages",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "includes_loyalty",
                schema: "subscriptions",
                table: "subscription_plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Existing plans: no loyalty and no win-back until SuperAdmin ticks them (admin data at deploy), 10 days ready.
            migrationBuilder.AddColumn<int>(
                name: "win_back_after_days",
                schema: "subscriptions",
                table: "subscription_plans",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<bool>(
                name: "win_back_enabled",
                schema: "subscriptions",
                table: "subscription_plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "periods",
                schema: "subscriptions",
                table: "subscription_coverage",
                type: "jsonb",
                nullable: true);

            // The coverage read model learns each period's plan (D-155), so loyalty can follow the plan for any shop
            // without a tenant scope. Written from the periods; the application keeps it in step from now on.
            migrationBuilder.Sql(CoveragePeriodsSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "win_back_messages",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "includes_loyalty",
                schema: "subscriptions",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "win_back_after_days",
                schema: "subscriptions",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "win_back_enabled",
                schema: "subscriptions",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "periods",
                schema: "subscriptions",
                table: "subscription_coverage");

            migrationBuilder.RenameColumn(
                name: "monthly_marketing_messages",
                schema: "subscriptions",
                table: "subscription_plans",
                newName: "monthly_whats_app_messages");

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

            migrationBuilder.AddColumn<bool>(
                name: "require_manual_confirmation",
                schema: "shops",
                table: "shops",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "loyalty_add_on_prices",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loyalty_add_on_prices", x => x.id);
                    table.CheckConstraint("ck_loyalty_add_on_prices_amount", "amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "postponement_offers",
                schema: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    blocked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    offered_by = table.Column<Guid>(type: "uuid", nullable: true),
                    offered_by_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    original_starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_postponement_offers", x => x.id);
                    table.CheckConstraint("ck_postponement_offers_range", "ends_at > starts_at");
                    table.ForeignKey(
                        name: "fk_postponement_offers_bookings_shop_id_booking_id",
                        columns: x => new { x.shop_id, x.booking_id },
                        principalSchema: "bookings",
                        principalTable: "bookings",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_postponement_offers_shops_shop_id",
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
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    price_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_version_number = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    stopped_by = table.Column<Guid>(type: "uuid", nullable: true),
                    stopped_on = table.Column<DateOnly>(type: "date", nullable: true)
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
                name: "ix_postponement_offers_booking_id_created_at",
                schema: "bookings",
                table: "postponement_offers",
                columns: new[] { "booking_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_postponement_offers_customer_id_created_at",
                schema: "bookings",
                table: "postponement_offers",
                columns: new[] { "customer_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_postponement_offers_professional_id_starts_at",
                schema: "bookings",
                table: "postponement_offers",
                columns: new[] { "professional_id", "starts_at" },
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_postponement_offers_shop_id_booking_id",
                schema: "bookings",
                table: "postponement_offers",
                columns: new[] { "shop_id", "booking_id" });

            // The holds of BarberOperations as OwnerSettings left them. Confirmed bookings stay confirmed, and the usage
            // counts and the BookingPending switches deleted above are not restored.
            migrationBuilder.Sql("""
                CREATE FUNCTION bookings.hold_lock(professional uuid) RETURNS void LANGUAGE sql AS $$
                    SELECT pg_advisory_xact_lock(hashtextextended('trimme.barber-time:' || professional::text, 0));
                $$;

                CREATE FUNCTION bookings.live_offer_overlaps(professional uuid, from_at timestamptz, to_at timestamptz, own_booking uuid)
                RETURNS boolean LANGUAGE sql STABLE AS $$
                    SELECT EXISTS (
                        SELECT 1
                        FROM bookings.postponement_offers o
                        JOIN bookings.bookings ob ON ob.id = o.booking_id
                        WHERE o.professional_id = professional
                          AND o.booking_id <> own_booking
                          AND o.status = 'Open'
                          AND o.expires_at > now()
                          AND ob.status IN ('Pending', 'Confirmed')
                          AND ob.starts_at = o.original_starts_at
                          AND ob.professional_id = o.professional_id
                          AND o.starts_at < to_at
                          AND o.blocked_until > from_at);
                $$;

                CREATE FUNCTION bookings.check_booking_against_offers() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM bookings.hold_lock(NEW.professional_id);
                    IF bookings.live_offer_overlaps(NEW.professional_id, NEW.starts_at, NEW.blocked_until, NEW.id) THEN
                        RAISE EXCEPTION 'The barber''s time is held for a postponement offer.'
                            USING ERRCODE = '23P01', CONSTRAINT = 'ex_bookings_postponement_hold';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE FUNCTION bookings.check_offer_hold() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM bookings.hold_lock(NEW.professional_id);
                    IF EXISTS (
                        SELECT 1 FROM bookings.bookings b
                        WHERE b.professional_id = NEW.professional_id
                          AND b.id <> NEW.booking_id
                          AND b.status IN ('Pending', 'Confirmed', 'Arrived')
                          AND b.starts_at < NEW.blocked_until
                          AND b.blocked_until > NEW.starts_at)
                       OR bookings.live_offer_overlaps(NEW.professional_id, NEW.starts_at, NEW.blocked_until, NEW.booking_id) THEN
                        RAISE EXCEPTION 'The barber is not free for this postponement offer.'
                            USING ERRCODE = '23P01', CONSTRAINT = 'ex_postponement_offers_hold';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER tr_bookings_postponement_hold
                    BEFORE INSERT OR UPDATE OF starts_at, ends_at, blocked_until, professional_id, status ON bookings.bookings
                    FOR EACH ROW WHEN (NEW.status IN ('Pending', 'Confirmed', 'Arrived'))
                    EXECUTE FUNCTION bookings.check_booking_against_offers();

                CREATE TRIGGER tr_postponement_offers_hold
                    BEFORE INSERT ON bookings.postponement_offers
                    FOR EACH ROW WHEN (NEW.status = 'Open')
                    EXECUTE FUNCTION bookings.check_offer_hold();
                """);
        }
    }
}
