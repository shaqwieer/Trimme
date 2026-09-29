using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Domain.Text;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Services.Domain;

namespace Trimme.Modules.Services.Infrastructure;

/// <summary>
/// <see cref="IShopOfferReader"/> for discovery (D-091). Reads through the caller's data scope (the public multi-shop
/// scope, D-090) with a fixed number of queries whatever the number of shops. Only published items count: active, not
/// archived, not hidden, and for a package every item service published (D-072).
/// </summary>
internal sealed class ShopOfferReader(TrimmeDbContext db) : IShopOfferReader
{
    public async Task<IReadOnlyDictionary<ShopId, ShopOfferSummary>> SummarizeAsync(
        IReadOnlyCollection<ShopId> shopIds, OfferMatch match, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);
        var published = await LoadAsync(shopIds, cancellationToken);
        var categoryText = string.IsNullOrEmpty(match.NormalizedQuery)
            ? new Dictionary<ServiceCategoryId, string>()
            : await db.Set<ServiceCategory>().AsNoTracking().Where(c => c.IsActive)
                .ToDictionaryAsync(c => c.Id, c => SearchText.Normalize($"{c.NameAr} {c.NameEn}"), cancellationToken);

        var result = new Dictionary<ShopId, ShopOfferSummary>();
        foreach (var shop in published.Offers.GroupBy(o => o.ShopId))
        {
            var offers = shop.ToList();
            var byCategory = offers.Where(o => o.CategoryId is not null)
                .GroupBy(o => o.CategoryId!.Value.Value)
                .ToDictionary(g => g.Key, g => g.Min(o => o.Summary.Price));

            var matched = match.IsEmpty
                ? null
                : offers.Where(o => Matches(o, match, categoryText)).OrderBy(o => o.Summary.Price).ThenBy(o => o.Summary.DurationMinutes).FirstOrDefault();

            result[shop.Key] = new ShopOfferSummary(
                offers.Min(o => o.Summary.Price),
                offers[0].Summary.Currency,
                byCategory,
                matched?.Summary,
                Probes(offers, matched, published));
        }

        return result;
    }

    public async Task<IReadOnlyList<OfferSummary>> ListForProfessionalAsync(ShopId shopId, ProfessionalId professionalId, CancellationToken cancellationToken)
    {
        var published = await LoadAsync([shopId], cancellationToken);
        return [.. published.Offers.Where(o => published.Eligible(o).Contains(professionalId)).Select(o => o.Summary)];
    }

    public async Task<IReadOnlyList<ProbeOffer>> ListProbeOffersAsync(ShopId shopId, CancellationToken cancellationToken)
    {
        var published = await LoadAsync([shopId], cancellationToken);
        return
        [
            .. published.Offers
                .Where(o => o.Summary.OnlineBookable)
                .Select(o => new ProbeOffer(o.Summary, published.Eligible(o)))
                .Where(p => p.ProfessionalIds.Count > 0),
        ];
    }

    /// <summary>
    /// The matched offer when it can be booked online; otherwise each professional's shortest online-bookable offer, so a
    /// shop whose shortest service belongs to an absent professional still shows the others' earliest time (D-091).
    /// </summary>
    private static IReadOnlyList<ProbeOffer> Probes(List<PublishedOffer> offers, PublishedOffer? matched, PublishedCatalog published)
    {
        if (matched is { Summary.OnlineBookable: true } && published.Eligible(matched) is { Count: > 0 } matchedProfessionals)
        {
            return [new ProbeOffer(matched.Summary, matchedProfessionals)];
        }

        var bookable = offers
            .Where(o => o.Summary.OnlineBookable)
            .Select(o => (Offer: o, Professionals: published.Eligible(o)))
            .Where(x => x.Professionals.Count > 0)
            .OrderBy(x => x.Offer.Summary.DurationMinutes).ThenBy(x => x.Offer.Summary.Price).ThenBy(x => x.Offer.Summary.Id)
            .ToList();
        return
        [
            .. bookable.SelectMany(x => x.Professionals)
                .Distinct()
                .Select(professional => (Professional: professional, Offer: bookable.First(x => x.Professionals.Contains(professional)).Offer))
                .GroupBy(x => x.Offer.Summary.Id)
                .Select(g => new ProbeOffer(g.First().Offer.Summary, [.. g.Select(x => x.Professional)])),
        ];
    }

    private static bool Matches(PublishedOffer offer, OfferMatch match, Dictionary<ServiceCategoryId, string> categoryText)
    {
        if (match.CategoryId is { } category && offer.CategoryId?.Value != category)
        {
            return false;
        }

        if (string.IsNullOrEmpty(match.NormalizedQuery))
        {
            return true;
        }

        var text = offer.SearchText;
        if (offer.CategoryId is { } own && categoryText.TryGetValue(own, out var categoryName))
        {
            text = $"{text} {categoryName}";
        }

        return SearchText.Matches(text, match.NormalizedQuery);
    }

    /// <summary>Every published service and package of the shops, in each shop's display order (services first).</summary>
    private async Task<PublishedCatalog> LoadAsync(IReadOnlyCollection<ShopId> shopIds, CancellationToken cancellationToken)
    {
        var ids = shopIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new PublishedCatalog([], []);
        }

        var services = await db.Set<ShopService>().AsNoTracking()
            .Where(s => ids.Contains(s.ShopId) && s.IsActive && !s.IsArchived && s.Moderation == ModerationState.Visible)
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);
        var packages = await db.Set<ServicePackage>().AsNoTracking().Include(p => p.Items)
            .Where(p => ids.Contains(p.ShopId) && p.IsActive && !p.IsArchived && p.Moderation == ModerationState.Visible)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);
        var assignments = await db.Set<ProfessionalServiceAssignment>().AsNoTracking()
            .Where(a => ids.Contains(a.ShopId))
            .ToListAsync(cancellationToken);

        var publishedServices = services.ToDictionary(s => s.Id);
        var professionalsByService = assignments.GroupBy(a => a.ServiceId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.ProfessionalId).ToHashSet());

        var offers = new List<PublishedOffer>();
        foreach (var service in services)
        {
            offers.Add(new PublishedOffer(
                service.ShopId,
                service.CategoryId,
                new OfferSummary(
                    service.Id.Value, IsPackage: false, service.NameAr, service.NameEn, service.Price, service.Currency,
                    service.DurationMinutes, service.OnlineBookable),
                SearchText.Normalize($"{service.NameAr} {service.NameEn}"),
                [service.Id]));
        }

        foreach (var package in packages)
        {
            var items = package.ExpandItems();
            if (!items.All(publishedServices.ContainsKey))
            {
                continue;
            }

            offers.Add(new PublishedOffer(
                package.ShopId,
                CategoryId: null,
                new OfferSummary(
                    package.Id.Value, IsPackage: true, package.NameAr, package.NameEn, package.Price, package.Currency,
                    package.DurationMinutes, items.All(item => publishedServices[item].OnlineBookable)),
                SearchText.Normalize($"{package.NameAr} {package.NameEn}"),
                items));
        }

        return new PublishedCatalog(offers, professionalsByService);
    }

    private sealed record PublishedOffer(
        ShopId ShopId, ServiceCategoryId? CategoryId, OfferSummary Summary, string SearchText, IReadOnlyList<ShopServiceId> Services);

    private sealed class PublishedCatalog(List<PublishedOffer> offers, Dictionary<ShopServiceId, HashSet<ProfessionalId>> professionalsByService)
    {
        public List<PublishedOffer> Offers { get; } = offers;

        /// <summary>Professionals assigned to every service of the offer (one professional does a whole package).</summary>
        public IReadOnlyList<ProfessionalId> Eligible(PublishedOffer offer)
        {
            HashSet<ProfessionalId>? eligible = null;
            foreach (var service in offer.Services)
            {
                if (!professionalsByService.TryGetValue(service, out var assigned))
                {
                    return [];
                }

                eligible = eligible is null ? [.. assigned] : [.. eligible.Intersect(assigned)];
            }

            return eligible is null ? [] : [.. eligible.OrderBy(p => p.Value)];
        }
    }
}
