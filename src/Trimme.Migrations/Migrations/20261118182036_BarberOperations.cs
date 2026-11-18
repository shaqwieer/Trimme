using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class BarberOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "actor_name",
                table: "booking_history",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "postponement_offers",
                schema: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    offered_by = table.Column<Guid>(type: "uuid", nullable: true),
                    offered_by_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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

            // D-147: a live postponement offer holds the barber's time. Both checks take the same per-barber transaction
            // lock first, so a booking and an offer for the same time cannot both pass; a refusal is SQLSTATE 23P01, which
            // the application already treats as a lost race (slot unavailable). An offer is live while it is Open, before
            // its deadline, and its booking still waits (Pending or Confirmed) at the original time with the same barber.
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
                          AND o.ends_at > from_at);
                $$;

                CREATE FUNCTION bookings.check_booking_against_offers() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM bookings.hold_lock(NEW.professional_id);
                    IF bookings.live_offer_overlaps(NEW.professional_id, NEW.starts_at, NEW.ends_at, NEW.id) THEN
                        RAISE EXCEPTION 'The barber''s time is held for a postponement offer.'
                            USING ERRCODE = '23P01', CONSTRAINT = 'ex_bookings_postponement_hold';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER tr_bookings_postponement_hold
                    BEFORE INSERT OR UPDATE OF starts_at, ends_at, professional_id, status ON bookings.bookings
                    FOR EACH ROW WHEN (NEW.status IN ('Pending', 'Confirmed', 'Arrived'))
                    EXECUTE FUNCTION bookings.check_booking_against_offers();

                CREATE FUNCTION bookings.check_offer_hold() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM bookings.hold_lock(NEW.professional_id);
                    IF EXISTS (
                        SELECT 1 FROM bookings.bookings b
                        WHERE b.professional_id = NEW.professional_id
                          AND b.id <> NEW.booking_id
                          AND b.status IN ('Pending', 'Confirmed', 'Arrived')
                          AND b.starts_at < NEW.ends_at
                          AND b.ends_at > NEW.starts_at)
                       OR bookings.live_offer_overlaps(NEW.professional_id, NEW.starts_at, NEW.ends_at, NEW.booking_id) THEN
                        RAISE EXCEPTION 'The barber is not free for this postponement offer.'
                            USING ERRCODE = '23P01', CONSTRAINT = 'ex_postponement_offers_hold';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER tr_postponement_offers_hold
                    BEFORE INSERT ON bookings.postponement_offers
                    FOR EACH ROW WHEN (NEW.status = 'Open')
                    EXECUTE FUNCTION bookings.check_offer_hold();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS tr_bookings_postponement_hold ON bookings.bookings;
                DROP TRIGGER IF EXISTS tr_postponement_offers_hold ON bookings.postponement_offers;
                DROP FUNCTION IF EXISTS bookings.check_booking_against_offers();
                DROP FUNCTION IF EXISTS bookings.check_offer_hold();
                DROP FUNCTION IF EXISTS bookings.live_offer_overlaps(uuid, timestamptz, timestamptz, uuid);
                DROP FUNCTION IF EXISTS bookings.hold_lock(uuid);
                """);

            migrationBuilder.DropTable(
                name: "postponement_offers",
                schema: "bookings");

            migrationBuilder.DropColumn(
                name: "actor_name",
                table: "booking_history");
        }
    }
}
