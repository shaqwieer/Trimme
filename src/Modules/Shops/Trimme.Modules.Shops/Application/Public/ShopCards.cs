using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Shops.Application.Public;

/// <summary>
/// <see cref="IShopCards"/>: the discovery pipeline restricted to the given shops (D-098), so a favorite shows exactly what
/// the search card shows, and a shop that left discovery (suspended, paused off the map, nothing to book) drops out.
/// </summary>
internal sealed class ShopCards(DiscoveryCatalog catalog) : IShopCards
{
    public async Task<IReadOnlyList<ShopCard>> GetAsync(IReadOnlyCollection<ShopId> shopIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shopIds);
        if (shopIds.Count == 0)
        {
            return [];
        }

        // Discovery reads at most MaxCandidates shops at a time, so a long favorites list is read in chunks.
        var wanted = shopIds.Distinct().ToList();
        var byId = new Dictionary<ShopId, DiscoveryShop>();
        foreach (var chunk in wanted.Chunk(DiscoveryCatalog.MaxCandidates))
        {
            var candidates = await catalog.CandidatesAsync(new DiscoveryArea(null, null, 0, null), cancellationToken, chunk);
            var (scope, listed) = await catalog.ListAsync(candidates, OfferMatch.None, withOpening: true, cancellationToken);
            using (scope)
            {
                foreach (var shop in listed)
                {
                    byId[shop.Shop.Id] = shop;
                }
            }
        }

        return
        [
            .. wanted.Where(byId.ContainsKey).Select(id => byId[id]).Select(s => new ShopCard(
                s.Shop.Id,
                s.Shop.Slug,
                s.Shop.NameAr,
                s.Shop.NameEn,
                s.Shop.IsVerified,
                s.Shop.District,
                s.Rating.Average,
                s.Rating.Count,
                s.Offers.MinPrice,
                s.Offers.Currency,
                s.Opening?.IsOpenNow ?? false,
                s.Opening?.ClosesAt,
                s.Opening?.NextOpensAt,
                s.Shop.TimeZone,
                DiscoveryCatalog.MediaUrl(s.Shop.CoverMediaId),
                DiscoveryCatalog.MediaUrl(s.Shop.LogoMediaId))),
        ];
    }
}
