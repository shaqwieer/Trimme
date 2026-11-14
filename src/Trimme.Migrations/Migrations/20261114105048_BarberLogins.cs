using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class BarberLogins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "login_phone_hash",
                schema: "identity",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "professional_id",
                schema: "identity",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_login_phone_hash",
                schema: "identity",
                table: "users",
                column: "login_phone_hash",
                unique: true,
                filter: "login_phone_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_professional_id",
                schema: "identity",
                table: "users",
                column: "professional_id",
                unique: true,
                filter: "professional_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_login_phone_hash",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_professional_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "login_phone_hash",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "professional_id",
                schema: "identity",
                table: "users");
        }
    }
}
