using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Trimme.BuildingBlocks.Application.Realtime;

namespace Trimme.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Publishes live operations events for the outbox messages a unit of work wrote, once they are committed (D-099):
/// <list type="bullet">
/// <item>a save outside an explicit transaction publishes after it succeeded;</item>
/// <item>inside an explicit transaction the events wait for the commit, and are dropped on rollback or failure;</item>
/// <item>a publishing failure is logged, never thrown: the command already succeeded.</item>
/// </list>
/// Every writer that records an outbox message is covered without calling anything itself. One instance per context.
/// </summary>
internal sealed class OperationsEventsInterceptor(
    IOperationsPublisher publisher,
    IReadOnlyList<IOperationsEventProjector> projectors,
    ILogger<OperationsEventsInterceptor> logger) : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly List<OperationsEvent> _saving = [];
    private readonly List<OperationsEvent> _awaitingCommit = [];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await SavedAsync(eventData.Context);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        SavedAsync(eventData.Context).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        _saving.Clear();
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _saving.Clear();
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    public async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
        await FlushCommittedAsync();

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        FlushCommittedAsync().GetAwaiter().GetResult();

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _awaitingCommit.Clear();
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => _awaitingCommit.Clear();

    public Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _awaitingCommit.Clear();
        return Task.CompletedTask;
    }

    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) => _awaitingCommit.Clear();

    private void Capture(DbContext? context)
    {
        _saving.Clear();
        if (context is null || projectors.Count == 0)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<OutboxMessage>().Where(e => e.State == EntityState.Added))
        {
            foreach (var projector in projectors)
            {
                if (projector.Project(entry.Entity.Type, entry.Entity.Payload) is { } change)
                {
                    _saving.Add(change);
                    break;
                }
            }
        }
    }

    private async Task SavedAsync(DbContext? context)
    {
        if (_saving.Count == 0)
        {
            return;
        }

        if (context?.Database.CurrentTransaction is not null)
        {
            _awaitingCommit.AddRange(_saving);
            _saving.Clear();
            return;
        }

        var events = _saving.ToList();
        _saving.Clear();
        await PublishAsync(events);
    }

    private async Task FlushCommittedAsync()
    {
        if (_awaitingCommit.Count == 0)
        {
            return;
        }

        var events = _awaitingCommit.ToList();
        _awaitingCommit.Clear();
        await PublishAsync(events);
    }

    private async Task PublishAsync(List<OperationsEvent> events)
    {
        try
        {
            // Not the request's token: the change is committed, so the notice goes out even if the caller disconnects.
            await publisher.PublishAsync(events, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Publishing {Count} operations events failed", events.Count);
        }
    }
}
