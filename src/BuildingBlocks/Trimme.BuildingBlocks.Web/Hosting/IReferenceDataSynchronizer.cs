namespace Trimme.BuildingBlocks.Web.Hosting;

/// <summary>
/// Code-defined reference data that must exist in every environment (for example the permission catalogue and the
/// system roles). Synchronizers run right after the migrations in the <c>migrate</c> command, never on API startup.
/// Implementations must be idempotent and must not touch data that admins are allowed to edit, except as documented.
/// </summary>
public interface IReferenceDataSynchronizer
{
    /// <summary>Lower runs first.</summary>
    int Order { get; }

    string Name { get; }

    Task SynchronizeAsync(IServiceProvider services, CancellationToken cancellationToken);
}
