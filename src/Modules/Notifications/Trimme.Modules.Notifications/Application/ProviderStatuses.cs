using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Jobs;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Infrastructure.Privacy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.Modules.Notifications.Application;

/// <summary>A status Meta reported for one message: its id, <c>sent</c>/<c>delivered</c>/<c>read</c>/<c>failed</c>, and the first error.</summary>
public sealed record ProviderStatusUpdate(string ProviderMessageId, string Status, string? Error);

internal sealed record ApplyProviderStatusesCommand(IReadOnlyList<ProviderStatusUpdate> Updates) : ICommand<int>;

/// <summary>
/// Applies provider-reported statuses to the matching dispatches (statuses only move forward) and, for a professional's
/// message, enqueues the job that records the number's delivery state. Unknown message ids are ignored. Returns how many
/// dispatches changed.
/// </summary>
internal sealed class ApplyProviderStatusesHandler(TrimmeDbContext db, IJobScheduler jobs, TimeProvider clock) : ICommandHandler<ApplyProviderStatusesCommand, int>
{
    public async Task<int> Handle(ApplyProviderStatusesCommand command, CancellationToken cancellationToken)
    {
        var ids = command.Updates.Select(u => u.ProviderMessageId).Distinct(StringComparer.Ordinal).Take(500).ToArray();
        var dispatches = await db.Set<WhatsAppDispatch>().Where(d => d.ProviderMessageId != null && ids.Contains(d.ProviderMessageId))
            .ToListAsync(cancellationToken);
        var changed = new List<WhatsAppDispatch>();
        foreach (var update in command.Updates)
        {
            if (dispatches.FirstOrDefault(d => d.ProviderMessageId == update.ProviderMessageId) is not { } dispatch)
            {
                continue;
            }

            var before = dispatch.Status;
            var status = update.Status switch
            {
                "delivered" => DispatchStatus.Delivered,
                "read" => DispatchStatus.Read,
                "failed" => DispatchStatus.Failed,
                _ => (DispatchStatus?)null,
            };
            if (status is { } next)
            {
                dispatch.RecordProviderStatus(next, update.Error is null ? null : SensitiveDataRedactor.Redact(update.Error), clock.GetUtcNow());
            }

            if (dispatch.Status != before)
            {
                changed.Add(dispatch);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var dispatch in changed.Where(d => d.Audience == MessageAudience.Professional && d.Kind != DispatchKind.Test).Distinct())
        {
            Jobs.ProfessionalDeliveryJob.Enqueue(jobs, dispatch.Id.Value);
        }

        return changed.Count;
    }
}
