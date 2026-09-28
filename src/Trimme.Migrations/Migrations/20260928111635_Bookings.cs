using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Bookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "bookings");

            migrationBuilder.CreateTable(
                name: "bookings",
                schema: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    reference = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_name_ar = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    professional_name_en = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_name_ar = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    item_name_en = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    channel = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    customer_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    payment_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount_due = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    during = table.Column<NpgsqlRange<DateTime>>(type: "tstzrange", nullable: false, computedColumnSql: "tstzrange(starts_at, ends_at, '[)')", stored: true),
                    package_items = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.UniqueConstraint("ak_bookings_shop_id_id", x => new { x.shop_id, x.id });
                    table.CheckConstraint("ck_bookings_item", "(service_id IS NULL) <> (package_id IS NULL)");
                    table.CheckConstraint("ck_bookings_range", "ends_at > starts_at");
                    table.ForeignKey(
                        name: "fk_bookings_professionals_shop_id_professional_id",
                        columns: x => new { x.shop_id, x.professional_id },
                        principalSchema: "professionals",
                        principalTable: "professionals",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                schema: "infra",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_records", x => new { x.user_id, x.scope, x.key });
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "infra",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "booking_history",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    from_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    to_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    previous_starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_booking_history_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "bookings",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "booking_notes",
                schema: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_booking_notes_bookings_shop_id_booking_id",
                        columns: x => new { x.shop_id, x.booking_id },
                        principalSchema: "bookings",
                        principalTable: "bookings",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_booking_notes_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_booking_history_booking_id",
                table: "booking_history",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_booking_notes_booking_id_created_at",
                schema: "bookings",
                table: "booking_notes",
                columns: new[] { "booking_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_booking_notes_shop_id_booking_id",
                schema: "bookings",
                table: "booking_notes",
                columns: new[] { "shop_id", "booking_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_customer_id_starts_at",
                schema: "bookings",
                table: "bookings",
                columns: new[] { "customer_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_professional_id_starts_at",
                schema: "bookings",
                table: "bookings",
                columns: new[] { "professional_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_reference",
                schema: "bookings",
                table: "bookings",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_shop_id_professional_id",
                schema: "bookings",
                table: "bookings",
                columns: new[] { "shop_id", "professional_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_shop_id_starts_at",
                schema: "bookings",
                table: "bookings",
                columns: new[] { "shop_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_expires_at",
                schema: "infra",
                table: "idempotency_records",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "infra",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            // R-BKG-03: two active bookings of one professional may never overlap. EF cannot express an exclusion
            // constraint; the status strings match the enum conversion (Pending, Confirmed, Arrived = BookingRules.Active).
            migrationBuilder.Sql("""
                ALTER TABLE bookings.bookings
                    ADD CONSTRAINT ex_bookings_professional_overlap
                    EXCLUDE USING gist (professional_id WITH =, during WITH &&)
                    WHERE (status IN ('Pending', 'Confirmed', 'Arrived'));
                """);

            // The booked service or package belongs to the booking's own shop (R-TEN-04). The snapshot keeps plain ids,
            // so these composite keys are added here rather than mapped.
            migrationBuilder.Sql("""
                ALTER TABLE bookings.bookings
                    ADD CONSTRAINT fk_bookings_shop_services_shop_id_service_id
                    FOREIGN KEY (shop_id, service_id) REFERENCES services.shop_services (shop_id, id) ON DELETE RESTRICT;
                ALTER TABLE bookings.bookings
                    ADD CONSTRAINT fk_bookings_service_packages_shop_id_package_id
                    FOREIGN KEY (shop_id, package_id) REFERENCES services.service_packages (shop_id, id) ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_history");

            migrationBuilder.DropTable(
                name: "booking_notes",
                schema: "bookings");

            migrationBuilder.DropTable(
                name: "idempotency_records",
                schema: "infra");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "infra");

            migrationBuilder.DropTable(
                name: "bookings",
                schema: "bookings");
        }
    }
}
