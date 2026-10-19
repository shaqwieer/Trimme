using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Application.Customer;

internal sealed record ListMyLoyaltyQuery : IQuery<MyLoyaltyResponse>;

internal sealed record GetMyShopLoyaltyQuery(string ShopSlug) : IQuery<MyShopLoyaltyResponse?>;

/// <summary>
/// The signed-in customer's own stamps and rewards (D-132). They are read through the customer's data scope; the shops'
/// programmes are published data, read in a public scope of exactly those shops.
/// </summary>
internal sealed class CustomerLoyaltyReader(TrimmeDbContext db, ICurrentCustomer customer, IPublicDataScope scope, IShopDirectory shops, TimeProvider clock)
{
    public async Task<MyLoyaltyResponse> ListAsync(CancellationToken cancellationToken)
    {
        if (customer.CustomerId is not { } customerId)
        {
            return new MyLoyaltyResponse([]);
        }

        var stamps = await OpenStampsAsync(customerId, null, cancellationToken);
        var rewards = await RewardsAsync(customerId, null, cancellationToken);
        var shopIds = stamps.Keys.Concat(rewards.Select(r => r.ShopId)).Distinct().Take(IPublicDataScope.MaxShops).ToList();
        if (shopIds.Count == 0)
        {
            return new MyLoyaltyResponse([]);
        }

        var programs = await ProgramsAsync(shopIds, cancellationToken);
        var summaries = await shops.FindManyAsync(shopIds, cancellationToken);
        var items = shopIds
            .Where(summaries.ContainsKey)
            .Select(id => Card(summaries[id], programs.GetValueOrDefault(id), stamps.GetValueOrDefault(id), rewards.Where(r => r.ShopId == id)))
            .OrderByDescending(c => c.Rewards.Count)
            .ThenByDescending(c => c.Stamps)
            .ToList();
        return new MyLoyaltyResponse(items);
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

        var programs = await ProgramsAsync([shop.Id], cancellationToken);
        var stamps = await OpenStampsAsync(customerId, shop.Id, cancellationToken);
        var rewards = await RewardsAsync(customerId, shop.Id, cancellationToken);
        return Card(shop, programs.GetValueOrDefault(shop.Id), stamps.GetValueOrDefault(shop.Id), rewards);
    }

    private static MyShopLoyaltyResponse Card(ShopSummary shop, LoyaltyProgram? program, int stamps, IEnumerable<LoyaltyReward> rewards) =>
        new(LoyaltyMapping.Shop(shop), program is null ? null : LoyaltyMapping.Program(program), stamps,
            [.. rewards.OrderBy(r => r.Status).ThenBy(r => r.ExpiresAt ?? DateTimeOffset.MaxValue).Select(LoyaltyMapping.Reward)]);

    private async Task<Dictionary<ShopId, int>> OpenStampsAsync(Guid customerId, ShopId? shopId, CancellationToken cancellationToken)
    {
        var stamps = db.Set<LoyaltyStamp>().AsNoTracking().Where(s => s.CustomerId == customerId && s.RewardId == null);
        if (shopId is { } only)
        {
            stamps = stamps.Where(s => s.ShopId == only);
        }

        return await stamps.GroupBy(s => s.ShopId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
    }

    /// <summary>Rewards the customer can still use, and those held for an upcoming booking.</summary>
    private async Task<List<LoyaltyReward>> RewardsAsync(Guid customerId, ShopId? shopId, CancellationToken cancellationToken)
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

    private async Task<Dictionary<ShopId, LoyaltyProgram>> ProgramsAsync(IReadOnlyCollection<ShopId> shopIds, CancellationToken cancellationToken)
    {
        using var _ = scope.BeginMany(shopIds);
        return await db.Set<LoyaltyProgram>().AsNoTracking()
            .Where(p => shopIds.Contains(p.ShopId) && p.IsActive)
            .ToDictionaryAsync(p => p.ShopId, cancellationToken);
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
