using System.Diagnostics;
using Hangfire.Server;
using Trimme.BuildingBlocks.Web.Observability;

namespace Trimme.BuildingBlocks.Web.Jobs;

/// <summary>
/// One span and one measurement per Hangfire job run (D-118), named after the job's type and method. Job arguments are
/// never recorded: they hold booking and dispatch identifiers, and the spans and metrics must not grow per person.
/// </summary>
internal sealed class JobTelemetryFilter : IServerFilter
{
    private const string ActivityKey = "trimme.activity";
    private const string StartedKey = "trimme.started";

    public void OnPerforming(PerformingContext context)
    {
        var name = JobName(context);
        var activity = TrimmeTelemetry.Source.StartActivity($"job {name}", ActivityKind.Internal);
        activity?.SetTag("job.name", name);
        context.Items[ActivityKey] = activity;
        context.Items[StartedKey] = Stopwatch.GetTimestamp();
    }

    public void OnPerformed(PerformedContext context)
    {
        var name = JobName(context);
        var outcome = context.Exception is null || context.ExceptionHandled ? "succeeded" : "failed";
        var tags = new TagList { { "job.name", name }, { "job.outcome", outcome } };
        TrimmeTelemetry.JobsExecuted.Add(1, tags);
        if (context.Items.TryGetValue(StartedKey, out var started) && started is long timestamp)
        {
            TrimmeTelemetry.JobDuration.Record(Stopwatch.GetElapsedTime(timestamp).TotalSeconds, tags);
        }

        if (context.Items.TryGetValue(ActivityKey, out var value) && value is Activity activity)
        {
            activity.SetTag("job.outcome", outcome);
            if (outcome == "failed")
            {
                // The exception type only: messages can quote data the job was handling.
                activity.SetStatus(ActivityStatusCode.Error, context.Exception?.GetType().Name);
            }

            activity.Dispose();
        }
    }

    private static string JobName(PerformContext context) =>
        $"{context.BackgroundJob.Job.Type.Name}.{context.BackgroundJob.Job.Method.Name}";
}
