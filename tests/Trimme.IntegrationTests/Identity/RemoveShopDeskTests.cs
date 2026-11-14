using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.IntegrationTests.Tenancy;
using Trimme.Migrations.Migrations;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;

namespace Trimme.IntegrationTests.Identity;

/// <summary>
/// D-145: the migration that removes the shop desk. Test databases are migrated from empty, so no desk exists when the
/// migration itself runs; this test rebuilds a desk as production has it (the role, its grant, an account signed in, a
/// pending invitation) next to an owner and a barber's login, runs the migration's script, and checks that only the
/// desk changed.
/// </summary>
public sealed class RemoveShopDeskTests(PostgresFixture postgres)
{
    [Fact]
    public async Task TheScript_DisablesTheDeskAndEndsItsSessions_RevokesItsInvitations_AndDeletesTheRole_OnlyTheDesk()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "remove_desk", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        using var owner = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        using var barber = await BarberLoginTests.NewBarberAsync(factory, admin, owner, shops.A.ShopId, "+966512389001", ct);

        // The desk as it was: the role with a grant, an account signed in, and a pending invitation.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var role = new ApplicationRole { Id = Guid.CreateVersion7(), Name = "ShopStaff", UserType = UserType.ShopUser };
            (await roles.CreateAsync(role)).Succeeded.ShouldBeTrue();
            var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
            db.Add(new RolePermission(role.Id, Permissions.Shop.BookingsRead));
            await db.SaveChangesAsync(ct);
        }

        var deskEmail = IdentityTestData.NewEmail("desk");
        await ShopTestData.CreateShopUserAsync(factory, shops.A.ShopId, deskEmail, "ShopStaff", ct);
        using var desk = await IdentityTestData.SignInStaffAsync(factory, deskEmail, ct);
        (await desk.GetAsync("/api/v1/shop/bookings", ct)).StatusCode.ShouldBe(HttpStatusCode.OK, "the desk works before the migration");
        var deskId = (Guid)(await ScalarAsync(factory, "SELECT id FROM identity.users WHERE email = @p", ct, deskEmail))!;
        await InviteAsync(factory, "ShopStaff", shops.A.ShopId, deskId, ct);
        await InviteAsync(factory, SystemRoles.ShopOwner, shops.A.ShopId, deskId, ct);

        await ExecuteAsync(factory, RemoveShopDesk.Sql, ct);

        // The desk: disabled, every session revoked (its cookie stops at once), its invitation revoked, the role gone.
        (await ScalarAsync(factory, "SELECT disabled_at IS NOT NULL FROM identity.users WHERE id = @p", ct, deskId)).ShouldBe(true);
        (await ScalarAsync(factory, "SELECT count(*) FROM identity.user_sessions WHERE user_id = @p AND revoked_at IS NULL", ct, deskId)).ShouldBe(0L);
        (await ScalarAsync(factory, "SELECT count(*) FROM identity.user_sessions WHERE user_id = @p AND revocation_reason = 'LoginDisabled'", ct, deskId))
            .ShouldBe(1L);
        (await desk.GetAsync("/api/v1/me", ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ScalarAsync(factory, "SELECT count(*) FROM identity.invitations WHERE role_name = 'ShopStaff' AND revoked_at IS NULL", ct)).ShouldBe(0L);
        (await ScalarAsync(factory, "SELECT count(*) FROM identity.roles WHERE name = 'ShopStaff'", ct)).ShouldBe(0L);
        (await ScalarAsync(factory, "SELECT count(*) FROM identity.user_roles WHERE user_id = @p", ct, deskId)).ShouldBe(0L);

        // Everyone else: untouched.
        (await ScalarAsync(factory, "SELECT count(*) FROM identity.invitations WHERE role_name = 'ShopOwner' AND revoked_at IS NULL", ct)).ShouldBe(1L);
        (await owner.GetAsync("/api/v1/shop/bookings", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await barber.GetAsync("/api/v1/shop/bookings", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ScalarAsync(factory, "SELECT count(*) FROM identity.users WHERE user_type = 'ShopUser' AND disabled_at IS NOT NULL", ct)).ShouldBe(1L);

        // Running it again changes nothing, and the next migrate does not bring the role back.
        await ExecuteAsync(factory, RemoveShopDesk.Sql, ct);
        await factory.MigrateAsync(ct);
        (await ScalarAsync(factory, "SELECT count(*) FROM identity.roles WHERE name = 'ShopStaff'", ct)).ShouldBe(0L);
        (await ScalarAsync(factory, "SELECT count(*) FROM identity.roles WHERE name = 'ShopBarber'", ct)).ShouldBe(1L);
    }

    private static async Task InviteAsync(TrimmeApiFactory factory, string role, Guid shopId, Guid invitedBy, CancellationToken ct)
    {
        var email = IdentityTestData.NewEmail("invited");
        await ExecuteAsync(factory,
            """
            INSERT INTO identity.invitations (id, email, normalized_email, user_type, role_name, shop_id, token_hash, locale, invited_by_user_id, created_at, expires_at)
            VALUES (gen_random_uuid(), @email, upper(@email), 'ShopUser', @role, @shop, md5(random()::text), 'ar', @by, now(), now() + interval '7 days')
            """,
            ct, ("email", email), ("role", role), ("shop", shopId), ("by", invitedBy));
    }

    private static async Task ExecuteAsync(TrimmeApiFactory factory, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> ScalarAsync(TrimmeApiFactory factory, string sql, CancellationToken ct, object? parameter = null)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        if (parameter is not null)
        {
            command.Parameters.AddWithValue("p", parameter);
        }

        return await command.ExecuteScalarAsync(ct);
    }
}
