using Microsoft.AspNetCore.Http;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;

namespace Trimme.BuildingBlocks.Web.Security;

/// <summary>Per-request flags set while the session is validated (see the Identity module's cookie validation).</summary>
public static class TenantRequestItems
{
    /// <summary>Set to <see langword="true"/> when the shop user's shop exists and is not suspended.</summary>
    public const string ShopOperable = "trimme.tenant.shop-operable";
}

/// <summary>
/// The tenant is the shop in the signed-in shop user's claims (written from the database at sign-in, never from the
/// client), and only while that shop is operable this request (not suspended). Anything else has no tenant.
/// Safe without an HTTP context (host commands, jobs): no tenant.
/// </summary>
internal sealed class HttpCurrentTenant(IHttpContextAccessor accessor) : ICurrentTenant
{
    public ShopId? ShopId
    {
        get
        {
            var http = accessor.HttpContext;
            if (http?.User is not { Identity.IsAuthenticated: true } user
                || user.FindFirst(TrimmeClaims.UserType)?.Value != UserTypes.ShopUser
                || !Guid.TryParse(user.FindFirst(TrimmeClaims.ShopId)?.Value, out var shopId)
                || http.Items[TenantRequestItems.ShopOperable] is not true)
            {
                return null;
            }

            return new ShopId(shopId);
        }
    }
}

/// <summary>
/// The signed-in customer from the session claims (validated against the database at authentication, like the tenant).
/// Anything else (anonymous, shop users, admins, host commands) has no customer.
/// </summary>
internal sealed class HttpCurrentCustomer(IHttpContextAccessor accessor) : ICurrentCustomer
{
    public Guid? CustomerId
    {
        get
        {
            var user = accessor.HttpContext?.User;
            return user is { Identity.IsAuthenticated: true }
                   && user.FindFirst(TrimmeClaims.UserType)?.Value == UserTypes.Customer
                   && Guid.TryParse(user.FindFirst(TrimmeClaims.Subject)?.Value, out var id)
                ? id
                : null;
        }
    }
}

/// <summary>Admin bypass (R-TEN-05): only a signed-in platform admin may open it.</summary>
internal sealed class AdminDataScope(TrimmeDbContext db, ICurrentUser user) : IAdminDataScope
{
    public IDisposable Begin()
    {
        if (user.UserType != UserTypes.PlatformAdmin)
        {
            throw new TenantViolationException("The admin data scope requires a platform admin.");
        }

        return db.EnterUnrestrictedScope();
    }
}

/// <summary>Read-only public view bound to one shop (D-066); available to any caller, including anonymous ones.</summary>
internal sealed class PublicDataScope(TrimmeDbContext db) : IPublicDataScope
{
    public IDisposable Begin(ShopId? shopId) => db.EnterPublicScope(shopId);
}

/// <summary>System bypass for host commands and background jobs; refused inside an HTTP request.</summary>
internal sealed class SystemDataScope(TrimmeDbContext db, IHttpContextAccessor accessor) : ISystemDataScope
{
    public IDisposable Begin()
    {
        if (accessor.HttpContext is not null)
        {
            throw new TenantViolationException("The system data scope is not available inside an HTTP request.");
        }

        return db.EnterUnrestrictedScope();
    }
}
