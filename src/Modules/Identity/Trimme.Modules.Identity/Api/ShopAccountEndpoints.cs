using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Identity.Application.Staff;

namespace Trimme.Modules.Identity.Api;

/// <summary>
/// A shop user's own account (D-151, «حسابي»): reading it, and a barber deleting their login. Renaming is the
/// Professionals module's (<c>PUT /shop/account/name</c>): a barber's name is their professional's.
/// </summary>
internal static class ShopAccountEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var account = api.MapGroup("/shop/account").WithTags("Shop: account").RequireUserType(UserTypes.ShopUser);

        account.MapGet("/", async (ICurrentUser user, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetShopAccountQuery(user.UserId!.Value, user.ProfessionalId), ct) is { } found
                    ? TypedResults.Ok(found)
                    : Domain.IdentityErrors.UserNotFound().ToProblem())
            .WithName("GetShopAccount")
            .WithSummary("The signed-in shop user's account: a barber's names (with the version to rename with) and masked sign-in number, or the owner's name and email; whether they may rename or delete it.")
            .Produces<ShopAccountResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        account.MapDelete("/", DeleteAsync).RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("DeleteShopAccount")
            .WithSummary("A barber deletes their login: its personal data is erased, every session ends and the cookies are cleared. The barber stays in the shop with their history; the owner is told and can make a new login. The owner's account: 403.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> DeleteAsync(HttpContext http, ICurrentUser user, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new DeleteBarberAccountCommand(user.UserId!.Value, user.ProfessionalId), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        await SessionCookies.ClearAsync(http);
        Csrf.Clear(http.Response);
        return TypedResults.NoContent();
    }
}
