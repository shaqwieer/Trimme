using System.Linq.Expressions;

namespace Trimme.BuildingBlocks.Application.Jobs;

/// <summary>
/// Background jobs (Hangfire with PostgreSQL storage, D-108), behind a port so modules never reference Hangfire.
/// Job arguments are stored and shown on the operations dashboard, so they must be identifiers, enums and times only:
/// never a name, a phone number or a rendered message.
/// </summary>
public interface IJobScheduler
{
    /// <summary>Runs <paramref name="job"/> as soon as a worker is free; returns the job id.</summary>
    string Enqueue<TJob>(Expression<Func<TJob, Task>> job)
        where TJob : class;

    /// <summary>Runs <paramref name="job"/> at <paramref name="runAt"/>; returns the job id (store it to cancel the job).</summary>
    string Schedule<TJob>(Expression<Func<TJob, Task>> job, DateTimeOffset runAt)
        where TJob : class;

    /// <summary>Deletes a scheduled or enqueued job; false when it no longer exists or already finished.</summary>
    bool Delete(string jobId);
}

/// <summary>
/// A job that runs on a schedule (registered with its cron expression by the module, see <c>AddRecurringJob</c>).
/// It runs with no user: data access opens the system scope in a <c>*.Jobs</c> namespace.
/// </summary>
public interface IRecurringJob
{
    Task RunAsync(CancellationToken cancellationToken);
}
