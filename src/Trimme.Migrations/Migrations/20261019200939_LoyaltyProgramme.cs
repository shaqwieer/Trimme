using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class LoyaltyProgramme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "loyalty");

            migrationBuilder.AddColumn<Guid>(
                name: "loyalty_reward_id",
                schema: "bookings",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "loyalty_reward_title_ar",
                schema: "bookings",
                table: "bookings",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "loyalty_reward_title_en",
                schema: "bookings",
                table: "bookings",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "programs",
                schema: "loyalty",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    required_visits = table.Column<int>(type: "integer", nullable: false),
                    reward_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    discount_percent = table.Column<int>(type: "integer", nullable: true),
                    reward_title_ar = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    reward_title_en = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    reward_validity_days = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_programs", x => x.id);
                    table.CheckConstraint("ck_programs_discount_percent", "discount_percent IS NULL OR discount_percent BETWEEN 1 AND 100");
                    table.CheckConstraint("ck_programs_required_visits", "required_visits BETWEEN 2 AND 20");
                    table.ForeignKey(
                        name: "fk_programs_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rewards",
                schema: "loyalty",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    discount_percent = table.Column<int>(type: "integer", nullable: true),
                    title_ar = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    title_en = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    redeemed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rewards", x => x.id);
                    table.ForeignKey(
                        name: "fk_rewards_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stamps",
                schema: "loyalty",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    earned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reward_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stamps", x => x.id);
                    table.ForeignKey(
                        name: "fk_stamps_rewards_reward_id",
                        column: x => x.reward_id,
                        principalSchema: "loyalty",
                        principalTable: "rewards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stamps_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_programs_shop_id",
                schema: "loyalty",
                table: "programs",
                column: "shop_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rewards_booking_id",
                schema: "loyalty",
                table: "rewards",
                column: "booking_id",
                filter: "booking_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_rewards_code",
                schema: "loyalty",
                table: "rewards",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rewards_customer_id",
                schema: "loyalty",
                table: "rewards",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_rewards_shop_id_customer_id_status",
                schema: "loyalty",
                table: "rewards",
                columns: new[] { "shop_id", "customer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stamps_booking_id",
                schema: "loyalty",
                table: "stamps",
                column: "booking_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stamps_customer_id",
                schema: "loyalty",
                table: "stamps",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_stamps_reward_id",
                schema: "loyalty",
                table: "stamps",
                column: "reward_id");

            migrationBuilder.CreateIndex(
                name: "ix_stamps_shop_id_customer_id_reward_id",
                schema: "loyalty",
                table: "stamps",
                columns: new[] { "shop_id", "customer_id", "reward_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "programs",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "stamps",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "rewards",
                schema: "loyalty");

            migrationBuilder.DropColumn(
                name: "loyalty_reward_id",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "loyalty_reward_title_ar",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "loyalty_reward_title_en",
                schema: "bookings",
                table: "bookings");
        }
    }
}
