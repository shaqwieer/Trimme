using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Reviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reviews");

            migrationBuilder.CreateTable(
                name: "rating_aggregates",
                schema: "reviews",
                columns: table => new
                {
                    subject = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    sum = table.Column<int>(type: "integer", nullable: false),
                    stars1 = table.Column<int>(type: "integer", nullable: false),
                    stars2 = table.Column<int>(type: "integer", nullable: false),
                    stars3 = table.Column<int>(type: "integer", nullable: false),
                    stars4 = table.Column<int>(type: "integer", nullable: false),
                    stars5 = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rating_aggregates", x => new { x.subject, x.subject_id });
                    table.CheckConstraint("ck_rating_aggregates_count", "count >= 0 AND count = stars1 + stars2 + stars3 + stars4 + stars5");
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                schema: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    author_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    item_name_ar = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    item_name_en = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reviews", x => x.id);
                    table.CheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_reviews_professionals_shop_id_professional_id",
                        columns: x => new { x.shop_id, x.professional_id },
                        principalSchema: "professionals",
                        principalTable: "professionals",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rating_aggregates_shop_id",
                schema: "reviews",
                table: "rating_aggregates",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_booking_id",
                schema: "reviews",
                table: "reviews",
                column: "booking_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reviews_customer_id",
                schema: "reviews",
                table: "reviews",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_professional_id_status_created_at",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "professional_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_shop_id_professional_id",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "shop_id", "professional_id" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_shop_id_status_created_at",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "shop_id", "status", "created_at" });

            // A review's booking belongs to the same shop (D-092). The booking's key type is the Bookings module's, so the
            // model keeps a plain id and the database enforces the same-shop reference.
            migrationBuilder.Sql("""
                ALTER TABLE reviews.reviews
                    ADD CONSTRAINT fk_reviews_bookings_shop_id_booking_id
                    FOREIGN KEY (shop_id, booking_id) REFERENCES bookings.bookings (shop_id, id) ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE reviews.reviews DROP CONSTRAINT IF EXISTS fk_reviews_bookings_shop_id_booking_id;");

            migrationBuilder.DropTable(
                name: "rating_aggregates",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "reviews",
                schema: "reviews");
        }
    }
}
