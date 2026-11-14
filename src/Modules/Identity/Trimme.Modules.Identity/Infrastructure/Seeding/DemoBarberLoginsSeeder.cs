using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Domain.Privacy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;

namespace Trimme.Modules.Identity.Infrastructure.Seeding;

/// <summary>
/// A login for each demo barber (D-145; idempotent; development only): their demo WhatsApp number and the demo password,
/// already replaced (no temporary password), so the local stack and the browser tests can sign in as a barber at once.
/// Runs after the demo professionals (order 250).
/// </summary>
internal sealed class DemoBarberLoginsSeeder : IDevSeeder
{
    private static readonly DateTimeOffset SeededAt = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    public int Order => 260;

    public string Name => "identity-demo-barber-logins";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<TrimmeDbContext>();
        var protector = services.GetRequiredService<Trimme.BuildingBlocks.Application.Privacy.IPersonalDataProtector>();
        var password = services.GetRequiredService<IConfiguration>()[DemoData.PasswordVariable] is { Length: > 0 } configured
            ? configured
            : DemoData.DefaultPassword;

        foreach (var barber in DemoData.Professionals)
        {
            if (await db.Set<ApplicationUser>().AnyAsync(u => u.ProfessionalId == barber.Id, cancellationToken))
            {
                continue;
            }

            // Deterministic ids from the professional, so repeated seeds on fresh databases produce identical rows.
            var id = new Guid(System.Security.Cryptography.SHA256.HashData(barber.Id.ToByteArray())[..16]);
            var user = new ApplicationUser
            {
                Id = id,
                UserName = $"barber-{id:N}",
                DisplayName = barber.NameAr,
                UserType = UserType.ShopUser,
                ShopId = barber.ShopId,
                ProfessionalId = barber.Id,
                LoginPhoneHash = PhoneNumber.TryParseMobile(barber.WhatsApp, out var number)
                    ? protector.LookupHash(number.E164, PersonalDataPurposes.BarberLogin)
                    : null,
                CreatedAt = SeededAt,
                LockoutEnabled = true,
            };

            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded || !(await users.AddToRoleAsync(user, SystemRoles.ShopBarber)).Succeeded)
            {
                throw new InvalidOperationException($"Demo barber login could not be created: {string.Join(", ", created.Errors.Select(e => e.Code))}");
            }
        }
    }
}
