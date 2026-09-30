using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Qr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "qr");

            migrationBuilder.AddColumn<Guid>(
                name: "qr_link_id",
                schema: "bookings",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "qr_visit_id",
                schema: "bookings",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "qr_code_links",
                schema: "qr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    target_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: true),
                    label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qr_code_links", x => x.id);
                    table.UniqueConstraint("ak_qr_code_links_shop_id_id", x => new { x.shop_id, x.id });
                    table.CheckConstraint("ck_qr_code_links_target", "(target_type = 'Professional') = (professional_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_qr_code_links_professionals_shop_id_professional_id",
                        columns: x => new { x.shop_id, x.professional_id },
                        principalSchema: "professionals",
                        principalTable: "professionals",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_qr_code_links_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "qr_code_routes",
                schema: "qr",
                columns: table => new
                {
                    code = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    link_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qr_code_routes", x => x.code);
                    table.ForeignKey(
                        name: "fk_qr_code_routes_qr_code_links_link_id",
                        column: x => x.link_id,
                        principalSchema: "qr",
                        principalTable: "qr_code_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qr_visits",
                schema: "qr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    visitor_hash = table.Column<string>(type: "character(32)", fixedLength: true, maxLength: 32, nullable: false),
                    device = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    locale = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qr_visits", x => x.id);
                    table.ForeignKey(
                        name: "fk_qr_visits_qr_code_links_link_id",
                        column: x => x.link_id,
                        principalSchema: "qr",
                        principalTable: "qr_code_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_qr_link_id_created_at",
                schema: "bookings",
                table: "bookings",
                columns: new[] { "qr_link_id", "created_at" },
                filter: "qr_link_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_shop_id_qr_link_id",
                schema: "bookings",
                table: "bookings",
                columns: new[] { "shop_id", "qr_link_id" });

            migrationBuilder.CreateIndex(
                name: "ix_qr_code_links_code",
                schema: "qr",
                table: "qr_code_links",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_qr_code_links_shop_id_created_at",
                schema: "qr",
                table: "qr_code_links",
                columns: new[] { "shop_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_qr_code_links_shop_id_professional_id",
                schema: "qr",
                table: "qr_code_links",
                columns: new[] { "shop_id", "professional_id" });

            migrationBuilder.CreateIndex(
                name: "ix_qr_code_routes_link_id",
                schema: "qr",
                table: "qr_code_routes",
                column: "link_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_qr_visits_link_id_visited_at",
                schema: "qr",
                table: "qr_visits",
                columns: new[] { "link_id", "visited_at" });

            migrationBuilder.CreateIndex(
                name: "ix_qr_visits_shop_id_visited_at",
                schema: "qr",
                table: "qr_visits",
                columns: new[] { "shop_id", "visited_at" });

            migrationBuilder.CreateIndex(
                name: "ix_qr_visits_visited_at",
                schema: "qr",
                table: "qr_visits",
                column: "visited_at");

            migrationBuilder.AddForeignKey(
                name: "fk_bookings_qr_code_links_shop_id_qr_link_id",
                schema: "bookings",
                table: "bookings",
                columns: new[] { "shop_id", "qr_link_id" },
                principalSchema: "qr",
                principalTable: "qr_code_links",
                principalColumns: new[] { "shop_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_bookings_qr_code_links_shop_id_qr_link_id",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropTable(
                name: "qr_code_routes",
                schema: "qr");

            migrationBuilder.DropTable(
                name: "qr_visits",
                schema: "qr");

            migrationBuilder.DropTable(
                name: "qr_code_links",
                schema: "qr");

            migrationBuilder.DropIndex(
                name: "ix_bookings_qr_link_id_created_at",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "ix_bookings_shop_id_qr_link_id",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "qr_link_id",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "qr_visit_id",
                schema: "bookings",
                table: "bookings");
        }
    }
}
