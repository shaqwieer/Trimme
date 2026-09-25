using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;

namespace Trimme.Api.Hosting;

/// <summary>
/// Command-line verbs handled by the API executable (D-038):
/// <list type="bullet">
/// <item><c>healthcheck</c>: probes <c>/health/live</c> on the local port (container health checks); runs before the host is built.</item>
/// <item><c>migrate</c>: applies EF Core migrations, then exits. The API never migrates on startup.</item>
/// <item><c>seed --dev</c>: runs development seeders, only when <see cref="DevSeedGuard"/> allows it.</item>
/// </list>
/// </summary>
internal static partial class HostCommands
{
    public const string Healthcheck = "healthcheck";
    public const string Migrate = "migrate";
    public const string Seed = "seed";

    public static bool IsHealthcheck(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Healthcheck, StringComparison.OrdinalIgnoreCase);

    public static bool IsHostCommand(string[] args) =>
        args.Length > 0 && (string.Equals(args[0], Migrate, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(args[0], Seed, StringComparison.OrdinalIgnoreCase));

    public static async Task<int> RunHealthcheckAsync()
    {
        var ports = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080";
        var port = ports.Split([';', ','], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "8080";

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/health/live"));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Trimme.HostCommands");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        if (string.Equals(args[0], Migrate, StringComparison.OrdinalIgnoreCase))
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();

            // On a brand-new database the provider probes the history table once and EF logs that probe
            // as an error before creating the table. It is expected and harmless (see README troubleshooting).
            await db.Database.MigrateAsync(cancellation.Token);
            var applied = (await db.Database.GetAppliedMigrationsAsync(cancellation.Token)).Count();
            LogMigrated(logger, applied);
            return 0;
        }

        var decision = DevSeedGuard.Evaluate(
            app.Environment.EnvironmentName,
            Environment.GetEnvironmentVariable(DevSeedGuard.AllowVariable),
            args);
        if (!decision.Allowed)
        {
            LogSeedRefused(logger, decision.Reason);
            return 2;
        }

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var seeders = scope.ServiceProvider.GetServices<IDevSeeder>()
                .OrderBy(s => s.Order)
                .ThenBy(s => s.Name, StringComparer.Ordinal)
                .ToArray();

            foreach (var seeder in seeders)
            {
                LogSeeding(logger, seeder.Name);
                await seeder.SeedAsync(scope.ServiceProvider, cancellation.Token);
            }

            LogSeeded(logger, seeders.Length);
        }

        return 0;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database is up to date ({AppliedCount} migrations applied in total)")]
    private static partial void LogMigrated(ILogger logger, int appliedCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Development seed refused: {Reason}")]
    private static partial void LogSeedRefused(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Running dev seeder {Seeder}")]
    private static partial void LogSeeding(ILogger logger, string seeder);

    [LoggerMessage(Level = LogLevel.Information, Message = "Development seed completed ({SeederCount} seeders)")]
    private static partial void LogSeeded(ILogger logger, int seederCount);
}

/// <summary>The only gate that allows development seed data to be written (spec §20).</summary>
internal static class DevSeedGuard
{
    public const string AllowVariable = "TRIMME_ALLOW_DEV_SEED";

    public static SeedDecision Evaluate(string environmentName, string? allowFlag, IReadOnlyList<string> args)
    {
        if (!string.Equals(environmentName, Environments.Development, StringComparison.Ordinal))
        {
            return new SeedDecision(false, $"environment is '{environmentName}', seeding requires 'Development'");
        }

        if (!string.Equals(allowFlag, "true", StringComparison.OrdinalIgnoreCase))
        {
            return new SeedDecision(false, $"{AllowVariable} must be set to 'true'");
        }

        if (!args.Skip(1).Any(a => string.Equals(a, "--dev", StringComparison.Ordinal)))
        {
            return new SeedDecision(false, "the '--dev' argument is required");
        }

        return new SeedDecision(true, "allowed");
    }
}

internal readonly record struct SeedDecision(bool Allowed, string Reason);
