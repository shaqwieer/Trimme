using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Notifications.Application;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.Modules.Notifications.Jobs;

/// <summary>
/// The monthly WhatsApp allowance of a shop's plan (D-149), checked before a customer message goes to the provider. One
/// slot per message, taken once by an atomic increment that cannot pass the allowance (two sends at the last slot never
/// both pass) and given back if the message never reaches the provider. A plan without an allowance is unlimited (its
/// messages are still counted); 0 sends none. When there is no slot, the message is not sent by WhatsApp (the in-app
/// notice is unaffected) and, at the month's first refusal, the owner is told. Staff messages and tests are never counted.
/// Runs inside the dispatch job's system scope.
/// </summary>
internal sealed class WhatsAppAllowanceGate(TrimmeDbContext db, IWhatsAppAllowance allowances, IShopDirectory shops, INotificationCenter center, TimeProvider clock)
{
    public const string OwnerNoticeKind = "whatsapp.allowance_reached";

    /// <summary>Whether the dispatch may go to the provider; a refused one is marked failed with <see cref="WhatsAppErrors.AllowanceReached"/>.</summary>
    public async Task<bool> PassAsync(WhatsAppDispatch dispatch, CancellationToken cancellationToken)
    {
        if (!dispatch.CountsAgainstAllowance || dispatch.AllowanceMonth is not null || dispatch.ShopId is not { } shopId)
        {
            return true;
        }

        var now = clock.GetUtcNow();
        var shop = await shops.FindAsync(shopId, cancellationToken);
        var month = WhatsAppMonths.Of(now, shop?.TimeZone ?? "Asia/Riyadh");
        var allowance = await allowances.MonthlyCustomerMessagesAsync(shopId, cancellationToken);
        if (allowance is not 0 && await TakeSlotAsync(shopId, month, allowance, cancellationToken))
        {
            dispatch.ReserveAllowance(month);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        dispatch.RecordFailure(
            string.Create(CultureInfo.InvariantCulture, $"{WhatsAppErrors.AllowanceReached}: {allowance ?? 0} this month"), permanent: true, now);
        if (await FirstRefusalAsync(shopId, month, now, cancellationToken))
        {
            await center.NotifyShopAsync(shopId, new InAppNotice(
                OwnerNoticeKind,
                string.Create(CultureInfo.InvariantCulture, $"whatsapp-allowance:{month:yyyy-MM}"),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["allowance"] = (allowance ?? 0).ToString(CultureInfo.InvariantCulture),
                    ["month"] = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                }), cancellationToken);
        }

        return false;
    }

    /// <summary>Gives the slot back when the message ended without the provider accepting it.</summary>
    public async Task ReleaseIfUnsentAsync(WhatsAppDispatch dispatch, CancellationToken cancellationToken)
    {
        if (dispatch.Status != DispatchStatus.Failed || dispatch.SentAt is not null || dispatch.ShopId is not { } shopId || dispatch.ReleaseAllowance() is not { } month)
        {
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE notifications.whatsapp_usage SET used = used - 1 WHERE shop_id = {shopId.Value} AND month = {month} AND used > 0",
            cancellationToken);
    }

    private async Task<bool> TakeSlotAsync(ShopId shopId, DateOnly month, int? allowance, CancellationToken cancellationToken)
    {
        // The conflict update runs only below the allowance; no row comes back once it is reached.
        var taken = allowance is { } cap
            ? await db.Database.SqlQuery<int>(
                $"INSERT INTO notifications.whatsapp_usage (shop_id, month, used) VALUES ({shopId.Value}, {month}, 1) ON CONFLICT (shop_id, month) DO UPDATE SET used = whatsapp_usage.used + 1 WHERE whatsapp_usage.used < {cap} RETURNING used AS \"Value\"")
                .ToListAsync(cancellationToken)
            : await db.Database.SqlQuery<int>(
                $"INSERT INTO notifications.whatsapp_usage (shop_id, month, used) VALUES ({shopId.Value}, {month}, 1) ON CONFLICT (shop_id, month) DO UPDATE SET used = whatsapp_usage.used + 1 RETURNING used AS \"Value\"")
                .ToListAsync(cancellationToken);
        return taken.Count > 0;
    }

    /// <summary>True once per shop and month: the refusal that tells the owner.</summary>
    private async Task<bool> FirstRefusalAsync(ShopId shopId, DateOnly month, DateTimeOffset now, CancellationToken cancellationToken) =>
        (await db.Database.SqlQuery<int>(
            $"INSERT INTO notifications.whatsapp_usage (shop_id, month, used, owner_told_at) VALUES ({shopId.Value}, {month}, 0, {now}) ON CONFLICT (shop_id, month) DO UPDATE SET owner_told_at = EXCLUDED.owner_told_at WHERE whatsapp_usage.owner_told_at IS NULL RETURNING 1 AS \"Value\"")
            .ToListAsync(cancellationToken)).Count > 0;
}
