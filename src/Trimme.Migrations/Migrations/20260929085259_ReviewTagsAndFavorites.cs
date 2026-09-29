using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ReviewTagsAndFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "customers");

            migrationBuilder.AddColumn<string[]>(
                name: "tags",
                schema: "reviews",
                table: "reviews",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.CreateTable(
                name: "favorites",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_favorites", x => x.id);
                    table.ForeignKey(
                        name: "fk_favorites_professionals_shop_id_professional_id",
                        columns: x => new { x.shop_id, x.professional_id },
                        principalSchema: "professionals",
                        principalTable: "professionals",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_favorites_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_favorites_customer_id_professional_id",
                schema: "customers",
                table: "favorites",
                columns: new[] { "customer_id", "professional_id" },
                unique: true,
                filter: "professional_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_favorites_customer_id_shop_id_shop",
                schema: "customers",
                table: "favorites",
                columns: new[] { "customer_id", "shop_id" },
                unique: true,
                filter: "professional_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_favorites_shop_id_professional_id",
                schema: "customers",
                table: "favorites",
                columns: new[] { "shop_id", "professional_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "favorites",
                schema: "customers");

            migrationBuilder.DropColumn(
                name: "tags",
                schema: "reviews",
                table: "reviews");
        }
    }
}
