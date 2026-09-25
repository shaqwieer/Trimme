namespace Trimme.BuildingBlocks.Web.Hosting;

/// <summary>
/// Deterministic development data contributed by a module (spec §20). Seeders run only through
/// <c>Trimme.Api seed --dev</c>, only in the Development environment with <c>TRIMME_ALLOW_DEV_SEED=true</c>.
/// Implementations must be idempotent (re-running produces the same state) and must never trigger
/// outbound notifications.
/// </summary>
public interface IDevSeeder
{
    /// <summary>Lower runs first (e.g. identity 100, shops 200, bookings 500).</summary>
    int Order { get; }

    string Name { get; }

    Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken);
}
