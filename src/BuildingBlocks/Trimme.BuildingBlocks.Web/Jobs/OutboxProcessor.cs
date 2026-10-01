using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Trimme.BuildingBlocks.Application.Jobs;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Infrastructure.Privacy;
using Trimme.BuildingBlocks.Web.Observability;

namespace Trimme.BuildingBlocks.Web.Jobs;

/// <summary>
/// Delivers committed outbox messages to their consumers (R-NTF-05, D-108), at least once:
/// <list type="bullet">
/// <item>messages due now, oldest first, skipping dead-lettered ones;</item>
/// <item>each consumer in its own transaction and service scope, with its <c>(message, consumer)</c> row written inside
/// it, so a consumer that succeeded never runs again for that message;</item>
/// <item>a failure rolls back that consumer only, and the message is retried with exponential backoff until
/// <see cref="OutboxMessage.MaxAttempts"/>, then dead-lettered;</item>
/// <item>the message is marked processed once every consumer has succeeded (or none handles its type).</item>
/// </list>
/// Consumers run in the system data scope: they are background work with no user.
/// </summary>
public sealed partial class OutboxProcessor(
    IServiceScopeFactory scopes, TimeProvider clock, IOptions<JobsOptions> options, ILogger<OutboxProcessor> logger)
{
    /// <summary>Processes one batch of due messages; returns how many messages were looked at.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        List<OutboxEnvelope> batch;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
            var now = clock.GetUtcNow();
            batch = await db.Set<OutboxMessage>().AsNoTracking()
                .Where(m => m.ProcessedAt == null && m.DeadLetteredAt == null && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
                .OrderBy(m => m.OccurredAt).ThenBy(m => m.Id)
                .Take(Math.Clamp(options.Value.OutboxBatchSize, 1, 500))
                .Select(m => new OutboxEnvelope(m.Id, m.Type, m.Payload, m.OccurredAt))
                .ToListAsync(cancellationToken);
        }

        foreach (var message in batch)
        {
            await ProcessAsync(message, cancellationToken);
        }

        return batch.Count;
    }

    private async Task ProcessAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        // One span per message (D-118), so its consumers' database commands and provider calls share a trace.
        using var activity = TrimmeTelemetry.Source.StartActivity($"outbox {message.Type}");
        activity?.SetTag("message.type", message.Type);
        string[] names;
        HashSet<string> done;
        await using (var scope = scopes.CreateAsyncScope())
        {
            names = [.. scope.ServiceProvider.GetServices<IOutboxConsumer>().Where(c => c.Handles(message.Type)).Select(c => c.Name)];
            var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
            done = [.. await db.Set<ProcessedMessage>().AsNoTracking()
                .Where(p => p.MessageId == message.Id).Select(p => p.Consumer).ToListAsync(cancellationToken)];
        }

        foreach (var name in names.Where(n => !done.Contains(n)))
        {
            var failure = await DeliverAsync(message, name, cancellationToken);
            if (failure is not null)
            {
                TrimmeTelemetry.OutboxFailures.Add(
                    1, new KeyValuePair<string, object?>("message.type", message.Type), new KeyValuePair<string, object?>("consumer", name));
                await RecordFailureAsync(message, name, failure, cancellationToken);
                return;
            }
        }

        await using var finish = scopes.CreateAsyncScope();
        var context = finish.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var row = await context.Set<OutboxMessage>().SingleAsync(m => m.Id == message.Id, cancellationToken);
        var processedAt = clock.GetUtcNow();
        row.MarkProcessed(processedAt);
        await context.SaveChangesAsync(cancellationToken);

        var type = new KeyValuePair<string, object?>("message.type", message.Type);
        TrimmeTelemetry.OutboxProcessed.Add(1, type);
        TrimmeTelemetry.OutboxDeliveryLag.Record(Math.Max(0, (processedAt - message.OccurredAt).TotalSeconds), type);
    }

    /// <summary>Runs one consumer in its own scope and transaction; returns the (redacted) failure, or null.</summary>
    private async Task<string?> DeliverAsync(OutboxEnvelope message, string consumerName, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetServices<IOutboxConsumer>().Single(c => c.Name == consumerName);
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var context = new OutboxConsumerContext();
        try
        {
            using (scope.ServiceProvider.GetRequiredService<ISystemDataScope>().Begin())
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await consumer.HandleAsync(message, context, cancellationToken);
                db.Add(new ProcessedMessage(message.Id, consumerName, clock.GetUtcNow()));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogConsumerFailed(logger, exception, message.Type, consumerName);
            return SensitiveDataRedactor.Redact($"{consumerName}: {exception.GetType().Name}: {exception.Message}");
        }

        foreach (var action in context.AfterCommitActions)
        {
            try
            {
                await action(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The work is committed; a follow-up (enqueuing a job, a live notice) is covered by the sweeps.
                LogAfterCommitFailed(logger, exception, message.Type, consumerName);
            }
        }

        return null;
    }

    private async Task RecordFailureAsync(OutboxEnvelope message, string consumerName, string failure, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var row = await db.Set<OutboxMessage>().SingleAsync(m => m.Id == message.Id, cancellationToken);
        if (row.RecordFailure(failure, clock.GetUtcNow()))
        {
            LogDeadLettered(logger, message.Id, message.Type, consumerName, row.Attempts);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox consumer {Consumer} failed for a {MessageType} message")]
    private static partial void LogConsumerFailed(ILogger logger, Exception exception, string messageType, string consumer);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A follow-up of outbox consumer {Consumer} failed for a {MessageType} message")]
    private static partial void LogAfterCommitFailed(ILogger logger, Exception exception, string messageType, string consumer);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} ({MessageType}) dead-lettered after {Attempts} attempts; last failing consumer {Consumer}")]
    private static partial void LogDeadLettered(ILogger logger, Guid messageId, string messageType, string consumer, int attempts);
}

/// <summary>
/// Runs <see cref="OutboxProcessor"/> every few seconds on one API instance at a time (D-108). Leadership is a
/// PostgreSQL session advisory lock held on a dedicated connection (never a pooled EF connection, which closes between
/// commands); when that connection drops, the lock is released and another instance takes over.
/// </summary>
internal sealed partial class OutboxProcessorService(
    IServiceScopeFactory scopes, IOptions<JobsOptions> options, ILogger<OutboxProcessorService> logger) : BackgroundService
{
    /// <summary>Arbitrary constant key of the outbox leader lock.</summary>
    private const long LeaderLockKey = 0x7472_696D_6D65_0001;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.OutboxPollSeconds, 1, 60));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var leader = await TryLeadAsync(stoppingToken);
                if (leader is null)
                {
                    await Task.Delay(interval * 5, stoppingToken);
                    continue;
                }

                while (!stoppingToken.IsCancellationRequested && leader.State == System.Data.ConnectionState.Open)
                {
                    await using (var scope = scopes.CreateAsyncScope())
                    {
                        var processed = await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(stoppingToken);
                        if (processed > 0)
                        {
                            continue;
                        }
                    }

                    await Task.Delay(interval, stoppingToken);
                    await using var ping = new NpgsqlCommand("SELECT 1", leader);
                    await ping.ExecuteScalarAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogLoopFailed(logger, exception);
                await Task.Delay(interval * 5, stoppingToken);
            }
        }
    }

    private async Task<NpgsqlConnection?> TryLeadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var connection = new NpgsqlConnection(JobsSetup.ConnectionString(scope.ServiceProvider));
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", LeaderLockKey);
        if (await command.ExecuteScalarAsync(cancellationToken) is true)
        {
            return connection;
        }

        await connection.DisposeAsync();
        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The outbox processor loop failed; retrying")]
    private static partial void LogLoopFailed(ILogger logger, Exception exception);
}

/// <summary>
/// Daily clean-up (D-108): processed outbox messages and their delivery records after 30 days, and expired idempotency
/// records (R-BKG-05). Dead-lettered messages are kept for an operator.
/// </summary>
public sealed partial class OutboxMaintenanceJob(TrimmeDbContext db, TimeProvider clock, ILogger<OutboxMaintenanceJob> logger) : IRecurringJob
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var before = now - Retention;
        var messages = await db.Set<OutboxMessage>().Where(m => m.ProcessedAt != null && m.ProcessedAt < before).ExecuteDeleteAsync(cancellationToken);
        var deliveries = await db.Set<ProcessedMessage>().Where(p => p.ProcessedAt < before).ExecuteDeleteAsync(cancellationToken);
        var keys = await db.Set<IdempotencyRecord>().Where(r => r.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
        LogCleaned(logger, messages, deliveries, keys);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox maintenance removed {Messages} messages, {Deliveries} delivery records and {Keys} idempotency keys")]
    private static partial void LogCleaned(ILogger logger, int messages, int deliveries, int keys);
}
