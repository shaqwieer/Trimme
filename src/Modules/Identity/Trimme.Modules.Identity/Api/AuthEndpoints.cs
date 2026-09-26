using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Identity.Application;
using Trimme.Modules.Identity.Application.Otp;
using Trimme.Modules.Identity.Application.Sessions;
using Trimme.Modules.Identity.Application.Staff;

namespace Trimme.Modules.Identity.Api;

public sealed record RequestOtpRequest(string Phone, bool TermsAccepted, string? Locale);

public sealed record VerifyOtpRequest(Guid ChallengeId, string Code);

public sealed record CompleteProfileRequest(string DisplayName, string PreferredLocale, bool TermsAccepted);

public sealed record StaffSignInRequest(string Email, string Password);

public sealed record ForgotPasswordRequest(string Email, string? Locale);

public sealed record ResetPasswordRequest(Guid UserId, string Token, string NewPassword);

public sealed record AcceptInvitationRequest(string Token, string DisplayName, string Password);

public sealed record CsrfTokenResponse(string Token);

public sealed record RevokedSessionsResponse(int Revoked);

/// <summary>Authentication and session endpoints (spec §9, D-005, D-027). All unsafe calls require the CSRF header.</summary>
internal static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapGet("/csrf", IssueCsrf).AllowAnonymous()
            .WithName("GetCsrfToken").WithSummary("Issues the CSRF cookie and returns its value for the X-CSRF-Token header.");

        auth.MapPost("/otp/request", RequestOtp).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Otp)
            .WithName("RequestOtp").WithSummary("Sends a 6-digit sign-in code to a Saudi mobile number (customers).")
            .Produces<OtpChallengeResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        auth.MapPost("/otp/verify", VerifyOtp).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("VerifyOtp").WithSummary("Verifies the code, signs the customer in (creating the account if new) and sets the session cookies.")
            .Produces<SignInResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden);
        auth.MapPost("/profile/complete", CompleteProfile).RequireUserType(UserTypes.Customer)
            .WithName("CompleteCustomerProfile").WithSummary("Saves a new customer's name, language and terms acceptance.")
            .Produces<MeResponse>().ProducesProblem(StatusCodes.Status400BadRequest);

        auth.MapPost("/staff/sign-in", StaffSignIn).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("StaffSignIn").WithSummary("Shop and admin staff sign in with email and password.")
            .Produces<SignInResponse>().ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        auth.MapPost("/password/forgot", ForgotPassword).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("ForgotPassword").WithSummary("Emails a password-reset link to a staff account. Always returns 202.")
            .Produces(StatusCodes.Status202Accepted).ProducesProblem(StatusCodes.Status400BadRequest);
        auth.MapPost("/password/reset", ResetPassword).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("ResetPassword").WithSummary("Sets a new staff password from an emailed link and signs out every device.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest);
        auth.MapPost("/invitations/accept", AcceptInvitation).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("AcceptInvitation").WithSummary("Creates a staff account from an emailed invitation and signs it in.")
            .Produces<SignInResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);

        auth.MapPost("/refresh", Refresh).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("RefreshSession").WithSummary("Rotates the refresh cookie and re-issues the access cookie.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);
        auth.MapPost("/sign-out", SignOut).AllowAnonymous()
            .WithName("SignOut").WithSummary("Ends the current session and clears the session cookies.");

        auth.MapGet("/sessions", ListSessions)
            .WithName("ListSessions").WithSummary("The signed-in user's active sessions (devices).");
        auth.MapDelete("/sessions/{sessionId:guid}", RevokeSession)
            .WithName("RevokeSession").WithSummary("Signs out one of the user's own sessions.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
        auth.MapPost("/sessions/revoke-all", RevokeOtherSessions)
            .WithName("RevokeOtherSessions").WithSummary("Signs out every other device; the current session stays signed in.")
            .Produces<RevokedSessionsResponse>();

        api.MapGet("/me", GetMe).WithTags("Auth")
            .WithName("GetMe").WithSummary("The signed-in user with roles and permissions.")
            .Produces<MeResponse>().ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static Ok<CsrfTokenResponse> IssueCsrf(HttpContext http, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return TypedResults.Ok(new CsrfTokenResponse(Csrf.Issue(http.Response)));
    }

    private static async Task<IResult> RequestOtp(RequestOtpRequest request, HttpContext http, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new RequestOtpCommand(request.Phone ?? string.Empty, request.TermsAccepted, request.Locale ?? "ar", SessionCookies.Client(http)),
            cancellationToken);
        return result.ToHttpResult(challenge => TypedResults.Accepted((string?)null, challenge));
    }

    private static async Task<IResult> VerifyOtp(VerifyOtpRequest request, HttpContext http, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new VerifyOtpCommand(request.ChallengeId, request.Code ?? string.Empty, SessionCookies.Client(http)), cancellationToken);
        return await SignedIn(http, result);
    }

    private static async Task<IResult> CompleteProfile(CompleteProfileRequest request, ICurrentUser user, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new CompleteProfileCommand(user.UserId!.Value, request.DisplayName ?? string.Empty, request.PreferredLocale ?? string.Empty, request.TermsAccepted),
            cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> StaffSignIn(StaffSignInRequest request, HttpContext http, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new StaffSignInCommand(request.Email ?? string.Empty, request.Password ?? string.Empty, SessionCookies.Client(http)),
            cancellationToken);
        return await SignedIn(http, result);
    }

    private static async Task<IResult> ForgotPassword(ForgotPasswordRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new ForgotPasswordCommand(request.Email ?? string.Empty, request.Locale ?? "ar"), cancellationToken);
        return result.ToHttpResult(() => TypedResults.Accepted((string?)null));
    }

    private static async Task<IResult> ResetPassword(ResetPasswordRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new ResetPasswordCommand(request.UserId, request.Token ?? string.Empty, request.NewPassword ?? string.Empty),
            cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> AcceptInvitation(AcceptInvitationRequest request, HttpContext http, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new AcceptInvitationCommand(request.Token ?? string.Empty, request.DisplayName ?? string.Empty, request.Password ?? string.Empty, SessionCookies.Client(http)),
            cancellationToken);
        return await SignedIn(http, result);
    }

    private static async Task<IResult> Refresh(HttpContext http, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var token = SessionCookies.ReadRefreshToken(http.Request);
        if (token is null)
        {
            return Domain.IdentityErrors.RefreshInvalid().ToProblem();
        }

        var result = await dispatcher.Send(new RefreshSessionCommand(token, SessionCookies.Client(http)), cancellationToken);
        if (result.IsFailure)
        {
            if (result.Error.Kind == ErrorKind.Unauthorized)
            {
                await SessionCookies.ClearAsync(http);
            }

            return result.Error.ToProblem();
        }

        await SessionCookies.SignInAsync(http, result.Value, rotateCsrf: false);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> SignOut(HttpContext http, ICurrentUser user, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.Send(new SignOutCommand(user.SessionId, user.UserId, SessionCookies.ReadRefreshToken(http.Request)), cancellationToken);
        await SessionCookies.ClearAsync(http);
        Csrf.Clear(http.Response);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<IReadOnlyList<SessionResponse>>> ListSessions(ICurrentUser user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dispatcher.Send(new ListSessionsQuery(user.UserId!.Value, user.SessionId), cancellationToken));

    private static async Task<IResult> RevokeSession(Guid sessionId, HttpContext http, ICurrentUser user, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new RevokeSessionCommand(user.UserId!.Value, sessionId), cancellationToken);
        if (result.IsSuccess && sessionId == user.SessionId)
        {
            await SessionCookies.ClearAsync(http);
        }

        return result.ToHttpResult();
    }

    private static async Task<IResult> RevokeOtherSessions(ICurrentUser user, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new RevokeOtherSessionsCommand(user.UserId!.Value, user.SessionId), cancellationToken);
        return result.ToHttpResult(count => TypedResults.Ok(new RevokedSessionsResponse(count)));
    }

    private static async Task<IResult> GetMe(ICurrentUser user, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var me = await dispatcher.Send(new GetMeQuery(user.UserId!.Value), cancellationToken);
        return me is null ? TypedResults.Unauthorized() : TypedResults.Ok(me);
    }

    private static async Task<IResult> SignedIn(HttpContext http, Result<SignInOutcome> result)
    {
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        await SessionCookies.SignInAsync(http, result.Value.Session);
        return TypedResults.Ok(result.Value.Response);
    }
}
