using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trimme.BuildingBlocks.Application.Jobs;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Subscriptions.Domain;

namespace Trimme.Modules.Subscriptions.Jobs;

/// <summary>
/// Daily subscription follow-up (spec §15, R-SUB-04/05, D-107, D-113). Statuses are computed from the stored end date
/// (D-077), so nothing is flipped; this job warns the shop and the admins who follow subscriptions, once per milestone:
/// when the days left fall to the expiring-soon threshold, then 7, 3 and 1 (those within the threshold), and on the first
/// day after the end (only within a week of it, so an old lapse is not announced again). Suspended shops are skipped.
/// A missed run is caught up: the notice is for the smallest milestone not below the days left, deduplicated per
/// period end and milestone. Existing bookings are never touched (D-014).
/// </summary>
internal sealed partial class SubscriptionExpiryJob(
    TrimmeDbContext db,
    IPlatformSettings settings,
    INotificationCenter center,
    IShopDirectory shops,
    ISystemDataScope scope,
    TimeProvider clock,
    ILogger<SubscriptionExpiryJob> logger) : IRecurringJob
{
    public const string AdminPermission = "Admin.Subscriptions.View";
    private static readonly int[] Milestones = [7, 3, 1];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var platform = await settings.GetAsync(cancellationToken);
        var today = platform.LocalDate(clock.GetUtcNow());
        var threshold = platform.ExpiringSoonThresholdDays;
        var notices = 0;
        using (scope.Begin())
        {
            var coverage = await db.Set<SubscriptionCoverage>().AsNoTracking()
                .Where(c => !c.IsSuspended && c.EndDate >= today.AddDays(-7) && c.EndDate <= today.AddDays(threshold))
                .ToListAsync(cancellationToken);
            var names = await shops.FindManyAsync([.. coverage.Select(c => c.ShopId)], cancellationToken);
            foreach (var shop in coverage)
            {
                if (Notice(shop.EndDate, today, threshold) is not { } notice || names.GetValueOrDefault(shop.ShopId) is not { } summary)
                {
                    continue;
                }

                var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["endDate"] = shop.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["daysLeft"] = notice.DaysLeft.ToString(CultureInfo.InvariantCulture),
                    ["shopId"] = shop.ShopId.Value.ToString(),
                    ["shopNameAr"] = summary.NameAr,
                    ["shopNameEn"] = string.IsNullOrWhiteSpace(summary.NameEn) ? summary.NameAr : summary.NameEn,
                };
                var key = $"subscription:{shop.EndDate:yyyyMMdd}:{notice.Milestone}";
                await center.NotifyShopAsync(shop.ShopId, new InAppNotice(notice.Kind, key, parameters), cancellationToken);
                await center.NotifyAdminsAsync(AdminPermission, new InAppNotice(notice.Kind, $"{key}:{shop.ShopId.Value:N}", parameters), cancellationToken);
                notices++;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await center.PushPendingAsync(cancellationToken);
        LogChecked(logger, notices);
    }

    /// <summary>The notice due for a period ending on <paramref name="end"/>: its kind, milestone and days left; null when none is due.</summary>
    internal static (string Kind, string Milestone, int DaysLeft)? Notice(DateOnly end, DateOnly today, int threshold)
    {
        if (today > end)
        {
            return today.DayNumber - end.DayNumber <= 7 ? ("subscription.expired", "expired", 0) : null;
        }

        var daysLeft = SubscriptionStatusCalculator.DaysRemaining(end, today);
        if (daysLeft > threshold)
        {
            return null;
        }

        var milestone = Milestones.Where(m => m < threshold && m >= daysLeft).DefaultIfEmpty(threshold).Min();
        return ("subscription.expiring", milestone.ToString(CultureInfo.InvariantCulture), daysLeft);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscription expiry check sent {Count} notices")]
    private static partial void LogChecked(ILogger logger, int count);
}
