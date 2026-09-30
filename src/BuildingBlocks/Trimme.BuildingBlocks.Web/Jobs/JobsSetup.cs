using System.Linq.Expressions;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Trimme.BuildingBlocks.Application.Jobs;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Web.Security;

namespace Trimme.BuildingBlocks.Web.Jobs;

/// <summary>Background work settings (D-108). Bound from <c>Jobs</c>.</summary>
public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary>
    /// Runs the Hangfire server, the outbox processor and the recurring jobs in this process. Defaults to on, except in
    /// the Testing environment, where tests call the processor and the jobs themselves. Setting it to false is also the
    /// rollback switch: messages then only accumulate in the outbox.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>How often the outbox processor looks for new messages.</summary>
    public int OutboxPollSeconds { get; set; } = 2;

    public int OutboxBatchSize { get; set; } = 50;

    public int WorkerCount { get; set; } = 4;

    public bool ResolveEnabled(IHostEnvironment environment) => Enabled ?? !environment.IsEnvironment("Testing");
}

/// <summary>A recurring job and its schedule, registered with Hangfire when the jobs start.</summary>
public sealed record RecurringJobRegistration(string Id, string Cron, Action<IRecurringJobManager, TimeZoneInfo> Register);

public static class JobsSetup
{
    public const string Schema = "hangfire";
    public const string DashboardPath = "/api/ops/jobs";
    public const string DashboardPermission = "Admin.Jobs.View";

    /// <summary>
    /// Hangfire with PostgreSQL storage in its own schema (D-108). The storage is a lazy singleton passed explicitly to the
    /// client, the server and the dashboard, so parallel test hosts on different databases never share Hangfire's static
    /// <c>JobStorage.Current</c>, and building the host opens no connection. The schema is installed by <c>migrate</c>
    /// (<see cref="HangfireSchemaSynchronizer"/>), never at API startup.
    /// </summary>
    public static IServiceCollection AddTrimmeJobs(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.Configure<JobsOptions>(configuration.GetSection(JobsOptions.SectionName));
        var options = configuration.GetSection(JobsOptions.SectionName).Get<JobsOptions>() ?? new JobsOptions();

        services.AddSingleton<JobStorage>(serviceProvider => new PostgreSqlStorage(
            new Hangfire.PostgreSql.Factories.NpgsqlConnectionFactory(ConnectionString(serviceProvider), StorageOptions()),
            StorageOptions()));
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings());
        services.AddSingleton<IJobScheduler, HangfireJobScheduler>();
        services.AddSingleton<IReferenceDataSynchronizer, HangfireSchemaSynchronizer>();
        services.AddScoped<OutboxProcessor>();
        services.AddRecurringJob<OutboxMaintenanceJob>("outbox-maintenance", "30 3 * * *");

        if (options.ResolveEnabled(environment))
        {
            services.AddHangfireServer((serviceProvider, server) =>
            {
                server.WorkerCount = Math.Clamp(options.WorkerCount, 1, 20);
                server.ServerName = $"trimme-api:{Environment.MachineName}";
            });
            services.AddHostedService<OutboxProcessorService>();
            services.AddHostedService<RecurringJobsRegistrar>();
        }

        return services;
    }

    /// <summary>Registers <typeparamref name="TJob"/> to run on <paramref name="cron"/> (platform time zone).</summary>
    public static IServiceCollection AddRecurringJob<TJob>(this IServiceCollection services, string id, string cron)
        where TJob : class, IRecurringJob
    {
        services.AddScoped<TJob>();
        services.AddSingleton(new RecurringJobRegistration(id, cron, (manager, zone) =>
            manager.AddOrUpdate<TJob>(id, job => job.RunAsync(CancellationToken.None), cron, new RecurringJobOptions { TimeZone = zone })));
        return services;
    }

    /// <summary>
    /// The operations dashboard at <c>/api/ops/jobs</c>, read-only, for platform admins with <c>Admin.Jobs.View</c>. It is
    /// under <c>/api</c> so the single-domain routing reaches it (D-027); it is never public.
    /// </summary>
    public static IApplicationBuilder UseTrimmeJobsDashboard(this IApplicationBuilder app)
    {
        var storage = app.ApplicationServices.GetRequiredService<JobStorage>();
        return app.UseHangfireDashboard(DashboardPath, new DashboardOptions
        {
            Authorization = [],
            AsyncAuthorization = [new PermissionDashboardFilter()],
            IsReadOnlyFunc = _ => true,
            DisplayStorageConnectionString = false,
            DashboardTitle = "TRIMME jobs",
            AppPath = null,
        }, storage);
    }

    internal static PostgreSqlStorageOptions StorageOptions() => new()
    {
        SchemaName = Schema,
        PrepareSchemaIfNecessary = false,
        QueuePollInterval = TimeSpan.FromSeconds(2),
        InvisibilityTimeout = TimeSpan.FromMinutes(5),
    };

    internal static string ConnectionString(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(PersistenceServiceCollectionExtensions.ConnectionStringName)
        ?? throw new InvalidOperationException("Connection string 'Trimme' is not configured.");
}

/// <summary><see cref="IJobScheduler"/> over Hangfire, on the host's own storage.</summary>
internal sealed class HangfireJobScheduler(JobStorage storage) : IJobScheduler
{
    private readonly BackgroundJobClient _client = new(storage);

    public string Enqueue<TJob>(Expression<Func<TJob, Task>> job)
        where TJob : class => _client.Enqueue(job);

    public string Schedule<TJob>(Expression<Func<TJob, Task>> job, DateTimeOffset runAt)
        where TJob : class => _client.Schedule(job, runAt);

    public bool Delete(string jobId) => _client.Delete(jobId);
}

/// <summary>Installs or upgrades the Hangfire schema during <c>migrate</c> (idempotent), never at API startup.</summary>
internal sealed class HangfireSchemaSynchronizer : IReferenceDataSynchronizer
{
    public int Order => 10;

    public string Name => "Hangfire schema";

    public async Task SynchronizeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(JobsSetup.ConnectionString(services));
        await connection.OpenAsync(cancellationToken);
        PostgreSqlObjectsInstaller.Install(connection, JobsSetup.Schema);
    }
}

/// <summary>Registers the recurring jobs when the jobs start (writes to Hangfire storage, so not during host build).</summary>
internal sealed partial class RecurringJobsRegistrar(
    IEnumerable<RecurringJobRegistration> registrations,
    JobStorage storage,
    IServiceScopeFactory scopes,
    ILogger<RecurringJobsRegistrar> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(await PlatformTimeZoneAsync(cancellationToken));
        var manager = new RecurringJobManager(storage);
        foreach (var registration in registrations)
        {
            try
            {
                registration.Register(manager, zone);
            }
            catch (Exception exception)
            {
                LogRegistrationFailed(logger, exception, registration.Id);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<string> PlatformTimeZoneAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var platform = scope.ServiceProvider.GetService<Application.Platform.IPlatformSettings>();
            return platform is null ? "Asia/Riyadh" : (await platform.GetAsync(cancellationToken)).TimeZone;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return "Asia/Riyadh";
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Registering recurring job {JobId} failed")]
    private static partial void LogRegistrationFailed(ILogger logger, Exception exception, string jobId);
}

/// <summary>Only a signed-in platform admin whose roles grant <c>Admin.Jobs.View</c> sees the dashboard.</summary>
internal sealed class PermissionDashboardFilter : IDashboardAsyncAuthorizationFilter
{
    public async Task<bool> AuthorizeAsync(DashboardContext context)
    {
        var http = context.GetHttpContext();
        var user = http.User;
        if (user.Identity?.IsAuthenticated != true
            || user.FindFirst(TrimmeClaims.UserType)?.Value != UserTypes.PlatformAdmin
            || !Guid.TryParse(user.FindFirst(TrimmeClaims.Subject)?.Value, out var userId))
        {
            return false;
        }

        var resolver = http.RequestServices.GetRequiredService<IPermissionResolver>();
        return (await resolver.GetPermissionsAsync(userId, http.RequestAborted)).Contains(JobsSetup.DashboardPermission);
    }
}
