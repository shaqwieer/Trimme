using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Jobs;

/// <summary>The part of the booking outbox payload (Bookings' <c>BookingEvent</c>, D-089) the loyalty card needs.</summary>
internal sealed record LoyaltyBookingEvent(Guid BookingId, Guid ShopId, Guid? CustomerId, string Status);

/// <summary>
/// Keeps the loyalty cards in step with bookings (D-132), from the booking outbox (at least once; the processor makes each
/// message run once per consumer):
/// <list type="bullet">
/// <item>Completed with a held reward: the reward is redeemed and the visit earns no stamp.</item>
/// <item>Completed otherwise, while the shop's programme is active: one stamp (unique per booking); when the open card is
/// full, its oldest stamps turn into a reward with the programme's current terms.</item>
/// <item>Cancelled or no-show: a reward held for the booking is released.</item>
/// </list>
/// Walk-ins (no customer) and seed data (no outbox message) never earn stamps.
/// </summary>
internal sealed class LoyaltyBookingConsumer(TrimmeDbContext db, TimeProvider clock) : IOutboxConsumer
{
    public const string ConsumerName = "loyalty.booking";

    public string Name => ConsumerName;

    public bool Handles(string messageType) => messageType is "booking.cancelled" or "booking.status_changed";

    public async Task HandleAsync(OutboxEnvelope message, OutboxConsumerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var change = JsonSerializer.Deserialize<LoyaltyBookingEvent>(message.Payload, JsonSerializerOptions.Web);
        if (change?.CustomerId is not { } customerId)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var held = await db.Set<LoyaltyReward>().SingleOrDefaultAsync(r => r.BookingId == change.BookingId, cancellationToken);
        if (message.Type == "booking.cancelled" || change.Status == "NoShow")
        {
            held?.Release(change.BookingId);
            return;
        }

        if (change.Status != "Completed")
        {
            return;
        }

        if (held is not null)
        {
            held.Redeem(change.BookingId, now);
            return;
        }

        var shopId = new ShopId(change.ShopId);
        if (await db.Set<LoyaltyStamp>().AnyAsync(s => s.BookingId == change.BookingId, cancellationToken)
            || await db.Set<LoyaltyProgram>().SingleOrDefaultAsync(p => p.ShopId == shopId && p.IsActive, cancellationToken) is not { } program)
        {
            return;
        }

        var stamp = LoyaltyStamp.Earn(EntityId.New<LoyaltyStampId>(), shopId, customerId, change.BookingId, now);
        db.Add(stamp);
        var open = await db.Set<LoyaltyStamp>()
            .Where(s => s.ShopId == shopId && s.CustomerId == customerId && s.RewardId == null)
            .ToListAsync(cancellationToken);
        open.Add(stamp);
        if (LoyaltyCard.Filling(open, program.RequiredVisits) is { } filled)
        {
            var reward = LoyaltyReward.Issue(EntityId.New<LoyaltyRewardId>(), program, customerId, LoyaltyCard.NewCode(), now);
            db.Add(reward);
            foreach (var used in filled)
            {
                used.TurnInto(reward.Id);
            }
        }
    }
}
