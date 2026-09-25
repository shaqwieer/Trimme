namespace Trimme.BuildingBlocks.Application.Security;

/// <summary>
/// The authenticated caller, resolved from the request claims and never from client-supplied identifiers.
/// Implemented by the Identity module in Phase 04; anonymous until then.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }
}
