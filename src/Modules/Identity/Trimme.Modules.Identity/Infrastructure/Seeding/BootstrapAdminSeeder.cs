using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;

namespace Trimme.Modules.Identity.Infrastructure.Seeding;

/// <summary>
/// One-time development bootstrap of the first SuperAdmin (spec §9, R-AUTH-03). It runs only through
/// <c>seed --dev</c> (Development + <c>TRIMME_ALLOW_DEV_SEED=true</c>), only when both
/// <c>TRIMME_BOOTSTRAP_ADMIN_EMAIL</c> and <c>TRIMME_BOOTSTRAP_ADMIN_PASSWORD</c> are set, and only while no
/// SuperAdmin exists. Production admins are invited, never seeded.
/// </summary>
internal sealed partial class BootstrapAdminSeeder : IDevSeeder
{
    public const string EmailVariable = "TRIMME_BOOTSTRAP_ADMIN_EMAIL";
    public const string PasswordVariable = "TRIMME_BOOTSTRAP_ADMIN_PASSWORD";

    public int Order => 100;

    public string Name => "identity-bootstrap-admin";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILogger<BootstrapAdminSeeder>>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var clock = services.GetRequiredService<TimeProvider>();

        var email = configuration[EmailVariable];
        var password = configuration[PasswordVariable];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            LogSkippedNoVariables(logger);
            return;
        }

        if ((await users.GetUsersInRoleAsync(SystemRoles.SuperAdmin)).Count > 0)
        {
            LogSkippedExists(logger);
            return;
        }

        var id = Guid.CreateVersion7();
        var admin = new ApplicationUser
        {
            Id = id,
            UserName = $"staff-{id:N}",
            Email = email.Trim(),
            EmailConfirmed = true,
            DisplayName = "مدير المنصة",
            UserType = UserType.PlatformAdmin,
            PreferredLocale = "ar",
            CreatedAt = clock.GetUtcNow(),
            LockoutEnabled = true,
        };

        var created = await users.CreateAsync(admin, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"Bootstrap admin could not be created: {string.Join(", ", created.Errors.Select(e => e.Code))}");
        }

        var role = await users.AddToRoleAsync(admin, SystemRoles.SuperAdmin);
        if (!role.Succeeded)
        {
            throw new InvalidOperationException("Bootstrap admin role could not be assigned. Run 'migrate' first.");
        }

        LogCreated(logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Bootstrap admin skipped: TRIMME_BOOTSTRAP_ADMIN_EMAIL/_PASSWORD are not set")]
    private static partial void LogSkippedNoVariables(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bootstrap admin skipped: a SuperAdmin already exists")]
    private static partial void LogSkippedExists(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bootstrap SuperAdmin created from the environment variables")]
    private static partial void LogCreated(ILogger logger);
}
