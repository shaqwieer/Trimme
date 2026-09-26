namespace Trimme.BuildingBlocks.Application.Security;

/// <summary>
/// Resolves the permission codes granted to a user through their roles (spec §7: authorization is enforced by the API).
/// The catalogue is data-driven; codes are compared ordinally.
/// </summary>
public interface IPermissionResolver
{
    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken);
}
