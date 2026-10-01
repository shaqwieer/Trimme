using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_users_user_type_created_at",
                schema: "identity",
                table: "users",
                columns: new[] { "user_type", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_status_created_at",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_starts_at",
                schema: "bookings",
                table: "bookings",
                column: "starts_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_user_type_created_at",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_reviews_status_created_at",
                schema: "reviews",
                table: "reviews");

            migrationBuilder.DropIndex(
                name: "ix_bookings_starts_at",
                schema: "bookings",
                table: "bookings");
        }
    }
}
