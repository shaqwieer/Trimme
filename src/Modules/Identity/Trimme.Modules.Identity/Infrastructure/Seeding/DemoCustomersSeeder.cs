using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Privacy;
using IPersonalDataProtector = Trimme.BuildingBlocks.Application.Privacy.IPersonalDataProtector;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;

namespace Trimme.Modules.Identity.Infrastructure.Seeding;

/// <summary>
/// Demo customers (spec §20): fixed ids, names and fake mobiles, stored like real sign-ups (encrypted mobile plus the
/// keyed lookup hash, D-050). Idempotent; development only. They sign in through the development OTP inbox.
/// </summary>
internal sealed class DemoCustomersSeeder : IDevSeeder
{
    private static readonly DateTimeOffset SeededAt = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>After the roles (100) and before bookings (400).</summary>
    public int Order => 220;

    public string Name => "identity-demo-customers";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var protector = services.GetRequiredService<IPersonalDataProtector>();
        foreach (var customer in DemoCustomers.All)
        {
            if (await users.FindByIdAsync(customer.Id.ToString()) is not null)
            {
                continue;
            }

            var user = new ApplicationUser
            {
                Id = customer.Id,
                UserName = $"customer-{customer.Id:N}",
                UserType = UserType.Customer,
                DisplayName = customer.Name,
                PhoneLookupHash = protector.LookupHash(customer.Mobile, PersonalDataPurposes.MobileNumber),
                ProtectedPhone = protector.Protect(customer.Mobile, PersonalDataPurposes.MobileNumber),
                PhoneNumberConfirmed = true,
                PreferredLocale = "ar",
                TermsAcceptedAt = SeededAt,
                CreatedAt = SeededAt,
                LockoutEnabled = true,
            };
            var created = await users.CreateAsync(user);
            if (!created.Succeeded || !(await users.AddToRoleAsync(user, SystemRoles.Customer)).Succeeded)
            {
                throw new InvalidOperationException($"Could not seed the demo customer {customer.Id}.");
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
