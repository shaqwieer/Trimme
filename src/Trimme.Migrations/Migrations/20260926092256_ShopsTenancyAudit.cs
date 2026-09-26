using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ShopsTenancyAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "administration");

            migrationBuilder.EnsureSchema(
                name: "shops");

            migrationBuilder.AddColumn<Guid>(
                name: "shop_id",
                schema: "identity",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "administration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shops",
                schema: "shops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name_en = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    require_manual_confirmation = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shops", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_shop_id",
                schema: "identity",
                table: "users",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_shop_id",
                schema: "identity",
                table: "invitations",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_entity_type_entity_id",
                schema: "administration",
                table: "audit_entries",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_occurred_at",
                schema: "administration",
                table: "audit_entries",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_shop_id",
                schema: "administration",
                table: "audit_entries",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_shops_slug",
                schema: "shops",
                table: "shops",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shops_status",
                schema: "shops",
                table: "shops",
                column: "status");

            migrationBuilder.AddForeignKey(
                name: "fk_invitations_shops_shop_id",
                schema: "identity",
                table: "invitations",
                column: "shop_id",
                principalSchema: "shops",
                principalTable: "shops",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_shops_shop_id",
                schema: "identity",
                table: "users",
                column: "shop_id",
                principalSchema: "shops",
                principalTable: "shops",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_invitations_shops_shop_id",
                schema: "identity",
                table: "invitations");

            migrationBuilder.DropForeignKey(
                name: "fk_users_shops_shop_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "administration");

            migrationBuilder.DropTable(
                name: "shops",
                schema: "shops");

            migrationBuilder.DropIndex(
                name: "ix_users_shop_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_invitations_shop_id",
                schema: "identity",
                table: "invitations");

            migrationBuilder.DropColumn(
                name: "shop_id",
                schema: "identity",
                table: "users");
        }
    }
}
