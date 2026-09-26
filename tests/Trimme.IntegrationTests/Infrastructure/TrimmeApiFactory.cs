using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Web.Security;

namespace Trimme.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real API in the <c>Testing</c> environment against a dedicated, migrated PostGIS database.
/// Extra configuration and startup filters can be supplied for scenario-specific hosts.
/// </summary>
public class TrimmeApiFactory(string connectionString, IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    public const string AllowedTestOrigin = "https://app.trimme.test";

    /// <summary>
    /// TestServer has no client IP, so every anonymous request shares one rate-limit partition. Tests get generous
    /// limits unless a scenario sets its own (see <c>RateLimitingTests</c> and the OTP limit tests).
    /// </summary>
    private static readonly Dictionary<string, string?> GenerousRateLimits =
        RateLimitPolicies.Defaults.Keys.ToDictionary(
            policy => $"{RateLimitingSetup.SectionName}:{policy}:{nameof(RateLimitPolicyOptions.PermitLimit)}",
            _ => (string?)"10000");

    public string ConnectionString { get; } = connectionString;

    public Action<IServiceCollection>? ConfigureTestServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Trimme", ConnectionString);
        builder.UseSetting("Cors:AllowedOrigins:0", AllowedTestOrigin);

        foreach (var (key, value) in GenerousRateLimits)
        {
            builder.UseSetting(key, value);
        }

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        if (ConfigureTestServices is not null)
        {
            builder.ConfigureServices(ConfigureTestServices);
        }
    }

    /// <summary>Same as the <c>migrate</c> command: migrations, then reference data (permission catalogue, roles).</summary>
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Database.MigrateAsync(cancellationToken);
        await ReferenceData.SynchronizeAsync(scope.ServiceProvider, cancellationToken);
    }

    /// <summary>A client on <c>https://localhost</c> (session cookies are <c>Secure</c>) that keeps cookies between calls.</summary>
    public HttpClient CreateBrowserClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

    public static async Task<TrimmeApiFactory> CreateMigratedAsync(PostgresFixture postgres, string prefix, CancellationToken cancellationToken)
    {
        var connectionString = await postgres.CreateDatabaseAsync(prefix, cancellationToken);
        var factory = new TrimmeApiFactory(connectionString);
        await factory.MigrateAsync(cancellationToken);
        return factory;
    }
}
