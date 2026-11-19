using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Jobs;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Jobs;

/// <summary>
/// Opens online booking again at a pause's return time (D-155, the client's 1-1, the user's answer: «يرجع الحجز تلقائياً»).
/// The bookability gate already reads a pause past its return time as over, so this only tidies the row away and writes
/// the audit entry (by the system). Every few minutes.
/// </summary>
internal sealed partial class OnlineBookingResumeJob(
    TrimmeDbContext db, ISystemDataScope scope, IAuditLog audit, TimeProvider clock, ILogger<OnlineBookingResumeJob> logger) : IRecurringJob
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        using (scope.Begin())
        {
            var due = await db.Set<OnlineBookingPause>()
                .Where(p => p.ResumesAt != null && p.ResumesAt <= now)
                .ToListAsync(cancellationToken);
            foreach (var pause in due)
            {
                db.Remove(pause);
                audit.Record(new AuditRecord(
                    "shop.online_booking_resumed", nameof(Shop), pause.ShopId.ToString(), pause.ShopId, "Online booking resumed at its return time",
                    SummaryArgs: AuditArgs.Of(("by", "system"), ("resumesAt", pause.ResumesAt))));
            }

            await db.SaveChangesAsync(cancellationToken);
            LogResumed(logger, due.Count);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Online booking resumed at its return time for {Count} shops")]
    private static partial void LogResumed(ILogger logger, int count);
}
