using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dead_lettered_at",
                schema: "infra",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at",
                schema: "infra",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "processed_messages",
                schema: "infra",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_messages", x => new { x.message_id, x.consumer });
                });

            migrationBuilder.CreateTable(
                name: "reminder_schedules",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    job_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminder_schedules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shop_notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    dedupe_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    parameters = table.Column<string>(type: "jsonb", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    read_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_shop_notifications_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shops",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    dedupe_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    parameters = table.Column<string>(type: "jsonb", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_templates",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(40)", maxLength: 40, nullable: false),
                    audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    locale = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    active_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_whatsapp_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_template_versions",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    provider_template_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    activated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    buttons = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_whatsapp_template_versions", x => x.id);
                    table.CheckConstraint("ck_whatsapp_template_versions_body", "char_length(body) BETWEEN 1 AND 1024");
                    table.ForeignKey(
                        name: "fk_whatsapp_template_versions_whatsapp_templates_template_id",
                        column: x => x.template_id,
                        principalSchema: "notifications",
                        principalTable: "whatsapp_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_dispatches",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dedupe_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    @event = table.Column<string>(name: "event", type: "character varying(40)", maxLength: 40, nullable: false),
                    audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    locale = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_version_number = table.Column<int>(type: "integer", nullable: false),
                    recipient_protected = table.Column<string>(type: "text", nullable: false),
                    recipient_masked = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    body = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    parameters = table.Column<List<string>>(type: "text[]", nullable: false),
                    provider_template_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_purged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    provider_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    job_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    buttons = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_whatsapp_dispatches", x => x.id);
                    table.ForeignKey(
                        name: "fk_whatsapp_dispatches_whats_app_template_template_id",
                        column: x => x.template_id,
                        principalSchema: "notifications",
                        principalTable: "whatsapp_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_whatsapp_dispatches_whats_app_template_version_template_ver",
                        column: x => x.template_version_id,
                        principalSchema: "notifications",
                        principalTable: "whatsapp_template_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_next_attempt_at",
                schema: "infra",
                table: "outbox_messages",
                column: "next_attempt_at",
                filter: "processed_at IS NULL AND dead_lettered_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at",
                schema: "infra",
                table: "outbox_messages",
                column: "processed_at",
                filter: "processed_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_processed_messages_processed_at",
                schema: "infra",
                table: "processed_messages",
                column: "processed_at");

            migrationBuilder.CreateIndex(
                name: "ix_reminder_schedules_booking_id",
                schema: "notifications",
                table: "reminder_schedules",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_reminder_schedules_booking_id_audience",
                schema: "notifications",
                table: "reminder_schedules",
                columns: new[] { "booking_id", "audience" },
                unique: true,
                filter: "status = 'Scheduled'");

            migrationBuilder.CreateIndex(
                name: "ix_reminder_schedules_status_due_at",
                schema: "notifications",
                table: "reminder_schedules",
                columns: new[] { "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shop_notifications_shop_id_created_at",
                schema: "notifications",
                table: "shop_notifications",
                columns: new[] { "shop_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shop_notifications_shop_id_dedupe_key",
                schema: "notifications",
                table: "shop_notifications",
                columns: new[] { "shop_id", "dedupe_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_notifications_unread",
                schema: "notifications",
                table: "shop_notifications",
                column: "shop_id",
                filter: "read_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_unread",
                schema: "notifications",
                table: "user_notifications",
                column: "user_id",
                filter: "read_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_user_id_created_at",
                schema: "notifications",
                table: "user_notifications",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_user_id_dedupe_key",
                schema: "notifications",
                table: "user_notifications",
                columns: new[] { "user_id", "dedupe_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_dispatches_booking_id",
                schema: "notifications",
                table: "whatsapp_dispatches",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_dispatches_created_at",
                schema: "notifications",
                table: "whatsapp_dispatches",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_dispatches_dedupe_key",
                schema: "notifications",
                table: "whatsapp_dispatches",
                column: "dedupe_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_dispatches_provider_message_id",
                schema: "notifications",
                table: "whatsapp_dispatches",
                column: "provider_message_id",
                filter: "provider_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_dispatches_shop_id_created_at",
                schema: "notifications",
                table: "whatsapp_dispatches",
                columns: new[] { "shop_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_dispatches_status_created_at",
                schema: "notifications",
                table: "whatsapp_dispatches",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_dispatches_template_id",
                schema: "notifications",
                table: "whatsapp_dispatches",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_dispatches_template_version_id",
                schema: "notifications",
                table: "whatsapp_dispatches",
                column: "template_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_template_versions_template_id_number",
                schema: "notifications",
                table: "whatsapp_template_versions",
                columns: new[] { "template_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_template_versions_template_id_status",
                schema: "notifications",
                table: "whatsapp_template_versions",
                columns: new[] { "template_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_templates_event_audience_locale",
                schema: "notifications",
                table: "whatsapp_templates",
                columns: new[] { "event", "audience", "locale" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_messages",
                schema: "infra");

            migrationBuilder.DropTable(
                name: "reminder_schedules",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "shop_notifications",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "user_notifications",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "whatsapp_dispatches",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "whatsapp_template_versions",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "whatsapp_templates",
                schema: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_next_attempt_at",
                schema: "infra",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_processed_at",
                schema: "infra",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "dead_lettered_at",
                schema: "infra",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                schema: "infra",
                table: "outbox_messages");
        }
    }
}
