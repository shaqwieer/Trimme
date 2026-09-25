using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Trimme.BuildingBlocks.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string ConnectionStringName = "Trimme";
    public const string MigrationsAssemblyName = "Trimme.Migrations";

    /// <summary>
    /// Registers <see cref="TrimmeDbContext"/> on Npgsql + PostGIS. The connection string is read lazily
    /// (inside the options callback) so test hosts can override configuration after registration.
    /// No connection is opened at startup.
    /// </summary>
    public static IServiceCollection AddTrimmePersistence(this IServiceCollection services)
    {
        services.AddDbContext<TrimmeDbContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is not configured (ConnectionStrings__{ConnectionStringName}).");

            options.UseTrimmeNpgsql(connectionString);
        });

        return services;
    }

    public static DbContextOptionsBuilder UseTrimmeNpgsql(this DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(connectionString, npgsql => npgsql
                .UseNetTopologySuite()
                .MigrationsAssembly(MigrationsAssemblyName)
                .MigrationsHistoryTable(TrimmeDbContext.MigrationsHistoryTable, TrimmeDbContext.MigrationsHistorySchema))
            .UseSnakeCaseNamingConvention();
    }
}
