using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class PauseUnavailableCallAuditCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // D-155: a paused shop stays listed (the setting goes); a pause gets a return time and who made it; the admin's
            // call button; who marked «غير متوفر اليوم»; the audit summary as a code with arguments (old rows keep null).
            migrationBuilder.DropColumn(
                name: "hide_paused_shops_from_discovery",
                schema: "administration",
                table: "platform_settings");

            migrationBuilder.AddColumn<bool>(
                name: "call_button_enabled",
                schema: "shops",
                table: "shops",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "call_button_phone",
                schema: "shops",
                table: "shops",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                schema: "availability",
                table: "professional_time_off",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "by_admin",
                schema: "shops",
                table: "online_booking_pauses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resumes_at",
                schema: "shops",
                table: "online_booking_pauses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary_args",
                schema: "administration",
                table: "audit_entries",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary_code",
                schema: "administration",
                table: "audit_entries",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_professional_time_off_shop_id_kind_starts_at",
                schema: "availability",
                table: "professional_time_off",
                columns: new[] { "shop_id", "kind", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_online_booking_pauses_resumes_at",
                schema: "shops",
                table: "online_booking_pauses",
                column: "resumes_at",
                filter: "resumes_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_professional_time_off_shop_id_kind_starts_at",
                schema: "availability",
                table: "professional_time_off");

            migrationBuilder.DropIndex(
                name: "ix_online_booking_pauses_resumes_at",
                schema: "shops",
                table: "online_booking_pauses");

            migrationBuilder.DropColumn(
                name: "call_button_enabled",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "call_button_phone",
                schema: "shops",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                schema: "availability",
                table: "professional_time_off");

            migrationBuilder.DropColumn(
                name: "by_admin",
                schema: "shops",
                table: "online_booking_pauses");

            migrationBuilder.DropColumn(
                name: "resumes_at",
                schema: "shops",
                table: "online_booking_pauses");

            migrationBuilder.DropColumn(
                name: "summary_args",
                schema: "administration",
                table: "audit_entries");

            migrationBuilder.DropColumn(
                name: "summary_code",
                schema: "administration",
                table: "audit_entries");

            migrationBuilder.AddColumn<bool>(
                name: "hide_paused_shops_from_discovery",
                schema: "administration",
                table: "platform_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }
    }
}
