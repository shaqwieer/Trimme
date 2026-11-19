using System.Globalization;
using Microsoft.Extensions.Logging;
using Trimme.BuildingBlocks.Application.Jobs;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.Modules.Notifications.Jobs;

/// <summary>
/// «رسالة الدعوة للعودة» (D-155, the client's fourth file and «كيف الموقع يعرف الباقة وحد الـ ٥٠٠»), once a day. For each
/// shop whose plan in force includes it, each account customer whose latest completed visit there started between N and
/// N + 7 days ago (N is the plan's «بعد كم يوم»; the week's margin means switching it on never messages old customers),
/// who has no open booking at any shop and has not turned these messages off, gets one WhatsApp (the MARKETING template)
/// and its copy in their bell. One per visit: the dedupe key names the shop, the customer and that last booking. The
/// plan's monthly marketing limit is checked when the message is sent (<see cref="MarketingLimitGate"/>), not here.
/// </summary>
internal sealed partial class WinBackJob(
    TrimmeDbContext db,
    IMarketingPlans plans,
    IWinBackCandidates candidates,
    ICustomerContactReader customers,
    IBookingNotificationSource bookings,
    IShopDirectory shops,
    IPlatformSettings settings,
    DispatchFactory factory,
    INotificationCenter center,
    IJobScheduler jobs,
    ISystemDataScope scope,
    TimeProvider clock,
    ILogger<WinBackJob> logger) : IRecurringJob
{
    /// <summary>How long after the N days a visit still gets its message (a missed day, or the setting just switched on).</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromDays(7);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var dispatches = new List<WhatsAppDispatch>();
        using (scope.Begin())
        {
            var now = clock.GetUtcNow();
            var platform = await settings.GetAsync(cancellationToken);
            foreach (var target in await plans.WinBackShopsAsync(cancellationToken))
            {
                if (await shops.FindAsync(target.ShopId, cancellationToken) is not { } shop)
                {
                    continue;
                }

                var to = now.AddDays(-target.AfterDays);
                foreach (var candidate in await candidates.ListAsync(target.ShopId, to - Margin, to, cancellationToken))
                {
                    if (await customers.FindAsync(candidate.CustomerId, cancellationToken) is not { IsActive: true, WinBackMessages: true }
                        || await bookings.FindAsync(candidate.LastBookingId, cancellationToken) is not { } booking)
                    {
                        continue;
                    }

                    var key = string.Create(CultureInfo.InvariantCulture, $"winback:{target.ShopId.Value:N}:{candidate.CustomerId:N}:{candidate.LastBookingId:N}");
                    var dispatch = await factory.CreateForBookingAsync(DispatchKind.WinBack, key, MessageEvent.WinBack, MessageAudience.Customer, booking, shop, platform, cancellationToken);
                    if (dispatch is null)
                    {
                        continue;
                    }

                    dispatches.Add(dispatch);
                    await center.NotifyUserAsync(candidate.CustomerId, new InAppNotice(
                        NoticeKinds.CustomerWinBack, key, CustomerNotices.About(booking, shop), booking.Id), cancellationToken);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            foreach (var dispatch in dispatches)
            {
                dispatch.AssignJob(SendDispatchJob.Enqueue(jobs, dispatch.Id.Value));
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        if (dispatches.Count > 0)
        {
            LogQueued(logger, dispatches.Count);
        }

        await center.PushPendingAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Win-back messages queued: {Count}")]
    private static partial void LogQueued(ILogger logger, int count);
}

/// <summary>The parameters of a customer's bell copy of a message about their booking (D-155).</summary>
internal static class CustomerNotices
{
    public static Dictionary<string, string> About(NotifiableBooking booking, ShopSummary shop) => new(StringComparer.Ordinal)
    {
        ["reference"] = booking.Reference,
        ["itemNameAr"] = booking.ItemNameAr,
        ["itemNameEn"] = booking.ItemNameEn ?? booking.ItemNameAr,
        ["professionalNameAr"] = booking.ProfessionalNameAr,
        ["professionalNameEn"] = booking.ProfessionalNameEn,
        ["startsAt"] = booking.StartsAt.ToString("O", CultureInfo.InvariantCulture),
        ["shopNameAr"] = shop.NameAr,
        ["shopNameEn"] = string.IsNullOrWhiteSpace(shop.NameEn) ? shop.NameAr : shop.NameEn,
        ["shopSlug"] = shop.Slug,
    };
}
