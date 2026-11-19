using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ShopShortName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "short_name_ar",
                schema: "shops",
                table: "shops",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "short_name_en",
                schema: "shops",
                table: "shops",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "short_name_ar",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "short_name_en",
                schema: "shops",
                table: "shops");
        }
    }
}
