using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;
using Trimme.Modules.Shops.Domain;

namespace Trimme.IntegrationTests.Tenancy;

/// <summary>Two shops with an owner and a staff member each: the isolation harness (R-TEN-06).</summary>
internal sealed record TwoShops(ShopMembers A, ShopMembers B);

internal sealed record ShopMembers(Guid ShopId, string OwnerEmail, string StaffEmail);

internal static class ShopTestData
{
    /// <summary>Test-only probe mounted in the pipeline: authenticates like the API does, then reports the tenant.</summary>
    public const string TenantProbePath = "/__test/tenant";

    public static async Task<TrimmeApiFactory> CreateFactoryAsync(
        PostgresFixture postgres, string prefix, CancellationToken cancellationToken, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var factory = new ProbeFactory(await postgres.CreateDatabaseAsync(prefix, cancellationToken), settings);
        await factory.MigrateAsync(cancellationToken);
        return factory;
    }

    public static async Task<Guid> CreateShopAsync(ApiSession admin, string slug, CancellationToken ct, bool activate = true)
    {
        using var created = await admin.PostAsync("/api/v1/admin/shops", new { slug, nameAr = $"محل {slug}", nameEn = $"Shop {slug}" }, ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));
        var id = (await created.JsonAsync(ct)).GetProperty("id").GetGuid();
        if (activate)
        {
            using var activated = await admin.PostAsync($"/api/v1/admin/shops/{id}/activate", new { }, ct);
            activated.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        return id;
    }

    /// <summary>Creates a shop user directly (the invitation path is covered by its own test).</summary>
    public static async Task CreateShopUserAsync(TrimmeApiFactory factory, Guid shopId, string email, string role, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var id = Guid.CreateVersion7();
        var user = new ApplicationUser
        {
            Id = id,
            UserName = $"staff-{id:N}",
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Shop member",
            UserType = UserType.ShopUser,
            ShopId = new ShopId(shopId),
            CreatedAt = DateTimeOffset.UtcNow,
            LockoutEnabled = true,
        };
        (await users.CreateAsync(user, IdentityTestData.StaffPassword)).Succeeded.ShouldBeTrue();
        (await users.AddToRoleAsync(user, role)).Succeeded.ShouldBeTrue();
        ct.ThrowIfCancellationRequested();
    }

    public static async Task<TwoShops> CreateTwoShopsAsync(TrimmeApiFactory factory, ApiSession admin, CancellationToken ct)
    {
        async Task<ShopMembers> One(string name)
        {
            var slug = $"{name}-{Guid.NewGuid():N}"[..24];
            var shopId = await CreateShopAsync(admin, slug, ct);
            var owner = IdentityTestData.NewEmail($"{name}-owner");
            var staff = IdentityTestData.NewEmail($"{name}-staff");
            await CreateShopUserAsync(factory, shopId, owner, SystemRoles.ShopOwner, ct);
            await CreateShopUserAsync(factory, shopId, staff, SystemRoles.ShopStaff, ct);
            return new ShopMembers(shopId, owner, staff);
        }

        return new TwoShops(await One("alpha"), await One("beta"));
    }

    private sealed class ProbeFactory(string connectionString, IReadOnlyDictionary<string, string?>? settings)
        : TrimmeApiFactory(connectionString, settings)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, TenantProbeStartupFilter>());
        }
    }

    /// <summary>Reports the resolved tenant and how many shop rows the real context lets the caller see.</summary>
    private sealed class TenantProbeStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (!context.Request.Path.Equals(TenantProbePath, StringComparison.Ordinal))
                {
                    await nextMiddleware(context);
                    return;
                }

                var result = await context.AuthenticateAsync(TrimmeClaims.AuthenticationScheme);
                if (result.Succeeded)
                {
                    context.User = result.Principal!;
                }

                var tenant = context.RequestServices.GetRequiredService<ICurrentTenant>().ShopId;
                var db = context.RequestServices.GetRequiredService<TrimmeDbContext>();
                var visibleShops = await db.Set<Shop>().CountAsync(context.RequestAborted);
                await context.Response.WriteAsJsonAsync(new { tenant = tenant?.Value, visibleShops }, context.RequestAborted);
            });
            next(app);
        };
    }
}
