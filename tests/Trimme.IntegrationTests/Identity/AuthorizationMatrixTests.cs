using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Persistence;

namespace Trimme.IntegrationTests.Identity;

/// <summary>
/// R-AUTH-09: every <c>/api/v1</c> endpoint is classified (explicitly anonymous, authenticated self-service,
/// user-type restricted or permission restricted) and the API enforces the classification.
/// </summary>
public sealed partial class AuthorizationMatrixTests(PostgresFixture postgres)
{
    /// <summary>The only endpoints open to anonymous callers. Adding one here is a reviewed decision.</summary>
    private static readonly HashSet<string> AnonymousAllowList =
    [
        "GET /api/v1/meta",
        "GET /api/v1/auth/csrf",
        "POST /api/v1/auth/otp/request",
        "POST /api/v1/auth/otp/verify",
        "POST /api/v1/auth/staff/sign-in",
        "POST /api/v1/auth/password/forgot",
        "POST /api/v1/auth/password/reset",
        "POST /api/v1/auth/invitations/accept",
        "POST /api/v1/auth/refresh",
        "POST /api/v1/auth/sign-out",
        "GET /api/v1/dev/otp-inbox/latest",

        // Phase 06: stored images and the published shop page (read-only, published data only, D-064/D-066).
        "GET /api/v1/media/{mediaId:guid}",
        "GET /api/v1/public/shops/{slug}",
        "GET /api/v1/public/shops/{slug}/professionals",

        // Phase 07: published catalogue (active, visible, non-archived items of active shops only).
        "GET /api/v1/public/service-categories",
        "GET /api/v1/public/shops/{slug}/services",
        "GET /api/v1/public/shops/{slug}/packages",

        // Phase 09: computed availability of published items (rate-limited; no professional contact or booking details).
        "GET /api/v1/public/shops/{slug}/availability/dates",
        "GET /api/v1/public/shops/{slug}/availability/slots",
    ];

    /// <summary>Endpoints any signed-in user may call about themselves (no permission needed).</summary>
    private static readonly HashSet<string> SelfServiceAllowList =
    [
        "GET /api/v1/me",
        "GET /api/v1/auth/sessions",
        "DELETE /api/v1/auth/sessions/{sessionId:guid}",
        "POST /api/v1/auth/sessions/revoke-all",
    ];

    [Fact]
    public async Task Endpoint_WithoutPermission_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "authz_matrix", ct);
        var endpoints = ApiEndpoints(factory);
        endpoints.Count.ShouldBeGreaterThan(15);

        using var anonymous = ApiSession.Create(factory);
        using var customer = await IdentityTestData.SignInCustomerAsync(factory, IdentityTestData.NewPhone(), ct);
        using var support = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.Support, ct);
        var supportPermissions = SystemRoles.Find(SystemRoles.Support)!.DefaultPermissions.ToHashSet(StringComparer.Ordinal);
        var checkedPermissionEndpoints = 0;

        foreach (var endpoint in endpoints)
        {
            var anonymousAllowed = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var permission = endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>()?.Permission;
            var userType = endpoint.Metadata.GetMetadata<RequiredUserTypeMetadata>()?.UserType;

            if (anonymousAllowed)
            {
                AnonymousAllowList.ShouldContain(endpoint.Key, $"{endpoint.Key} allows anonymous access but is not on the reviewed allow-list.");
                (permission ?? userType).ShouldBeNull($"{endpoint.Key} cannot be both anonymous and restricted.");
                continue;
            }

            // Everything else rejects anonymous callers.
            using (var response = await Send(anonymous, endpoint, ct))
            {
                response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, endpoint.Key);
                (await response.ErrorCodeAsync(ct)).ShouldBe("auth.unauthenticated", endpoint.Key);
            }

            if (permission is not null)
            {
                Permissions.All.ShouldContain(p => p.Code == permission, $"{endpoint.Key} requires unknown permission {permission}.");
                using (var asCustomer = await Send(customer, endpoint, ct))
                {
                    asCustomer.StatusCode.ShouldBe(HttpStatusCode.Forbidden, endpoint.Key);
                    (await asCustomer.ErrorCodeAsync(ct)).ShouldBe("auth.forbidden", endpoint.Key);
                }

                if (!supportPermissions.Contains(permission))
                {
                    using var asSupport = await Send(support, endpoint, ct);
                    asSupport.StatusCode.ShouldBe(HttpStatusCode.Forbidden, endpoint.Key);
                }

                checkedPermissionEndpoints++;
            }
            else if (userType is not null)
            {
                using var otherType = await Send(userType == UserTypes.Customer ? support : customer, endpoint, ct);
                otherType.StatusCode.ShouldBe(HttpStatusCode.Forbidden, endpoint.Key);
            }
            else
            {
                SelfServiceAllowList.ShouldContain(endpoint.Key, $"{endpoint.Key} needs a permission, a user type, or a reviewed self-service entry.");
            }
        }

        checkedPermissionEndpoints.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Admin_WithPermission_CanReadTheCatalogue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "authz_catalogue", ct);
        using var ops = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.OperationsManager, ct);

        using var permissions = await ops.GetAsync("/api/v1/admin/permissions", ct);
        permissions.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await permissions.JsonAsync(ct)).GetArrayLength().ShouldBe(Permissions.All.Count);

        using var roles = await ops.GetAsync("/api/v1/admin/roles", ct);
        var list = (await roles.JsonAsync(ct)).EnumerateArray().ToArray();
        list.Select(r => r.GetProperty("name").GetString()).ShouldBe(SystemRoles.All.Select(r => r.Name), ignoreOrder: true);
        var superAdmin = list.Single(r => r.GetProperty("name").GetString() == SystemRoles.SuperAdmin);
        superAdmin.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .ShouldContain(Permissions.SuperAdmin.SubscriptionPlansManage);

        // Operations managers cannot invite admins (SuperAdmin only by default).
        using var invite = await ops.PostAsync(
            "/api/v1/admin/staff/invitations", new { email = IdentityTestData.NewEmail(), role = SystemRoles.Support, locale = "ar" }, ct);
        invite.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void PermissionCatalogue_HasNoTransferPermission()
    {
        var forbidden = new[] { "transfer", "move", "export", "reassign" };
        Permissions.All.Select(p => p.Code)
            .Where(code => forbidden.Any(f => code.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ShouldBeEmpty();
        Permissions.All.Select(p => p.Code).ShouldBeUnique();
        Permissions.All.ShouldAllBe(p => p.Code.StartsWith("Shop.", StringComparison.Ordinal) == (p.UserType == UserType.ShopUser));
    }

    [Fact]
    public void SeedRoles_GrantOnlyPermissionsOfTheirUserType()
    {
        var catalogue = Permissions.All.ToDictionary(p => p.Code);
        foreach (var role in SystemRoles.All)
        {
            role.DefaultPermissions.ShouldAllBe(code => catalogue.ContainsKey(code) && catalogue[code].UserType == role.UserType, role.Name);
        }

        var superAdminOnly = Permissions.All.Where(p => p.Scope == "SuperAdmin").Select(p => p.Code).ToArray();
        superAdminOnly.ShouldNotBeEmpty();
        SystemRoles.All.Where(r => r.Name != SystemRoles.SuperAdmin)
            .SelectMany(r => r.DefaultPermissions)
            .Intersect(superAdminOnly)
            .ShouldBeEmpty("Plan pricing and subscription overrides are SuperAdmin-only (spec §7).");
        SystemRoles.Find(SystemRoles.Customer)!.DefaultPermissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task CatalogueSync_RestoresManagedRoles_AndKeepsAdminEditsToEditableRoles()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "authz_sync", ct);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
            var roles = await db.Set<ApplicationRole>().ToDictionaryAsync(r => r.Name!, ct);
            var grants = db.Set<RolePermission>();
            grants.Remove(await grants.SingleAsync(g => g.RoleId == roles[SystemRoles.SuperAdmin].Id && g.PermissionCode == Permissions.Admin.ShopsView, ct));
            grants.Remove(await grants.SingleAsync(g => g.RoleId == roles[SystemRoles.OperationsManager].Id && g.PermissionCode == Permissions.Admin.ShopsView, ct));
            grants.Add(new RolePermission(roles[SystemRoles.OperationsManager].Id, Permissions.Shop.ServicesManage));
            await db.SaveChangesAsync(ct);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await ReferenceData.SynchronizeAsync(scope.ServiceProvider, ct);
            await ReferenceData.SynchronizeAsync(scope.ServiceProvider, ct);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
            var roles = await db.Set<ApplicationRole>().ToDictionaryAsync(r => r.Name!, ct);
            var grants = await db.Set<RolePermission>().ToListAsync(ct);
            grants.ShouldContain(g => g.RoleId == roles[SystemRoles.SuperAdmin].Id && g.PermissionCode == Permissions.Admin.ShopsView);
            grants.ShouldNotContain(g => g.RoleId == roles[SystemRoles.OperationsManager].Id && g.PermissionCode == Permissions.Admin.ShopsView);
            grants.ShouldNotContain(g => g.RoleId == roles[SystemRoles.OperationsManager].Id && g.PermissionCode == Permissions.Shop.ServicesManage);
            (await db.Set<Permission>().CountAsync(ct)).ShouldBe(Permissions.All.Count);
        }
    }

    private static List<ApiEndpoint> ApiEndpoints(TrimmeApiFactory factory) =>
        [.. factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => ("/" + e.RoutePattern.RawText?.TrimStart('/')).StartsWith("/api/v1/", StringComparison.Ordinal))
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => new ApiEndpoint(method, "/" + e.RoutePattern.RawText!.TrimStart('/'), e.Metadata)))];

    private static async Task<HttpResponseMessage> Send(ApiSession session, ApiEndpoint endpoint, CancellationToken ct)
    {
        var path = RouteParameter().Replace(endpoint.Route, Guid.NewGuid().ToString());

        // Upload endpoints only match multipart requests (anything else is 415 at routing), so send the right shape.
        if (endpoint.Metadata.OfType<IAcceptsMetadata>().Any(m => m.ContentTypes.Contains("multipart/form-data")))
        {
            using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), path) { Content = new MultipartFormDataContent() };
            request.Headers.Add(Csrf.HeaderName, await session.CsrfTokenAsync(ct));
            return await session.Client.SendAsync(request, ct);
        }

        var body = endpoint.Method is "POST" or "PUT" or "PATCH" ? new { } : null;
        return await session.SendAsync(new HttpMethod(endpoint.Method), path, body, ct);
    }

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();

    private sealed record ApiEndpoint(string Method, string Route, EndpointMetadataCollection Metadata)
    {
        public string Key => $"{Method} {Route}";
    }
}
