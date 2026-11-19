using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Loyalty;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Application.Customer;

internal sealed record ListMyLoyaltyQuery : IQuery<MyLoyaltyResponse>;

internal sealed record GetMyShopLoyaltyQuery(string ShopSlug) : IQuery<MyShopLoyaltyResponse?>;

/// <summary>
/// The signed-in customer's loyalty (D-149), keyed by their account's mobile, so a card follows the number: points earned
/// online, at the shop's desk with the number typed, or by an earlier account with the same number. The cards live with
/// the shops that run or ran the add-on; they are read inside the public scope of those shops, filtered by the member key
/// of the customer's own number. D-132 coupons are the customer's own rows.
/// <para>
/// D-152 (the client's 2-5): a card never disappears because its reward was used. A shop where the customer used a D-132
/// coupon keeps its card (with the add-on off too), and every used reward is a history line with its title.
/// </para>
/// </summary>
internal sealed class CustomerLoyaltyReader(
    TrimmeDbContext db, ICurrentCustomer customer, IPublicDataScope scope, IShopDirectory shops, ILoyaltyPlanStatus addOn, ILoyaltyMemberKeys keys,
    TimeProvider clock)
{
    /// <summary>History lines per card.</summary>
    public const int HistoryLength = 50;

    public async Task<MyLoyaltyResponse> ListAsync(CancellationToken cancellationToken)
    {
        if (customer.CustomerId is not { } customerId)
        {
            return new MyLoyaltyResponse([]);
        }

        var memberKey = await keys.ForCustomerAsync(customerId, cancellationToken);
        var coupons = await CouponsAsync(customerId, null, cancellationToken);
        var used = await UsedCouponsAsync(customerId, null, cancellationToken);
        var candidates = (await addOn.ShopsWithLoyaltyAsync(IPublicDataScope.MaxShops, cancellationToken))
            .Concat(coupons.Select(c => c.ShopId)).Concat(used.Select(c => c.ShopId)).Distinct().Take(IPublicDataScope.MaxShops).ToList();
        if (candidates.Count == 0)
        {
            return new MyLoyaltyResponse([]);
        }

        var cards = await CardsAsync(candidates, memberKey, coupons, used, onlyWithData: true, cancellationToken);
        return new MyLoyaltyResponse([.. cards.OrderByDescending(c => c.RewardsReady).ThenByDescending(c => c.Points).ThenByDescending(c => c.Coupons.Count)]);
    }

    public async Task<MyShopLoyaltyResponse?> ForShopAsync(string slug, CancellationToken cancellationToken)
    {
        ShopSummary? shop;
        using (scope.Begin(shopId: null))
        {
            shop = await shops.FindBySlugAsync(slug, cancellationToken);
        }

        if (shop is not { Status: ShopStatus.Active } || customer.CustomerId is not { } customerId)
        {
            return null;
        }

        var memberKey = await keys.ForCustomerAsync(customerId, cancellationToken);
        var coupons = await CouponsAsync(customerId, shop.Id, cancellationToken);
        var used = await UsedCouponsAsync(customerId, shop.Id, cancellationToken);
        return (await CardsAsync([shop.Id], memberKey, coupons, used, onlyWithData: false, cancellationToken)).SingleOrDefault();
    }

    private async Task<List<MyShopLoyaltyResponse>> CardsAsync(
        IReadOnlyList<ShopId> shopIds,
        string? memberKey,
        IReadOnlyList<LoyaltyReward> coupons,
        IReadOnlyList<LoyaltyReward> usedCoupons,
        bool onlyWithData,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var summaries = await shops.FindManyAsync(shopIds, cancellationToken);
        var active = await addOn.ActiveManyAsync(shopIds, cancellationToken);
        List<LoyaltySettings> settings;
        List<LoyaltyAccount> accounts = [];
        List<LoyaltyRedemption> held = [];
        List<LoyaltyEntry> entries = [];
        List<LoyaltyRedemption> consumed = [];
        using (scope.BeginMany(shopIds))
        {
            settings = await db.Set<LoyaltySettings>().AsNoTracking().Where(s => shopIds.Contains(s.ShopId)).ToListAsync(cancellationToken);
            if (memberKey is not null)
            {
                accounts = await db.Set<LoyaltyAccount>().AsNoTracking().Where(a => shopIds.Contains(a.ShopId) && a.MemberKey == memberKey).ToListAsync(cancellationToken);
                held = await db.Set<LoyaltyRedemption>().AsNoTracking()
                    .Where(r => shopIds.Contains(r.ShopId) && r.MemberKey == memberKey && r.Status == LoyaltyRedemptionStatus.Held)
                    .ToListAsync(cancellationToken);
                var withCards = accounts.Select(a => a.ShopId).ToList();
                entries = withCards.Count == 0
                    ? []
                    : await db.Set<LoyaltyEntry>().AsNoTracking()
                        .Where(e => withCards.Contains(e.ShopId) && e.MemberKey == memberKey)
                        .OrderByDescending(e => e.At).Take(HistoryLength * withCards.Count)
                        .ToListAsync(cancellationToken);

                // The rewards those history lines used, for their titles (D-152).
                var usedOn = entries.Where(e => e.Kind == LoyaltyEntryKind.Redeemed && e.BookingId is not null).Select(e => e.BookingId!.Value).ToList();
                consumed = usedOn.Count == 0
                    ? []
                    : await db.Set<LoyaltyRedemption>().AsNoTracking()
                        .Where(r => usedOn.Contains(r.BookingId) && r.MemberKey == memberKey)
                        .ToListAsync(cancellationToken);
            }
        }

        var cards = new List<MyShopLoyaltyResponse>();
        foreach (var shopId in shopIds)
        {
            if (summaries.GetValueOrDefault(shopId) is not { } shop)
            {
                continue;
            }

            var rules = settings.FirstOrDefault(s => s.ShopId == shopId);
            var account = accounts.FirstOrDefault(a => a.ShopId == shopId);
            var heldHere = held.Where(r => r.ShopId == shopId).ToList();
            var couponsHere = coupons.Where(c => c.ShopId == shopId).ToList();
            var usedHere = usedCoupons.Where(c => c.ShopId == shopId).ToList();
            if (onlyWithData && account is null && heldHere.Count == 0 && couponsHere.Count == 0 && usedHere.Count == 0)
            {
                continue;
            }

            var history = entries.Where(e => e.ShopId == shopId)
                .Select(e => LoyaltyMapping.Entry(e, e.Kind == LoyaltyEntryKind.Redeemed ? consumed.FirstOrDefault(r => r.BookingId == e.BookingId) : null))
                .Concat(usedHere.Select(LoyaltyMapping.CouponUsed))
                .OrderByDescending(h => h.At).Take(HistoryLength);

            var points = heldHere.Sum(r => r.Points);
            var card = LoyaltyBalance.Card(LoyaltyBalance.Available(account, rules, points, now), rules?.PointsForReward ?? 0);
            var running = active.GetValueOrDefault(shopId) && rules is not null;
            cards.Add(new MyShopLoyaltyResponse(
                LoyaltyMapping.Shop(shop),
                running,
                rules is null ? null : LoyaltyMapping.Rules(rules),
                card.Points,
                card.RewardsReady,
                card.CurrentCard,
                card.PointsToNext,
                heldHere.Count,
                [.. history],
                [.. couponsHere.OrderBy(r => r.Status).ThenBy(r => r.ExpiresAt ?? DateTimeOffset.MaxValue).Select(LoyaltyMapping.Reward)]));
        }

        return cards;
    }

    /// <summary>D-132 coupons the customer can still use, and those held for an upcoming booking (their own rows).</summary>
    private async Task<List<LoyaltyReward>> CouponsAsync(Guid customerId, ShopId? shopId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var rewards = db.Set<LoyaltyReward>().AsNoTracking()
            .Where(r => r.CustomerId == customerId
                        && (r.Status == LoyaltyRewardStatus.Held || (r.Status == LoyaltyRewardStatus.Available && (r.ExpiresAt == null || r.ExpiresAt > now))));
        if (shopId is { } only)
        {
            rewards = rewards.Where(r => r.ShopId == only);
        }

        return await rewards.ToListAsync(cancellationToken);
    }

    /// <summary>D-132 coupons the customer used at a completed visit (their own rows), newest first, for the history (D-152).</summary>
    private async Task<List<LoyaltyReward>> UsedCouponsAsync(Guid customerId, ShopId? shopId, CancellationToken cancellationToken)
    {
        var rewards = db.Set<LoyaltyReward>().AsNoTracking().Where(r => r.CustomerId == customerId && r.Status == LoyaltyRewardStatus.Redeemed);
        if (shopId is { } only)
        {
            rewards = rewards.Where(r => r.ShopId == only);
        }

        return await rewards.OrderByDescending(r => r.RedeemedAt).Take(HistoryLength * 10).ToListAsync(cancellationToken);
    }
}

internal sealed class ListMyLoyaltyHandler(CustomerLoyaltyReader reader) : IQueryHandler<ListMyLoyaltyQuery, MyLoyaltyResponse>
{
    public Task<MyLoyaltyResponse> Handle(ListMyLoyaltyQuery query, CancellationToken cancellationToken) => reader.ListAsync(cancellationToken);
}

internal sealed class GetMyShopLoyaltyHandler(CustomerLoyaltyReader reader) : IQueryHandler<GetMyShopLoyaltyQuery, MyShopLoyaltyResponse?>
{
    public Task<MyShopLoyaltyResponse?> Handle(GetMyShopLoyaltyQuery query, CancellationToken cancellationToken) =>
        reader.ForShopAsync(query.ShopSlug, cancellationToken);
}
