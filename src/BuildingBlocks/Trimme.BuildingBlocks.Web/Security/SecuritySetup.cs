using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Web.Privacy;

namespace Trimme.BuildingBlocks.Web.Security;

public static class SecuritySetup
{
    public const string DataProtectionApplicationName = "trimme";

    /// <summary>
    /// Registers the current user, permission-based authorization, the shared Data Protection key ring and the
    /// personal-data protector. The authentication scheme itself is added by the Identity module.
    /// </summary>
    public static IServiceCollection AddTrimmeSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<ICurrentTenant, HttpCurrentTenant>();
        services.AddScoped<ICurrentCustomer, HttpCurrentCustomer>();
        services.AddScoped<IAdminDataScope, AdminDataScope>();
        services.AddScoped<ISystemDataScope, SystemDataScope>();
        services.AddScoped<IPublicDataScope, PublicDataScope>();

        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, TrimmeAuthorizationPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddDataProtection().SetApplicationName(DataProtectionApplicationName).ProtectTrimmeKeys(configuration, environment);
        services.AddOptions<KeyManagementOptions>()
            .Configure<IServiceScopeFactory>((options, scopes) => options.XmlRepository = new DatabaseXmlRepository(scopes));

        var lookupKeyRequired = !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
        services.AddOptions<PersonalDataOptions>()
            .Bind(configuration.GetSection(PersonalDataOptions.SectionName))
            .Validate(
                options => !lookupKeyRequired || !string.IsNullOrWhiteSpace(options.LookupKey),
                $"{PersonalDataOptions.SectionName}:LookupKey must be configured outside Development and Testing.")
            .ValidateOnStart();
        services.AddSingleton<IPersonalDataProtector, PersonalDataProtector>();

        return services;
    }
}
