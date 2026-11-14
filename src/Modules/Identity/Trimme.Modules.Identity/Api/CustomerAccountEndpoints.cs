using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Identity.Application.Customer;

namespace Trimme.Modules.Identity.Api;

/// <summary><c>Phone</c> empty keeps the number; another number moves the account to it (codes off only, D-144).</summary>
public sealed record UpdateCustomerProfileRequest(string DisplayName, string PreferredLocale, string? Email, string? Phone);

/// <summary>The customer's own account (D-144): personal info with email and number, and deleting the account.</summary>
internal static class CustomerAccountEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var me = api.MapGroup("/me").WithTags("Customer account").RequireUserType(UserTypes.Customer);

        me.MapGet("/profile", GetProfile)
            .WithName("GetCustomerProfile").WithSummary("The customer's name, language, email and masked number, and whether the number can be changed here.")
            .Produces<CustomerProfileResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        me.MapPut("/profile", UpdateProfile).RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("UpdateCustomerProfile")
            .WithSummary("Saves the customer's name, language and email; a new number becomes the one they sign in with (while sign-in codes are off).")
            .Produces<CustomerProfileResponse>().ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict);
        api.MapDelete("/me", DeleteAccount).WithTags("Customer account").RequireUserType(UserTypes.Customer).RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("DeleteCustomerAccount")
            .WithSummary("Deletes the customer's account: refused while bookings are ahead; erases the name, number and email, ends every session and clears the cookies.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> GetProfile(ICurrentUser user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        await dispatcher.Send(new GetCustomerProfileQuery(user.UserId!.Value), cancellationToken) is { } profile
            ? TypedResults.Ok(profile)
            : Domain.IdentityErrors.UserNotFound().ToProblem();

    private static async Task<IResult> UpdateProfile(
        UpdateCustomerProfileRequest request, ICurrentUser user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.Send(
            new UpdateCustomerProfileCommand(
                user.UserId!.Value, request.DisplayName ?? string.Empty, request.PreferredLocale ?? string.Empty, request.Email, request.Phone),
            cancellationToken)).ToHttpResult();

    private static async Task<IResult> DeleteAccount(HttpContext http, ICurrentUser user, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new DeleteCustomerAccountCommand(user.UserId!.Value), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        await SessionCookies.ClearAsync(http);
        Csrf.Clear(http.Response);
        return TypedResults.NoContent();
    }
}
