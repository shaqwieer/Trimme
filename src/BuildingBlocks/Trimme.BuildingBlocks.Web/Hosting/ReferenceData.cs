using Microsoft.Extensions.DependencyInjection;

namespace Trimme.BuildingBlocks.Web.Hosting;

/// <summary>Runs every <see cref="IReferenceDataSynchronizer"/> in order (the <c>migrate</c> command and test hosts).</summary>
public static class ReferenceData
{
    public static async Task<int> SynchronizeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        var synchronizers = services.GetServices<IReferenceDataSynchronizer>()
            .OrderBy(s => s.Order)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToArray();

        foreach (var synchronizer in synchronizers)
        {
            await synchronizer.SynchronizeAsync(services, cancellationToken);
        }

        return synchronizers.Length;
    }
}
