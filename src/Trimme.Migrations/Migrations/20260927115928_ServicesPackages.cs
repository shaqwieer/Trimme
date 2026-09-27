using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ServicesPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "services");

            migrationBuilder.CreateTable(
                name: "service_categories",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name_en = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    icon = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_packages",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name_en = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    description_ar = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description_en = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    moderation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    moderation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_packages", x => x.id);
                    table.UniqueConstraint("ak_service_packages_shop_id_id", x => new { x.shop_id, x.id });
                    table.CheckConstraint("ck_service_packages_duration", "duration_minutes BETWEEN 5 AND 480 AND duration_minutes % 5 = 0");
                    table.CheckConstraint("ck_service_packages_price", "price >= 0");
                    table.ForeignKey(
                        name: "fk_service_packages_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shop_services",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name_en = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    description_ar = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description_en = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    online_bookable = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    moderation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    moderation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_services", x => x.id);
                    table.UniqueConstraint("ak_shop_service_shop_id_id", x => new { x.shop_id, x.id });
                    table.CheckConstraint("ck_shop_services_duration", "duration_minutes BETWEEN 5 AND 480 AND duration_minutes % 5 = 0");
                    table.CheckConstraint("ck_shop_services_price", "price >= 0");
                    table.ForeignKey(
                        name: "fk_shop_services_service_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "services",
                        principalTable: "service_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shop_services_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "professional_services",
                schema: "services",
                columns: table => new
                {
                    professional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_professional_services", x => new { x.professional_id, x.service_id });
                    table.ForeignKey(
                        name: "fk_professional_services_professionals_shop_id_professional_id",
                        columns: x => new { x.shop_id, x.professional_id },
                        principalSchema: "professionals",
                        principalTable: "professionals",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_professional_services_shop_service_shop_id_service_id",
                        columns: x => new { x.shop_id, x.service_id },
                        principalSchema: "services",
                        principalTable: "shop_services",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_professional_services_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_package_items",
                schema: "services",
                columns: table => new
                {
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_package_items", x => new { x.package_id, x.service_id });
                    table.ForeignKey(
                        name: "fk_service_package_items_service_packages_shop_id_package_id",
                        columns: x => new { x.shop_id, x.package_id },
                        principalSchema: "services",
                        principalTable: "service_packages",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_service_package_items_shop_service_shop_id_service_id",
                        columns: x => new { x.shop_id, x.service_id },
                        principalSchema: "services",
                        principalTable: "shop_services",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_package_items_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_professional_services_shop_id_professional_id",
                schema: "services",
                table: "professional_services",
                columns: new[] { "shop_id", "professional_id" });

            migrationBuilder.CreateIndex(
                name: "ix_professional_services_shop_id_service_id",
                schema: "services",
                table: "professional_services",
                columns: new[] { "shop_id", "service_id" });

            migrationBuilder.CreateIndex(
                name: "ix_service_categories_display_order",
                schema: "services",
                table: "service_categories",
                column: "display_order");

            migrationBuilder.CreateIndex(
                name: "ix_service_package_items_shop_id_package_id",
                schema: "services",
                table: "service_package_items",
                columns: new[] { "shop_id", "package_id" });

            migrationBuilder.CreateIndex(
                name: "ix_service_package_items_shop_id_service_id",
                schema: "services",
                table: "service_package_items",
                columns: new[] { "shop_id", "service_id" });

            migrationBuilder.CreateIndex(
                name: "ix_service_packages_shop_id_display_order",
                schema: "services",
                table: "service_packages",
                columns: new[] { "shop_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_shop_services_category_id",
                schema: "services",
                table: "shop_services",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_shop_services_shop_id_display_order",
                schema: "services",
                table: "shop_services",
                columns: new[] { "shop_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_shop_services_shop_id_is_archived_is_active",
                schema: "services",
                table: "shop_services",
                columns: new[] { "shop_id", "is_archived", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "professional_services",
                schema: "services");

            migrationBuilder.DropTable(
                name: "service_package_items",
                schema: "services");

            migrationBuilder.DropTable(
                name: "service_packages",
                schema: "services");

            migrationBuilder.DropTable(
                name: "shop_services",
                schema: "services");

            migrationBuilder.DropTable(
                name: "service_categories",
                schema: "services");
        }
    }
}
