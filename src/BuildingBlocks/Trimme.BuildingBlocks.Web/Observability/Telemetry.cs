using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Trimme.BuildingBlocks.Web.Observability;

/// <summary>
/// TRIMME's own trace source and meter (D-118). Tags carry names, types and outcomes only: never identifiers of people,
/// phone numbers, message text or job arguments.
/// </summary>
public static class TrimmeTelemetry
{
    public const string Name = "Trimme";

    public static readonly ActivitySource Source = new(Name);

    public static readonly Meter Meter = new(Name);

    /// <summary>Background jobs run, by job and outcome (<c>succeeded</c>, <c>failed</c>).</summary>
    public static readonly Counter<long> JobsExecuted = Meter.CreateCounter<long>(
        "trimme.jobs.executed", unit: "{job}", description: "Background jobs run, by job and outcome.");

    public static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>(
        "trimme.jobs.duration", unit: "s", description: "Background job run time.");

    /// <summary>Outbox messages delivered to every consumer.</summary>
    public static readonly Counter<long> OutboxProcessed = Meter.CreateCounter<long>(
        "trimme.outbox.processed", unit: "{message}", description: "Outbox messages delivered to every consumer.");

    /// <summary>Failed consumer deliveries (each is retried with backoff, then dead-lettered).</summary>
    public static readonly Counter<long> OutboxFailures = Meter.CreateCounter<long>(
        "trimme.outbox.failures", unit: "{delivery}", description: "Failed outbox consumer deliveries.");

    /// <summary>Time from the change being committed to the message being fully delivered.</summary>
    public static readonly Histogram<double> OutboxDeliveryLag = Meter.CreateHistogram<double>(
        "trimme.outbox.delivery_lag", unit: "s", description: "Time from commit to full delivery of an outbox message.");
}

public static class TelemetrySetup
{
    /// <summary>The standard OpenTelemetry variable; when it is empty nothing is exported.</summary>
    public const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>The share of traces kept (0–1, default 1), with the standard variable's name.</summary>
    public const string SampleRatioKey = "OTEL_TRACES_SAMPLER_ARG";

    private static readonly PathString HealthPath = "/health";

    private static double SampleRatio(IConfiguration configuration) =>
        double.TryParse(configuration[SampleRatioKey], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ratio)
            ? Math.Clamp(ratio, 0, 1)
            : 1;

    /// <summary>
    /// Traces and metrics for ASP.NET Core, outgoing HTTP, PostgreSQL (Npgsql, which carries every EF Core command), the
    /// .NET runtime, Hangfire jobs and the outbox (D-118). The OTLP exporter is added only when
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, so each environment chooses its collector by configuration alone
    /// (<c>OTEL_EXPORTER_OTLP_PROTOCOL</c>, <c>OTEL_EXPORTER_OTLP_HEADERS</c> and <c>OTEL_SERVICE_NAME</c> work as usual).
    /// </summary>
    public static IServiceCollection AddTrimmeTelemetry(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var export = !string.IsNullOrWhiteSpace(configuration[OtlpEndpointKey]);
        var version = typeof(TelemetrySetup).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService("trimme-api", serviceVersion: version)
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment.name", environment.EnvironmentName)]))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new ParentBasedSampler(new SkipBackgroundPollingSampler(SampleRatio(configuration))))
                    .AddSource(TrimmeTelemetry.Name)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        // Probes would drown the traces. Query string values are redacted by the instrumentation itself.
                        options.Filter = context => !context.Request.Path.StartsWithSegments(HealthPath);
                    })
                    .AddHttpClientInstrumentation()
                    .AddNpgsql();
                if (export)
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(TrimmeTelemetry.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddNpgsqlInstrumentation();
                if (export)
                {
                    metrics.AddOtlpExporter();
                }
            });

        return services;
    }
}

/// <summary>
/// Keeps every trace except parentless client spans (D-118). Requests, Hangfire jobs and outbox messages each start their
/// own span, so every database command or provider call made for real work has a parent. What is left without one is
/// background polling: the outbox loop every few seconds and Hangfire's queue and heartbeat queries, which would
/// outnumber real traces many times over and carry no context. (Npgsql adds its tags only after sampling, so the
/// decision cannot look at <c>db.system</c>.)
/// </summary>
internal sealed class SkipBackgroundPollingSampler(double ratio) : Sampler
{
    private readonly Sampler _roots = ratio >= 1 ? new AlwaysOnSampler() : new TraceIdRatioBasedSampler(Math.Max(0, ratio));

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters) =>
        samplingParameters.Kind == System.Diagnostics.ActivityKind.Client
            ? new SamplingResult(SamplingDecision.Drop)
            : _roots.ShouldSample(samplingParameters);
}
