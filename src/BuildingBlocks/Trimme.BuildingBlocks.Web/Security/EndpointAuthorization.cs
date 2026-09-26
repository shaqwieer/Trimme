using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Security;

namespace Trimme.BuildingBlocks.Web.Security;

/// <summary>
/// Endpoint authorization vocabulary. Every <c>/api/v1</c> endpoint requires an authenticated user by default
/// (default deny, applied on the route group). Endpoints narrow that with a permission or a user type, or opt out
/// explicitly with <c>AllowAnonymous()</c>. The endpoint matrix test reads the metadata added here (R-AUTH-09).
/// </summary>
public static class EndpointAuthorization
{
    public const string PermissionPolicyPrefix = "permission:";
    public const string UserTypePolicyPrefix = "user-type:";

    /// <summary>Requires a permission from the data-driven catalogue, checked against the database per request.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return builder
            .RequireAuthorization(PermissionPolicyPrefix + permission)
            .WithMetadata(new RequiredPermissionMetadata(permission));
    }

    /// <summary>Restricts a self-service endpoint to one user type (for example customer-only profile completion).</summary>
    public static TBuilder RequireUserType<TBuilder>(this TBuilder builder, string userType)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userType);
        return builder
            .RequireAuthorization(UserTypePolicyPrefix + userType)
            .WithMetadata(new RequiredUserTypeMetadata(userType));
    }
}

public sealed record RequiredPermissionMetadata(string Permission);

public sealed record RequiredUserTypeMetadata(string UserType);

internal sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Builds <c>permission:*</c> and <c>user-type:*</c> policies on demand; other names use the defaults.</summary>
internal sealed class TrimmeAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);

        if (policyName.StartsWith(EndpointAuthorization.PermissionPolicyPrefix, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder(TrimmeClaims.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[EndpointAuthorization.PermissionPolicyPrefix.Length..]))
                .Build();
        }

        if (policyName.StartsWith(EndpointAuthorization.UserTypePolicyPrefix, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder(TrimmeClaims.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(TrimmeClaims.UserType, policyName[EndpointAuthorization.UserTypePolicyPrefix.Length..])
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}

/// <summary>Succeeds when one of the user's roles carries the required permission.</summary>
internal sealed class PermissionAuthorizationHandler(ICurrentUser currentUser, IPermissionResolver resolver)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (currentUser.UserId is not { } userId)
        {
            return;
        }

        var permissions = await resolver.GetPermissionsAsync(userId, CancellationToken.None);
        if (permissions.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
