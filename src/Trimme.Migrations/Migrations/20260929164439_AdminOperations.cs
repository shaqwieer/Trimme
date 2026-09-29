using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AdminOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "flag_reason",
                schema: "reviews",
                table: "reviews",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "flagged_at",
                schema: "reviews",
                table: "reviews",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "moderated_at",
                schema: "reviews",
                table: "reviews",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "moderation_reason",
                schema: "reviews",
                table: "reviews",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "reviews",
                table: "reviews",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            // The audit list pages by insertion order (D-104). Existing entries are numbered in time order first, then the
            // column becomes an identity that continues after them.
            migrationBuilder.Sql("""
                ALTER TABLE administration.audit_entries ADD COLUMN sequence bigint;
                UPDATE administration.audit_entries a SET sequence = o.n
                FROM (SELECT id, row_number() OVER (ORDER BY occurred_at, id) AS n FROM administration.audit_entries) o
                WHERE a.id = o.id;
                ALTER TABLE administration.audit_entries ALTER COLUMN sequence SET NOT NULL;
                ALTER TABLE administration.audit_entries ALTER COLUMN sequence ADD GENERATED ALWAYS AS IDENTITY;
                SELECT setval(pg_get_serial_sequence('administration.audit_entries', 'sequence'),
                              COALESCE((SELECT max(sequence) FROM administration.audit_entries), 0) + 1, false);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_action_sequence",
                schema: "administration",
                table: "audit_entries",
                columns: new[] { "action", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_actor_user_id_sequence",
                schema: "administration",
                table: "audit_entries",
                columns: new[] { "actor_user_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_sequence",
                schema: "administration",
                table: "audit_entries",
                column: "sequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_entries_action_sequence",
                schema: "administration",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_actor_user_id_sequence",
                schema: "administration",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_sequence",
                schema: "administration",
                table: "audit_entries");

            migrationBuilder.DropColumn(
                name: "flag_reason",
                schema: "reviews",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "flagged_at",
                schema: "reviews",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "moderated_at",
                schema: "reviews",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "moderation_reason",
                schema: "reviews",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "reviews",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "sequence",
                schema: "administration",
                table: "audit_entries");
        }
    }
}
