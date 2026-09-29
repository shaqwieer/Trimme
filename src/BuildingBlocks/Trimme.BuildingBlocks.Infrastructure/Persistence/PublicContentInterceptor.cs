using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Trimme.BuildingBlocks.Domain.Primitives;

namespace Trimme.BuildingBlocks.Infrastructure.Persistence;

/// <summary>Told after a save that changed public content (D-093); the web layer evicts the public response cache.</summary>
public interface IPublicContentChangeSink
{
    Task PublicContentChangedAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Notices saves that add, change or delete an <see cref="IPublicContent"/> entity and, once the save succeeded, tells
/// <see cref="IPublicContentChangeSink"/>. Inside an explicit transaction the notice comes before the commit, so a page
/// read in between may be cached until the next change or the cache expiry (at most a few minutes).
/// </summary>
internal sealed class PublicContentInterceptor(IPublicContentChangeSink sink) : SaveChangesInterceptor
{
    private bool _pending;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Detect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Detect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await NotifyAsync(cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        NotifyAsync(CancellationToken.None).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        _pending = false;
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending = false;
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Detect(DbContext? context) =>
        _pending |= context is not null && context.ChangeTracker.Entries()
            .Any(e => e.Entity is IPublicContent && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

    private async Task NotifyAsync(CancellationToken cancellationToken)
    {
        if (_pending)
        {
            _pending = false;
            await sink.PublicContentChangedAsync(cancellationToken);
        }
    }
}
