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
/// booking with no number earns nothing.</item>
/// <item>Cancelled (by anyone) or a no-show: a reward held for the booking goes back to the card.</item>
/// <item>A D-132 coupon held for the booking is redeemed or released the same way; no new stamps are given.</item>
/// </list>
/// <para>
/// D-152 (the client's 3-4): every completed visit of a signed-in customer gets one in-app notice,
/// <see cref="VisitNoticeKind"/> («شكراً لزيارتك… قيّم زيارتك»), with the points when the shop's loyalty is on. It is sent
/// here, after the points are settled, so it is one notice per visit and never a WhatsApp message (no cost). Walk-in and
/// phone bookings have no account and get nothing. It replaces D-150's <c>loyalty.visit_completed</c>, which is no longer
/// sent (notices already in inboxes keep their kind).
/// </para>
/// </summary>
internal sealed class LoyaltyBookingConsumer(
    TrimmeDbContext db,
    LoyaltyLedger ledger,
    ILoyaltyPlanStatus addOn,
    ILoyaltyMemberKeys keys,
    IWalkInContactReader walkIns,
    IPersonalDataProtector protector,
    INotificationCenter center,
    IShopDirectory shops,
    TimeProvider clock) : IOutboxConsumer
{
    public const string ConsumerName = "loyalty.booking";

    /// <summary>
    /// The in-app notice after a completed visit (D-152): the shop's names and slug always; the points parameters
    /// (<c>pointsEarned</c>, <c>points</c>, <c>currentCard</c>, <c>pointsForReward</c>, <c>pointsToNext</c>,
    /// <c>rewardsReady</c>) only while the shop's loyalty add-on is on with its rules set. The notice's booking id leads to
    /// «قيّم زيارتك».
    /// </summary>
    public const string VisitNoticeKind = "booking.completed";

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
            await ThankAsync(change, context, null, cancellationToken);
            return;
        }

        var active = await addOn.IsActiveAsync(shopId, cancellationToken);
        if (!active && redemption is not { Status: LoyaltyRedemptionStatus.Held })
        {
            await ThankAsync(change, context, null, cancellationToken);
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

        var points = active && settings is not null
            ? new VisitPoints(earned, LoyaltyBalance.Card(Math.Max(account.Points - heldElsewhere, 0), settings.PointsForReward), settings)
            : null;
        await ThankAsync(change, context, points, cancellationToken);
    }

    /// <summary>What the visit did to the card, for the notice: the points it added and the card after it.</summary>
    private sealed record VisitPoints(int Earned, LoyaltyCardState Card, LoyaltySettings Settings);

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

    /// <summary>
    /// «شكراً لزيارتك» (D-152): the signed-in customer of a completed visit is thanked in-app, asked to rate it, and told the
    /// points when there are any. One per booking (the dedupe key); nothing for a booking without an account.
    /// </summary>
    private async Task ThankAsync(LoyaltyBookingEvent change, OutboxConsumerContext context, VisitPoints? points, CancellationToken cancellationToken)
    {
        if (change.CustomerId is not { } customerId)
        {
            return;
        }

        var shop = await shops.FindAsync(new ShopId(change.ShopId), cancellationToken);
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["shopNameAr"] = shop?.NameAr ?? string.Empty,
            ["shopNameEn"] = string.IsNullOrWhiteSpace(shop?.NameEn) ? shop?.NameAr ?? string.Empty : shop.NameEn,
            ["shopSlug"] = shop?.Slug ?? string.Empty,
        };
        if (points is { } visit)
        {
            parameters["pointsEarned"] = visit.Earned.ToString(CultureInfo.InvariantCulture);
            parameters["points"] = visit.Card.Points.ToString(CultureInfo.InvariantCulture);
            parameters["currentCard"] = visit.Card.CurrentCard.ToString(CultureInfo.InvariantCulture);
            parameters["pointsForReward"] = visit.Settings.PointsForReward.ToString(CultureInfo.InvariantCulture);
            parameters["pointsToNext"] = visit.Card.PointsToNext.ToString(CultureInfo.InvariantCulture);
            parameters["rewardsReady"] = visit.Card.RewardsReady.ToString(CultureInfo.InvariantCulture);
        }

        await center.NotifyUserAsync(customerId, new InAppNotice(VisitNoticeKind, $"visit:{change.BookingId:N}", parameters, change.BookingId), cancellationToken);
        context.AfterCommit(ct => center.PushPendingAsync(ct));
    }
}
