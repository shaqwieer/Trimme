using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Application.Sessions;

/// <summary>Rotates the refresh token and re-issues the access cookie.</summary>
internal sealed record RefreshSessionCommand(string RefreshToken, ClientContext Client) : ICommand<Result<IssuedSession>>;

/// <summary>Ends the current session, identified by the access cookie or, if it has expired, the refresh cookie.</summary>
internal sealed record SignOutCommand(Guid? SessionId, Guid? UserId, string? RefreshToken) : ICommand<Result>;

internal sealed record ListSessionsQuery(Guid UserId, Guid? CurrentSessionId) : IQuery<IReadOnlyList<SessionResponse>>;

internal sealed record RevokeSessionCommand(Guid UserId, Guid SessionId) : ICommand<Result>;

/// <summary>Signs out every other device (spec §9 "revoke all sessions"); the current session stays.</summary>
internal sealed record RevokeOtherSessionsCommand(Guid UserId, Guid? CurrentSessionId) : ICommand<Result<int>>;

internal sealed class RefreshSessionHandler(SessionManager sessions) : ICommandHandler<RefreshSessionCommand, Result<IssuedSession>>
{
    public Task<Result<IssuedSession>> Handle(RefreshSessionCommand command, CancellationToken cancellationToken) =>
        sessions.RotateAsync(command.RefreshToken, command.Client, cancellationToken);
}

internal sealed class SignOutHandler(SessionManager sessions) : ICommandHandler<SignOutCommand, Result>
{
    public async Task<Result> Handle(SignOutCommand command, CancellationToken cancellationToken)
    {
        UserSessionId? sessionId = command.SessionId is { } id ? new UserSessionId(id) : null;
        if (sessionId is null && !string.IsNullOrEmpty(command.RefreshToken))
        {
            sessionId = await sessions.FindSessionByRefreshTokenAsync(command.RefreshToken, cancellationToken);
        }

        if (sessionId is { } toRevoke)
        {
            await sessions.RevokeAsync(toRevoke, command.UserId, SessionRevocationReason.SignedOut, cancellationToken);
        }

        return Result.Success();
    }
}

internal sealed class ListSessionsHandler(SessionManager sessions) : IQueryHandler<ListSessionsQuery, IReadOnlyList<SessionResponse>>
{
    public Task<IReadOnlyList<SessionResponse>> Handle(ListSessionsQuery query, CancellationToken cancellationToken) =>
        sessions.ListActiveAsync(query.UserId, query.CurrentSessionId is { } id ? new UserSessionId(id) : null, cancellationToken);
}

internal sealed class RevokeSessionHandler(SessionManager sessions) : ICommandHandler<RevokeSessionCommand, Result>
{
    public async Task<Result> Handle(RevokeSessionCommand command, CancellationToken cancellationToken) =>
        await sessions.RevokeAsync(new UserSessionId(command.SessionId), command.UserId, SessionRevocationReason.RevokedByUser, cancellationToken)
            ? Result.Success()
            : IdentityErrors.SessionNotFound();
}

internal sealed class RevokeOtherSessionsHandler(SessionManager sessions) : ICommandHandler<RevokeOtherSessionsCommand, Result<int>>
{
    public async Task<Result<int>> Handle(RevokeOtherSessionsCommand command, CancellationToken cancellationToken) =>
        await sessions.RevokeAllAsync(
            command.UserId,
            command.CurrentSessionId is { } id ? new UserSessionId(id) : null,
            SessionRevocationReason.RevokedAllByUser,
            cancellationToken);
}
