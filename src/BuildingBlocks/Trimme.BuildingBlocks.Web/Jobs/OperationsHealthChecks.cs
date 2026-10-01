using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Infrastructure.Persistence;

namespace Trimme.BuildingBlocks.Web.Jobs;

/// <summary>Thresholds for the background-work health checks (D-118). Bound from <c>Jobs:Health</c>.</summary>
public sealed class JobsHealthOptions
{
    public const string SectionName = "Jobs:Health";

    /// <summary>The oldest undelivered outbox message may wait this long before the check reports Degraded.</summary>
    public int OutboxLagSeconds { get; set; } = 300;

    /// <summary>A Hangfire server whose last heartbeat is older than this counts as gone.</summary>
    public int ServerHeartbeatSeconds { get; set; } = 120;
}

/// <summary>
/// Is a Hangfire server of this deployment alive? Reports Degraded, never Unhealthy (D-118): readiness gates traffic, and
/// stalled jobs must not take the API out of rotation, only raise an alert.
/// </summary>
internal sealed class HangfireHealthCheck(
    JobStorage storage, IOptions<JobsOptions> jobs, IOptions<JobsHealthOptions> health, IHostEnvironment environment, TimeProvider clock) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!jobs.Value.ResolveEnabled(environment))
        {
            return Task.FromResult(HealthCheckResult.Healthy("Jobs are disabled in this process."));
        }

        try
        {
            var cutoff = clock.GetUtcNow().UtcDateTime.AddSeconds(-health.Value.ServerHeartbeatSeconds);
            var servers = storage.GetMonitoringApi().Servers();
            var alive = servers.Count(server => server.Heartbeat is { } beat && beat >= cutoff);
            var data = new Dictionary<string, object> { ["servers"] = alive };
            return Task.FromResult(alive > 0
                ? HealthCheckResult.Healthy(data: data)
                : HealthCheckResult.Degraded("No Hangfire server has sent a heartbeat recently.", data: data));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromResult(HealthCheckResult.Degraded("The Hangfire storage could not be read."));
        }
    }
}

/// <summary>
/// How far behind is the outbox? Degraded when the oldest message still due waits longer than the threshold, or when a
/// message has been dead-lettered (an operator must replay it). Never Unhealthy, for the same reason as above.
/// </summary>
internal sealed class OutboxHealthCheck(TrimmeDbContext db, IOptions<JobsHealthOptions> health, TimeProvider clock) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var pending = db.Set<OutboxMessage>().AsNoTracking().Where(m => m.ProcessedAt == null);
            var oldest = await pending.Where(m => m.DeadLetteredAt == null)
                .OrderBy(m => m.OccurredAt).Select(m => (DateTimeOffset?)m.OccurredAt).FirstOrDefaultAsync(cancellationToken);
            var deadLettered = await pending.CountAsync(m => m.DeadLetteredAt != null, cancellationToken);

            var lagSeconds = oldest is { } occurred ? Math.Max(0, (clock.GetUtcNow() - occurred).TotalSeconds) : 0;
            var data = new Dictionary<string, object> { ["lagSeconds"] = Math.Round(lagSeconds), ["deadLettered"] = deadLettered };
            if (lagSeconds > health.Value.OutboxLagSeconds)
            {
                return HealthCheckResult.Degraded("Outbox delivery is behind.", data: data);
            }

            return deadLettered > 0
                ? HealthCheckResult.Degraded("Outbox messages are dead-lettered.", data: data)
                : HealthCheckResult.Healthy(data: data);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Degraded("The outbox could not be read.");
        }
    }
}
