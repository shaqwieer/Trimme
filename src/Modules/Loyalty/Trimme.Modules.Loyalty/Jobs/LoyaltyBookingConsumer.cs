using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Loyalty;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Application;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Jobs;

/// <summary>The part of the booking outbox payload (Bookings' <c>BookingEvent</c>, D-089) the loyalty card needs.</summary>
internal sealed record LoyaltyBookingEvent(
    Guid BookingId, Guid ShopId, Guid ProfessionalId, Guid? CustomerId, string Status, string Channel, Guid? ActorId = null, string? ActorName = null);

/// <summary>
/// Keeps the loyalty points in step with bookings (D-149), from the booking outbox (at least once; the processor makes each
/// message run once per consumer, and the ledger's unique indexes make it idempotent):
/// <list type="bullet">
/// <item>Completed («انتهت الخدمة»): a reward held for the booking is used (its points leave the card). Otherwise, while
/// the shop's loyalty add-on is in force, the visit earns the first-visit or the later-visit points, recorded with the
/// barber who closed it. Points are never earned on booking, arrival or a no-show. A visit that used a reward earns none
/// (as under D-132). The member is the online account's mobile or the number typed into a walk-in or phone booking; a
/// booking with no number earns nothing. The signed-in customer is told «خلصت زيارتك» in-app with the points.</item>
/// <item>Cancelled (by anyone) or a no-show: a reward held for the booking goes back to the card.</item>
/// <item>A D-132 coupon held for the booking is redeemed or released the same way; no new stamps are given.</item>
/// </list>
/// </summary>
internal sealed class LoyaltyBookingConsumer(
    TrimmeDbContext db,
    LoyaltyLedger ledger,
    ILoyaltyAddOnStatus addOn,
    ILoyaltyMemberKeys keys,
    IWalkInContactReader walkIns,
    IPersonalDataProtector protector,
    INotificationCenter center,
    IShopDirectory shops,
    TimeProvider clock) : IOutboxConsumer
{
    public const string ConsumerName = "loyalty.booking";

    /// <summary>The in-app notice after a completed visit («خلصت زيارتك»).</summary>
    public const string VisitNoticeKind = "loyalty.visit_completed";

    public string Name => ConsumerName;

    public bool Handles(string messageType) => messageType is "booking.cancelled" or "booking.status_changed";

    public async Task HandleAsync(OutboxEnvelope message, OutboxConsumerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        var change = JsonSerializer.Deserialize<LoyaltyBookingEvent>(message.Payload, JsonSerializerOptions.Web);
        if (change is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var coupon = change.CustomerId is null ? null : await db.Set<LoyaltyReward>().SingleOrDefaultAsync(r => r.BookingId == change.BookingId, cancellationToken);
        var redemption = await db.Set<LoyaltyRedemption>().SingleOrDefaultAsync(r => r.BookingId == change.BookingId, cancellationToken);
        if (message.Type == "booking.cancelled" || change.Status == "NoShow")
        {
            coupon?.Release(change.BookingId);
            redemption?.Return(now);
            return;
        }

        if (change.Status != "Completed")
        {
            return;
        }

        coupon?.Redeem(change.BookingId, now);
        var shopId = new ShopId(change.ShopId);
        var memberKey = redemption?.MemberKey ?? await MemberKeyAsync(change, cancellationToken);
        if (memberKey is null)
        {
            return;
        }

        var active = await addOn.IsActiveAsync(shopId, cancellationToken);
        if (!active && redemption is not { Status: LoyaltyRedemptionStatus.Held })
        {
            return;
        }

        await ledger.LockAsync(shopId, memberKey, cancellationToken);
        var settings = await ledger.SettingsAsync(shopId, cancellationToken);
        var account = await ledger.AccountAsync(shopId, memberKey, track: true, cancellationToken);
        if (account is null)
        {
            account = LoyaltyAccount.Open(shopId, memberKey, now);
            db.Add(account);
        }

        // Points held for other bookings, and for this one until it is used, are not lost without visits.
        var firstVisit = account.LastVisitAt is null;
        var heldElsewhere = await ledger.HeldAsync(shopId, memberKey, change.BookingId, cancellationToken);
        var heldHere = redemption is { Status: LoyaltyRedemptionStatus.Held } ? redemption.Points : 0;
        var (kind, lost, lostAt) = account.Settle(settings, heldElsewhere + heldHere, now);
        if (lost > 0)
        {
            db.Add(LoyaltyEntry.Of(account, kind, -lost, null, null, null, null, lostAt));
        }

        var earned = 0;
        if (redemption is not null && redemption.Consume(now))
        {
            account.Consume(redemption.Points, now);
            db.Add(LoyaltyEntry.Of(account, LoyaltyEntryKind.Redeemed, -redemption.Points, change.BookingId, change.ProfessionalId, change.ActorId, change.ActorName, now));
        }
        else if (active && settings is not null && coupon is null
                 && !await db.Set<LoyaltyEntry>().AnyAsync(e => e.BookingId == change.BookingId && e.Kind == LoyaltyEntryKind.Earned, cancellationToken))
        {
            earned = firstVisit ? settings.FirstVisitPoints : settings.VisitPoints;
            db.Add(LoyaltyEntry.Of(account, LoyaltyEntryKind.Earned, earned, change.BookingId, change.ProfessionalId, change.ActorId, change.ActorName, now));
        }

        // Any completed visit starts the inactivity clock again.
        account.Visit(earned, now);

        if (change.CustomerId is { } customerId && active && settings is not null)
        {
            var card = LoyaltyBalance.Card(Math.Max(account.Points - heldElsewhere, 0), settings.PointsForReward);
            await TellCustomerAsync(customerId, shopId, change.BookingId, earned, card, settings, cancellationToken);
            context.AfterCommit(ct => center.PushPendingAsync(ct));
        }
    }

    /// <summary>The member of the booking: the online account's mobile, or the walk-in's typed number; null without one.</summary>
    private async Task<string?> MemberKeyAsync(LoyaltyBookingEvent change, CancellationToken cancellationToken)
    {
        if (change.CustomerId is { } customerId)
        {
            return await keys.ForCustomerAsync(customerId, cancellationToken);
        }

        return change.Channel is "WalkIn" or "Phone" && await walkIns.FindMobileAsync(change.BookingId, cancellationToken) is { } mobile
            ? LoyaltyMembers.KeyOf(protector, mobile)
            : null;
    }

    private async Task TellCustomerAsync(
        Guid customerId, ShopId shopId, Guid bookingId, int earned, LoyaltyCardState card, LoyaltySettings settings, CancellationToken cancellationToken)
    {
        var shop = await shops.FindAsync(shopId, cancellationToken);
        await center.NotifyUserAsync(customerId, new InAppNotice(
            VisitNoticeKind,
            $"loyalty:{bookingId:N}",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["shopNameAr"] = shop?.NameAr ?? string.Empty,
                ["shopNameEn"] = string.IsNullOrWhiteSpace(shop?.NameEn) ? shop?.NameAr ?? string.Empty : shop.NameEn,
                ["shopSlug"] = shop?.Slug ?? string.Empty,
                ["pointsEarned"] = earned.ToString(CultureInfo.InvariantCulture),
                ["points"] = card.Points.ToString(CultureInfo.InvariantCulture),
                ["currentCard"] = card.CurrentCard.ToString(CultureInfo.InvariantCulture),
                ["pointsForReward"] = settings.PointsForReward.ToString(CultureInfo.InvariantCulture),
                ["pointsToNext"] = card.PointsToNext.ToString(CultureInfo.InvariantCulture),
                ["rewardsReady"] = card.RewardsReady.ToString(CultureInfo.InvariantCulture),
            },
            bookingId), cancellationToken);
    }
}
