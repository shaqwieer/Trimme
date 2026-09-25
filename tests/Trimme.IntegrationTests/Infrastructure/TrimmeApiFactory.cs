using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Infrastructure.Persistence;

namespace Trimme.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real API in the <c>Testing</c> environment against a dedicated, migrated PostGIS database.
/// Extra configuration and startup filters can be supplied for scenario-specific hosts.
/// </summary>
public class TrimmeApiFactory(string connectionString, IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    public const string AllowedTestOrigin = "https://app.trimme.test";

    public string ConnectionString { get; } = connectionString;

    public Action<IServiceCollection>? ConfigureTestServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Trimme", ConnectionString);
        builder.UseSetting("Cors:AllowedOrigins:0", AllowedTestOrigin);

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        if (ConfigureTestServices is not null)
        {
            builder.ConfigureServices(ConfigureTestServices);
        }
    }

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public static async Task<TrimmeApiFactory> CreateMigratedAsync(PostgresFixture postgres, string prefix, CancellationToken cancellationToken)
    {
        var connectionString = await postgres.CreateDatabaseAsync(prefix, cancellationToken);
        var factory = new TrimmeApiFactory(connectionString);
        await factory.MigrateAsync(cancellationToken);
        return factory;
    }
}
