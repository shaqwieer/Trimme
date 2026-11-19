using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Notifications.Application;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.Modules.Notifications.Jobs;

/// <summary>
/// The monthly marketing limit of a shop's plan (D-155, the client's «الحد ينفحص في السيرفر وقت الإرسال»), checked before a
/// win-back message goes to the provider; no other message is ever counted or stopped. One slot per message, taken once
/// by an atomic increment that cannot pass the limit (two sends at the last slot never both pass, however many go out at
/// once) and given back if the message never reaches the provider. The window is a month from the day the shop's
/// subscription period started. A plan without a limit is unlimited (its messages are still counted); 0 sends none. When
/// there is no slot, the dispatch is kept as «وصل الحد» and, at the window's first refusal, the owner is told. Runs inside
/// the dispatch job's system scope.
/// </summary>
internal sealed class MarketingLimitGate(TrimmeDbContext db, IMarketingPlans plans, INotificationCenter center, TimeProvider clock)
{
    /// <summary>Whether the dispatch may go to the provider; a refused one is marked <see cref="DispatchStatus.LimitReached"/>.</summary>
    public async Task<bool> PassAsync(WhatsAppDispatch dispatch, CancellationToken cancellationToken)
    {
        if (!dispatch.CountsAgainstAllowance || dispatch.AllowanceMonth is not null || dispatch.ShopId is not { } shopId)
        {
            return true;
        }

        var now = clock.GetUtcNow();
        var terms = await plans.ForShopAsync(shopId, cancellationToken);
        if (terms.Allowance is not 0 && await TakeSlotAsync(shopId, terms.WindowStart, terms.Allowance, cancellationToken))
        {
            dispatch.ReserveAllowance(terms.WindowStart);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        dispatch.RecordLimitReached(
            string.Create(CultureInfo.InvariantCulture, $"{WhatsAppErrors.MarketingLimitReached}: {terms.Allowance ?? 0} from {terms.WindowStart:yyyy-MM-dd}"), now);
        if (await FirstRefusalAsync(shopId, terms.WindowStart, now, cancellationToken))
        {
            await center.NotifyShopAsync(shopId, new InAppNotice(
                NoticeKinds.MarketingLimitReached,
                string.Create(CultureInfo.InvariantCulture, $"marketing-limit:{terms.WindowStart:yyyy-MM-dd}"),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["allowance"] = (terms.Allowance ?? 0).ToString(CultureInfo.InvariantCulture),
                    ["windowStart"] = terms.WindowStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["windowEnd"] = terms.WindowEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                }), cancellationToken);
        }

        return false;
    }

    /// <summary>Gives the slot back when the message ended without the provider accepting it.</summary>
    public async Task ReleaseIfUnsentAsync(WhatsAppDispatch dispatch, CancellationToken cancellationToken)
    {
        if (dispatch.Status != DispatchStatus.Failed || dispatch.SentAt is not null || dispatch.ShopId is not { } shopId || dispatch.ReleaseAllowance() is not { } window)
        {
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE notifications.whatsapp_usage SET used = used - 1 WHERE shop_id = {shopId.Value} AND month = {window} AND used > 0",
            cancellationToken);
    }

    private async Task<bool> TakeSlotAsync(ShopId shopId, DateOnly window, int? allowance, CancellationToken cancellationToken)
    {
        // The conflict update runs only below the limit; no row comes back once it is reached.
        var taken = allowance is { } cap
            ? await db.Database.SqlQuery<int>(
                $"INSERT INTO notifications.whatsapp_usage (shop_id, month, used) VALUES ({shopId.Value}, {window}, 1) ON CONFLICT (shop_id, month) DO UPDATE SET used = whatsapp_usage.used + 1 WHERE whatsapp_usage.used < {cap} RETURNING used AS \"Value\"")
                .ToListAsync(cancellationToken)
            : await db.Database.SqlQuery<int>(
                $"INSERT INTO notifications.whatsapp_usage (shop_id, month, used) VALUES ({shopId.Value}, {window}, 1) ON CONFLICT (shop_id, month) DO UPDATE SET used = whatsapp_usage.used + 1 RETURNING used AS \"Value\"")
                .ToListAsync(cancellationToken);
        return taken.Count > 0;
    }

    /// <summary>True once per shop and window: the refusal that tells the owner.</summary>
    private async Task<bool> FirstRefusalAsync(ShopId shopId, DateOnly window, DateTimeOffset now, CancellationToken cancellationToken) =>
        (await db.Database.SqlQuery<int>(
            $"INSERT INTO notifications.whatsapp_usage (shop_id, month, used, owner_told_at) VALUES ({shopId.Value}, {window}, 0, {now}) ON CONFLICT (shop_id, month) DO UPDATE SET owner_told_at = EXCLUDED.owner_told_at WHERE whatsapp_usage.owner_told_at IS NULL RETURNING 1 AS \"Value\"")
            .ToListAsync(cancellationToken)).Count > 0;
}
