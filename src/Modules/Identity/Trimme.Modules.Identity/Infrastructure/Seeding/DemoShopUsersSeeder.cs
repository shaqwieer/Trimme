using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;

namespace Trimme.Modules.Identity.Infrastructure.Seeding;

/// <summary>
/// An owner and a staff account for each demo shop (idempotent; development only). The password comes from
/// <c>TRIMME_DEMO_PASSWORD</c>, defaulting to a documented local-only value.
/// </summary>
internal sealed class DemoShopUsersSeeder : IDevSeeder
{
    private static readonly DateTimeOffset SeededAt = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    public int Order => 210;

    public string Name => "identity-demo-shop-users";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var password = services.GetRequiredService<IConfiguration>()[DemoData.PasswordVariable] is { Length: > 0 } configured
            ? configured
            : DemoData.DefaultPassword;

        foreach (var shop in DemoData.Shops)
        {
            await EnsureAsync(users, shop, shop.OwnerEmail, "مالك المحل", SystemRoles.ShopOwner, password);
            await EnsureAsync(users, shop, shop.StaffEmail, "موظف الاستقبال", SystemRoles.ShopStaff, password);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static async Task EnsureAsync(UserManager<ApplicationUser> users, DemoShop shop, string email, string name, string role, string password)
    {
        if (await users.FindByEmailAsync(email) is not null)
        {
            return;
        }

        // Deterministic ids from the email, so repeated seeds on fresh databases produce identical rows.
        var id = new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(email))[..16]);
        var user = new ApplicationUser
        {
            Id = id,
            UserName = $"staff-{id:N}",
            Email = email,
            EmailConfirmed = true,
            DisplayName = name,
            UserType = UserType.ShopUser,
            ShopId = shop.Id,
            CreatedAt = SeededAt,
            LockoutEnabled = true,
        };

        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded || !(await users.AddToRoleAsync(user, role)).Succeeded)
        {
            throw new InvalidOperationException($"Demo shop user could not be created: {string.Join(", ", created.Errors.Select(e => e.Code))}");
        }
    }
}
