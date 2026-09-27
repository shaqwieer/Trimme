using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ShopProfileLocationProfessionals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "media");

            migrationBuilder.EnsureSchema(
                name: "professionals");

            migrationBuilder.AddColumn<string>(
                name: "address_line",
                schema: "shops",
                table: "shops",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "amenities",
                schema: "shops",
                table: "shops",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "shops",
                table: "shops",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Barbershop");

            migrationBuilder.AddColumn<string>(
                name: "city",
                schema: "shops",
                table: "shops",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cover_media_id",
                schema: "shops",
                table: "shops",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description_ar",
                schema: "shops",
                table: "shops",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description_en",
                schema: "shops",
                table: "shops",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "district",
                schema: "shops",
                table: "shops",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "editable_fields",
                schema: "shops",
                table: "shops",
                type: "text[]",
                nullable: false,
                defaultValue: new[] { "Amenities", "Cover", "Description", "Gallery", "Logo", "PublicPhone" });

            migrationBuilder.AddColumn<string>(
                name: "formatted_address",
                schema: "shops",
                table: "shops",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "gallery_media_ids",
                schema: "shops",
                table: "shops",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<bool>(
                name: "is_verified",
                schema: "shops",
                table: "shops",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Point>(
                name: "location",
                schema: "shops",
                table: "shops",
                type: "geography (point, 4326)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "location_confirmed_at",
                schema: "shops",
                table: "shops",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "location_confirmed_by",
                schema: "shops",
                table: "shops",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location_source",
                schema: "shops",
                table: "shops",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "logo_media_id",
                schema: "shops",
                table: "shops",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "public_phone",
                schema: "shops",
                table: "shops",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "media_files",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    content_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "professionals",
                schema: "professionals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name_en = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    specialty_ar = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    specialty_en = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    bio_ar = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    bio_en = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    avatar_media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_professionals", x => x.id);
                    table.UniqueConstraint("ak_professionals_shop_id_id", x => new { x.shop_id, x.id });
                    table.ForeignKey(
                        name: "fk_professionals_media_files_avatar_media_id",
                        column: x => x.avatar_media_id,
                        principalSchema: "media",
                        principalTable: "media_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_professionals_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "professional_contacts",
                schema: "professionals",
                columns: table => new
                {
                    professional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_whatsapp = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    whatsapp_lookup_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    whatsapp_masked = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    notifications_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    whatsapp_verification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_professional_contacts", x => x.professional_id);
                    table.ForeignKey(
                        name: "fk_professional_contacts_professionals_shop_id_professional_id",
                        columns: x => new { x.shop_id, x.professional_id },
                        principalSchema: "professionals",
                        principalTable: "professionals",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_professional_contacts_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shops_cover_media_id",
                schema: "shops",
                table: "shops",
                column: "cover_media_id");

            migrationBuilder.CreateIndex(
                name: "ix_shops_location",
                schema: "shops",
                table: "shops",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_shops_logo_media_id",
                schema: "shops",
                table: "shops",
                column: "logo_media_id");

            migrationBuilder.CreateIndex(
                name: "ix_professional_contacts_shop_id_professional_id",
                schema: "professionals",
                table: "professional_contacts",
                columns: new[] { "shop_id", "professional_id" });

            migrationBuilder.CreateIndex(
                name: "ix_professional_contacts_whatsapp_lookup_hash",
                schema: "professionals",
                table: "professional_contacts",
                column: "whatsapp_lookup_hash",
                unique: true,
                filter: "whatsapp_lookup_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_professionals_avatar_media_id",
                schema: "professionals",
                table: "professionals",
                column: "avatar_media_id");

            migrationBuilder.CreateIndex(
                name: "ix_professionals_shop_id_slug",
                schema: "professionals",
                table: "professionals",
                columns: new[] { "shop_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_professionals_shop_id_status",
                schema: "professionals",
                table: "professionals",
                columns: new[] { "shop_id", "status" });

            migrationBuilder.AddForeignKey(
                name: "fk_shops_media_files_cover_media_id",
                schema: "shops",
                table: "shops",
                column: "cover_media_id",
                principalSchema: "media",
                principalTable: "media_files",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_shops_media_files_logo_media_id",
                schema: "shops",
                table: "shops",
                column: "logo_media_id",
                principalSchema: "media",
                principalTable: "media_files",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_shops_media_files_cover_media_id",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropForeignKey(
                name: "fk_shops_media_files_logo_media_id",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropTable(
                name: "professional_contacts",
                schema: "professionals");

            migrationBuilder.DropTable(
                name: "professionals",
                schema: "professionals");

            migrationBuilder.DropTable(
                name: "media_files",
                schema: "media");

            migrationBuilder.DropIndex(
                name: "ix_shops_cover_media_id",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropIndex(
                name: "ix_shops_location",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropIndex(
                name: "ix_shops_logo_media_id",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "address_line",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "amenities",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "category",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "city",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "cover_media_id",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "description_ar",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "description_en",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "district",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "editable_fields",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "formatted_address",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "gallery_media_ids",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "is_verified",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "location",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "location_confirmed_at",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "location_confirmed_by",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "location_source",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "logo_media_id",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "public_phone",
                schema: "shops",
                table: "shops");
        }
    }
}
