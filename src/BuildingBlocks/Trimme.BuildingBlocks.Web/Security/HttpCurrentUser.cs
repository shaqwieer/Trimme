using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Trimme.BuildingBlocks.Application.Security;

namespace Trimme.BuildingBlocks.Web.Security;

/// <summary><see cref="ICurrentUser"/> read from the authenticated principal of the current request.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;

    public bool IsAuthenticated => UserId is not null;

    public Guid? UserId => ReadGuid(TrimmeClaims.Subject);

    public string? UserType => Principal?.FindFirst(TrimmeClaims.UserType)?.Value;

    public Guid? SessionId => ReadGuid(TrimmeClaims.SessionId);

    public Guid? ShopId => ReadGuid(TrimmeClaims.ShopId);

    private Guid? ReadGuid(string claimType) =>
        Guid.TryParse(Principal?.FindFirst(claimType)?.Value, out var value) ? value : null;
}
