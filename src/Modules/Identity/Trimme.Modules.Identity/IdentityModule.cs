using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Web.Modules;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Identity.Api;
using Trimme.Modules.Identity.Application;
using Trimme.Modules.Identity.Application.Sessions;
using Trimme.Modules.Identity.Application.Staff;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure;
using Trimme.Modules.Identity.Infrastructure.Email;
using Trimme.Modules.Identity.Infrastructure.Otp;
using Trimme.Modules.Identity.Infrastructure.Persistence;
using Trimme.Modules.Identity.Infrastructure.Seeding;

namespace Trimme.Modules.Identity;

/// <summary>
/// Identity module (schema <c>identity</c>): accounts on ASP.NET Core Identity, customer OTP sign-in, staff password
/// sign-in, cookie sessions with refresh rotation, the permission catalogue and roles (Phase 04).
/// </summary>
public sealed class IdentityModule : ModuleBase
{
    public override string Name => "Identity";

    public override string Schema => IdentityModel.Schema;

    public override void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        base.AddServices(services, configuration);

        services.AddOptions<SessionOptions>().Bind(configuration.GetSection(SessionOptions.SectionName));
        services.AddOptions<InvitationOptions>().Bind(configuration.GetSection(InvitationOptions.SectionName));
        services.AddOptions<WebLinkOptions>().Bind(configuration.GetSection(WebLinkOptions.SectionName));
        services.AddSingleton(configuration.GetSection("Identity:Otp:Limits").Get<OtpPolicy>() ?? new OtpPolicy());

        AddIdentityCore(services);
        AddSessionAuthentication(services);
        AddDelivery(services, configuration);

        services.AddScoped<AccountStore>();
        services.AddScoped<IAccountStore>(sp => sp.GetRequiredService<AccountStore>());
        services.AddScoped<IRoleDirectory>(sp => sp.GetRequiredService<AccountStore>());
        services.AddScoped<IPermissionResolver, PermissionResolver>();
        services.AddScoped<SessionValidator>();
        services.AddScoped<SessionManager>();
        services.AddScoped<MeReader>();
        services.AddScoped<InvitationIssuer>();

        services.AddSingleton<IReferenceDataSynchronizer, PermissionCatalogueSynchronizer>();
        services.AddSingleton<IDevSeeder, BootstrapAdminSeeder>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder api)
    {
        AuthEndpoints.Map(api);
        AdminIdentityEndpoints.Map(api);
        DevOtpInboxEndpoints.Map(api);
    }

    public override void ConfigureModel(ModelBuilder modelBuilder)
    {
        base.ConfigureModel(modelBuilder);
        IdentityModel.Configure(modelBuilder);
    }

    private static void AddIdentityCore(IServiceCollection services)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // NIST SP 800-63B: length over composition rules.
                options.Password.RequiredLength = 10;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                // Customers have no email; staff email uniqueness is a filtered unique index.
                options.User.RequireUniqueEmail = false;
                options.Stores.MaxLengthForKeys = 128;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<TrimmeDbContext>()
            .AddTokenProvider<DataProtectorTokenProvider<ApplicationUser>>(TokenOptions.DefaultProvider);

        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));
    }

    private static void AddSessionAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(TrimmeClaims.AuthenticationScheme)
            .AddCookie(TrimmeClaims.AuthenticationScheme, options =>
            {
                options.Cookie.Name = SessionCookies.AccessCookie;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.Cookie.IsEssential = true;
                options.SlidingExpiration = false;
                options.Events = new CookieAuthenticationEvents
                {
                    // An API never redirects: unauthenticated → 401, unauthorized → 403, as problem details.
                    OnRedirectToLogin = context => context.HttpContext.WriteProblemAsync(
                        StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthenticated, "Authentication is required."),
                    OnRedirectToAccessDenied = context => context.HttpContext.WriteProblemAsync(
                        StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "You do not have permission to perform this action."),
                    OnValidatePrincipal = ValidateSessionAsync,
                };
            });

        services.AddOptions<CookieAuthenticationOptions>(TrimmeClaims.AuthenticationScheme)
            .Configure<IOptions<SessionOptions>>((cookie, sessions) => cookie.ExpireTimeSpan = sessions.Value.AccessLifetime);
    }

    /// <summary>Rejects the access cookie as soon as its session is revoked or the account is disabled (R-AUTH-05).</summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var valid = Guid.TryParse(principal?.FindFirst(TrimmeClaims.SessionId)?.Value, out var sessionId)
                    && Guid.TryParse(principal?.FindFirst(TrimmeClaims.Subject)?.Value, out var userId)
                    && await context.HttpContext.RequestServices.GetRequiredService<SessionValidator>()
                        .IsActiveAsync(sessionId, userId, context.HttpContext.RequestAborted);

        if (!valid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(TrimmeClaims.AuthenticationScheme);
        }
    }

    private static void AddDelivery(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OtpDeliveryOptions>()
            .Bind(configuration.GetSection(OtpDeliveryOptions.SectionName))
            .Validate<IHostEnvironment>(
                (options, environment) => options.Sender != OtpSenderKind.DevInbox || OtpDeliveryOptions.IsLocal(environment),
                "Identity:Otp:Sender=DevInbox is only allowed in Development and Testing.")
            .ValidateOnStart();
        services.AddSingleton<DevOtpInbox>();
        services.AddScoped<IOtpSender>(sp =>
            sp.GetRequiredService<IOptions<OtpDeliveryOptions>>().Value.ResolveSender(sp.GetRequiredService<IHostEnvironment>()) switch
            {
                OtpSenderKind.DevInbox => ActivatorUtilities.CreateInstance<DevInboxOtpSender>(sp),
                _ => ActivatorUtilities.CreateInstance<UnavailableOtpSender>(sp),
            });

        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName))
            .Validate<IHostEnvironment>(
                (options, environment) => !string.IsNullOrWhiteSpace(options.Host) || OtpDeliveryOptions.IsLocal(environment),
                "Email:Smtp:Host must be configured outside Development and Testing.")
            .ValidateOnStart();
        services.AddScoped<IEmailSender>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<SmtpOptions>>().Value.Host)
                ? ActivatorUtilities.CreateInstance<DroppingEmailSender>(sp)
                : ActivatorUtilities.CreateInstance<SmtpEmailSender>(sp));
        services.AddScoped<IIdentityMailer, IdentityMailer>();
    }
}
