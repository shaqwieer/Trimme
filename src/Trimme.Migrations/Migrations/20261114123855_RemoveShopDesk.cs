using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimme.Migrations.Migrations
{
    /// <summary>
    /// D-145: the shop desk role (ShopStaff) is removed; each barber signs in as their own desk. Every desk account is
    /// disabled and its sessions revoked (the access cookie is checked against its session on every request, so it stops
    /// at the next request; refresh tokens belong to the revoked sessions), its pending invitations are revoked, and the
    /// role, its grants and memberships are deleted. Owners and barbers are untouched. Not reversible: the down step does
    /// nothing (the role and its memberships are not restored).
    /// </summary>
    public partial class RemoveShopDesk : Migration
    {
        /// <summary>The whole change, as one script (an integration test runs it against desk, owner and barber rows).</summary>
        public const string Sql = """
            UPDATE identity.users SET disabled_at = now()
            WHERE disabled_at IS NULL AND id IN (
                SELECT ur.user_id FROM identity.user_roles ur JOIN identity.roles r ON r.id = ur.role_id WHERE r.name = 'ShopStaff');

            UPDATE identity.user_sessions SET revoked_at = now(), revocation_reason = 'LoginDisabled'
            WHERE revoked_at IS NULL AND user_id IN (
                SELECT ur.user_id FROM identity.user_roles ur JOIN identity.roles r ON r.id = ur.role_id WHERE r.name = 'ShopStaff');

            UPDATE identity.invitations SET revoked_at = now()
            WHERE role_name = 'ShopStaff' AND accepted_at IS NULL AND revoked_at IS NULL;

            DELETE FROM identity.user_roles WHERE role_id IN (SELECT id FROM identity.roles WHERE name = 'ShopStaff');
            DELETE FROM identity.role_permissions WHERE role_id IN (SELECT id FROM identity.roles WHERE name = 'ShopStaff');
            DELETE FROM identity.role_claims WHERE role_id IN (SELECT id FROM identity.roles WHERE name = 'ShopStaff');
            DELETE FROM identity.roles WHERE name = 'ShopStaff';
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the desk accounts stay disabled and the role is not recreated (D-145).
        }
    }
}
