using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <summary>
    /// D-149, the owner's dashboard: each shop's own booking settings (start interval, minimum notice, furthest day,
    /// cleaning time), the cleaning time held after each booking (<c>blocked_until</c>, which the exclusion constraint and
    /// the postponement holds now compare), each barber's own length per service and their languages, and the shop's own
    /// sections of its services. Data: every shop becomes a men's barbershop, the platform's furthest day becomes 14, and
    /// each shop gets a section for every platform category its services use, with its services placed in it.
    /// </summary>
    public partial class OwnerSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- Each shop's own booking settings (null follows the platform's) and its cleaning time.
            migrationBuilder.AddColumn<int>(name: "booking_horizon_days", schema: "shops", table: "shops", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "buffer_minutes", schema: "shops", table: "shops", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "min_lead_time_minutes", schema: "shops", table: "shops", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "slot_step_minutes", schema: "shops", table: "shops", type: "integer", nullable: true);

            // --- Barbers: languages, and their own length per service.
            migrationBuilder.AddColumn<string[]>(
                name: "languages", schema: "professionals", table: "professionals", type: "text[]", nullable: false, defaultValue: Array.Empty<string>());
            migrationBuilder.AddColumn<int>(name: "duration_minutes", schema: "services", table: "professional_services", type: "integer", nullable: true);
            migrationBuilder.AddCheckConstraint(
                name: "ck_professional_services_duration",
                schema: "services",
                table: "professional_services",
                sql: "duration_minutes IS NULL OR (duration_minutes > 0 AND duration_minutes % 5 = 0)");

            // --- What a booking (and a live postponement offer) holds of the barber's time: through its cleaning time.
            // Existing rows had no cleaning time, so they hold until their end.
            migrationBuilder.AddColumn<int>(name: "buffer_minutes", schema: "bookings", table: "bookings", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "blocked_until", schema: "bookings", table: "bookings", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "blocked_until", schema: "bookings", table: "postponement_offers", type: "timestamp with time zone", nullable: true);
            migrationBuilder.Sql("""
                UPDATE bookings.bookings SET blocked_until = ends_at;
                UPDATE bookings.postponement_offers SET blocked_until = ends_at;
                ALTER TABLE bookings.bookings ALTER COLUMN blocked_until SET NOT NULL;
                ALTER TABLE bookings.postponement_offers ALTER COLUMN blocked_until SET NOT NULL;
                """);
            migrationBuilder.AddCheckConstraint(name: "ck_bookings_blocked", schema: "bookings", table: "bookings", sql: "blocked_until >= ends_at");

            // The exclusion constraint (R-BKG-03) compares the generated range, which now runs to blocked_until. A generated
            // column's expression cannot be altered in place, so the constraint and the column are rebuilt.
            migrationBuilder.Sql("""
                ALTER TABLE bookings.bookings DROP CONSTRAINT ex_bookings_professional_overlap;
                ALTER TABLE bookings.bookings DROP COLUMN during;
                ALTER TABLE bookings.bookings
                    ADD COLUMN during tstzrange GENERATED ALWAYS AS (tstzrange(starts_at, blocked_until, '[)')) STORED NOT NULL;
                ALTER TABLE bookings.bookings
                    ADD CONSTRAINT ex_bookings_professional_overlap
                    EXCLUDE USING gist (professional_id WITH =, during WITH &&)
                    WHERE (status IN ('Pending', 'Confirmed', 'Arrived'));
                """);

            // The postponement holds (D-147) compare the same ranges: a booking's and an offer's start to blocked_until.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION bookings.live_offer_overlaps(professional uuid, from_at timestamptz, to_at timestamptz, own_booking uuid)
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

                CREATE OR REPLACE FUNCTION bookings.check_booking_against_offers() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM bookings.hold_lock(NEW.professional_id);
                    IF bookings.live_offer_overlaps(NEW.professional_id, NEW.starts_at, NEW.blocked_until, NEW.id) THEN
                        RAISE EXCEPTION 'The barber''s time is held for a postponement offer.'
                            USING ERRCODE = '23P01', CONSTRAINT = 'ex_bookings_postponement_hold';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE OR REPLACE FUNCTION bookings.check_offer_hold() RETURNS trigger LANGUAGE plpgsql AS $$
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

                DROP TRIGGER tr_bookings_postponement_hold ON bookings.bookings;
                CREATE TRIGGER tr_bookings_postponement_hold
                    BEFORE INSERT OR UPDATE OF starts_at, ends_at, blocked_until, professional_id, status ON bookings.bookings
                    FOR EACH ROW WHEN (NEW.status IN ('Pending', 'Confirmed', 'Arrived'))
                    EXECUTE FUNCTION bookings.check_booking_against_offers();
                """);

            // --- The shop's own sections of its services.
            migrationBuilder.CreateTable(
                name: "service_sections",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name_en = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_sections", x => x.id);
                    table.UniqueConstraint("ak_service_sections_shop_id_id", x => new { x.shop_id, x.id });
                    table.ForeignKey(
                        name: "fk_service_sections_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(
                name: "ix_service_sections_shop_id_display_order", schema: "services", table: "service_sections", columns: ["shop_id", "display_order"]);
            migrationBuilder.AddColumn<Guid>(name: "section_id", schema: "services", table: "shop_services", type: "uuid", nullable: true);
            migrationBuilder.CreateIndex(
                name: "ix_shop_services_shop_id_section_id", schema: "services", table: "shop_services", columns: ["shop_id", "section_id"]);
            migrationBuilder.AddForeignKey(
                name: "fk_shop_services_service_sections_shop_id_section_id",
                schema: "services",
                table: "shop_services",
                columns: ["shop_id", "section_id"],
                principalSchema: "services",
                principalTable: "service_sections",
                principalColumns: ["shop_id", "id"],
                onDelete: ReferentialAction.Restrict);

            // Each shop gets a section for every platform category its (non-archived) services use, named and ordered like
            // the category; its services in that category are placed in it. The section's id is derived from the shop and
            // the category, so the placement needs no lookup by name.
            migrationBuilder.Sql("""
                INSERT INTO services.service_sections (id, shop_id, name_ar, name_en, display_order, created_at)
                SELECT md5(used.shop_id::text || ':' || c.id::text)::uuid,
                       used.shop_id,
                       left(c.name_ar, 60),
                       nullif(left(c.name_en, 60), ''),
                       row_number() OVER (PARTITION BY used.shop_id ORDER BY c.display_order, c.name_ar),
                       now()
                FROM (SELECT DISTINCT shop_id, category_id FROM services.shop_services WHERE category_id IS NOT NULL AND NOT is_archived) used
                JOIN services.service_categories c ON c.id = used.category_id;

                UPDATE services.shop_services s
                SET section_id = md5(s.shop_id::text || ':' || s.category_id::text)::uuid
                WHERE s.category_id IS NOT NULL
                  AND EXISTS (SELECT 1 FROM services.service_sections sec WHERE sec.id = md5(s.shop_id::text || ':' || s.category_id::text)::uuid);
                """);

            // --- The client's decisions: a men's barbershop platform, and bookings 14 days ahead as in the app.
            migrationBuilder.Sql("""
                UPDATE shops.shops SET category = 'Barbershop' WHERE category <> 'Barbershop';
                UPDATE administration.platform_settings SET booking_horizon_days = 14;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "fk_shop_services_service_sections_shop_id_section_id", schema: "services", table: "shop_services");
            migrationBuilder.DropIndex(name: "ix_shop_services_shop_id_section_id", schema: "services", table: "shop_services");
            migrationBuilder.DropColumn(name: "section_id", schema: "services", table: "shop_services");
            migrationBuilder.DropTable(name: "service_sections", schema: "services");

            migrationBuilder.Sql("""
                DROP TRIGGER tr_bookings_postponement_hold ON bookings.bookings;
                CREATE TRIGGER tr_bookings_postponement_hold
                    BEFORE INSERT OR UPDATE OF starts_at, ends_at, professional_id, status ON bookings.bookings
                    FOR EACH ROW WHEN (NEW.status IN ('Pending', 'Confirmed', 'Arrived'))
                    EXECUTE FUNCTION bookings.check_booking_against_offers();

                CREATE OR REPLACE FUNCTION bookings.live_offer_overlaps(professional uuid, from_at timestamptz, to_at timestamptz, own_booking uuid)
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

                CREATE OR REPLACE FUNCTION bookings.check_booking_against_offers() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM bookings.hold_lock(NEW.professional_id);
                    IF bookings.live_offer_overlaps(NEW.professional_id, NEW.starts_at, NEW.ends_at, NEW.id) THEN
                        RAISE EXCEPTION 'The barber''s time is held for a postponement offer.'
                            USING ERRCODE = '23P01', CONSTRAINT = 'ex_bookings_postponement_hold';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE OR REPLACE FUNCTION bookings.check_offer_hold() RETURNS trigger LANGUAGE plpgsql AS $$
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

                ALTER TABLE bookings.bookings DROP CONSTRAINT ex_bookings_professional_overlap;
                ALTER TABLE bookings.bookings DROP COLUMN during;
                ALTER TABLE bookings.bookings
                    ADD COLUMN during tstzrange GENERATED ALWAYS AS (tstzrange(starts_at, ends_at, '[)')) STORED NOT NULL;
                ALTER TABLE bookings.bookings
                    ADD CONSTRAINT ex_bookings_professional_overlap
                    EXCLUDE USING gist (professional_id WITH =, during WITH &&)
                    WHERE (status IN ('Pending', 'Confirmed', 'Arrived'));
                """);

            migrationBuilder.DropCheckConstraint(name: "ck_bookings_blocked", schema: "bookings", table: "bookings");
            migrationBuilder.DropColumn(name: "blocked_until", schema: "bookings", table: "postponement_offers");
            migrationBuilder.DropColumn(name: "blocked_until", schema: "bookings", table: "bookings");
            migrationBuilder.DropColumn(name: "buffer_minutes", schema: "bookings", table: "bookings");
            migrationBuilder.DropCheckConstraint(name: "ck_professional_services_duration", schema: "services", table: "professional_services");
            migrationBuilder.DropColumn(name: "duration_minutes", schema: "services", table: "professional_services");
            migrationBuilder.DropColumn(name: "languages", schema: "professionals", table: "professionals");
            migrationBuilder.DropColumn(name: "slot_step_minutes", schema: "shops", table: "shops");
            migrationBuilder.DropColumn(name: "min_lead_time_minutes", schema: "shops", table: "shops");
            migrationBuilder.DropColumn(name: "buffer_minutes", schema: "shops", table: "shops");
            migrationBuilder.DropColumn(name: "booking_horizon_days", schema: "shops", table: "shops");
        }
    }
}
