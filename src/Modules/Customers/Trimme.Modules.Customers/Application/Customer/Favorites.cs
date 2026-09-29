using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Customers.Domain;

namespace Trimme.Modules.Customers.Application.Customer;

/// <summary>A saved shop, as its discovery card shows it.</summary>
public sealed record FavoriteShopResponse(
    Guid Id,
    string Slug,
    string NameAr,
    string NameEn,
    bool IsVerified,
    string? District,
    decimal Rating,
    int ReviewCount,
    decimal? MinPrice,
    string? Currency,
    bool IsOpenNow,
    DateTimeOffset? ClosesAt,
    DateTimeOffset? NextOpensAt,
    string TimeZone,
    string? CoverUrl,
    string? LogoUrl,
    DateTimeOffset SavedAt);

/// <summary>A saved professional with their shop; never a phone or WhatsApp number (R-PRO-02).</summary>
public sealed record FavoriteProfessionalResponse(
    Guid Id,
    string Slug,
    string NameAr,
    string NameEn,
    string? SpecialtyAr,
    string? SpecialtyEn,
    string? AvatarUrl,
    Guid ShopId,
    string ShopSlug,
    string ShopNameAr,
    string ShopNameEn,
    decimal Rating,
    int ReviewCount,
    DateTimeOffset SavedAt);

/// <summary>
/// The customer's favorites, most recently saved first. Only shops and professionals discovery would show are listed;
/// <c>ShopIds</c> and <c>ProfessionalIds</c> are everything saved (to fill the heart buttons).
/// </summary>
public sealed record FavoritesResponse(
    IReadOnlyList<FavoriteShopResponse> Shops,
    IReadOnlyList<FavoriteProfessionalResponse> Professionals,
    IReadOnlyList<Guid> ShopIds,
    IReadOnlyList<Guid> ProfessionalIds);

internal sealed record ListFavoritesQuery : IQuery<FavoritesResponse>;

internal sealed record SaveShopFavoriteCommand(Guid ShopId) : ICommand<Result>;

internal sealed record RemoveShopFavoriteCommand(Guid ShopId) : ICommand<Result>;

internal sealed record SaveProfessionalFavoriteCommand(Guid ProfessionalId, Guid ShopId) : ICommand<Result>;

internal sealed record RemoveProfessionalFavoriteCommand(Guid ProfessionalId) : ICommand<Result>;

/// <summary>
/// Reads a customer's favorites through the customer filter (D-085): another customer's rows are never visible, and the
/// data layer refuses to add or remove them. Cards come from the discovery read models (D-098).
/// </summary>
internal sealed class ListFavoritesHandler(
    TrimmeDbContext db,
    IShopCards shops,
    IPublicDataScope scope,
    IProfessionalDirectory professionals,
    IRatingReader ratings) : IQueryHandler<ListFavoritesQuery, FavoritesResponse>
{
    public async Task<FavoritesResponse> Handle(ListFavoritesQuery query, CancellationToken cancellationToken)
    {
        var saved = await db.Set<Favorite>().AsNoTracking()
            .OrderByDescending(f => f.CreatedAt).ThenBy(f => f.Id)
            .ToListAsync(cancellationToken);
        var savedShops = saved.Where(f => f.ProfessionalId is null).ToList();
        var savedProfessionals = saved.Where(f => f.ProfessionalId is not null).ToList();

        // One card read for every shop involved: saved shops and the shops of saved professionals.
        var cards = (await shops.GetAsync([.. saved.Select(f => f.ShopId).Distinct()], cancellationToken)).ToDictionary(c => c.Id);
        IReadOnlyList<FavoriteShopResponse> shopItems =
        [
            .. savedShops.Where(f => cards.ContainsKey(f.ShopId)).Select(f => ToShop(cards[f.ShopId], f.CreatedAt)),
        ];

        List<FavoriteProfessionalResponse> professionalItems = [];
        var listedShops = savedProfessionals.Select(f => f.ShopId).Distinct().Where(cards.ContainsKey).ToList();
        if (listedShops.Count > 0)
        {
            using (scope.BeginMany(listedShops))
            {
                var active = (await professionals.ListActiveProfilesAsync(listedShops, cancellationToken)).ToDictionary(p => p.Id);
                var rated = await ratings.GetAsync(RatingSubject.Professional, [.. active.Keys.Select(k => k.Value)], cancellationToken);
                foreach (var favorite in savedProfessionals)
                {
                    if (active.TryGetValue(favorite.ProfessionalId!.Value, out var card) && card.ShopId == favorite.ShopId)
                    {
                        var rating = rated.GetValueOrDefault(card.Id.Value) ?? RatingSummary.Empty;
                        var shop = cards[card.ShopId];
                        professionalItems.Add(new FavoriteProfessionalResponse(
                            card.Id.Value, card.Slug, card.NameAr, card.NameEn, card.SpecialtyAr, card.SpecialtyEn, card.AvatarUrl,
                            shop.Id.Value, shop.Slug, shop.NameAr, shop.NameEn, rating.Average, rating.Count, favorite.CreatedAt));
                    }
                }
            }
        }

        return new FavoritesResponse(
            shopItems,
            professionalItems,
            [.. savedShops.Select(f => f.ShopId.Value)],
            [.. savedProfessionals.Select(f => f.ProfessionalId!.Value.Value)]);
    }

    private static FavoriteShopResponse ToShop(ShopCard c, DateTimeOffset savedAt) =>
        new(c.Id.Value, c.Slug, c.NameAr, c.NameEn, c.IsVerified, c.District, c.Rating, c.ReviewCount, c.MinPrice, c.Currency,
            c.IsOpenNow, c.ClosesAt, c.NextOpensAt, c.TimeZone, c.CoverUrl, c.LogoUrl, savedAt);
}

/// <summary>
/// Saves a shop discovery lists (404 otherwise). Saving again is a no-op, also when two taps race (the unique index
/// decides); at most <see cref="Favorite.MaxPerKind"/> shops.
/// </summary>
internal sealed class SaveShopFavoriteHandler(TrimmeDbContext db, ICurrentCustomer customer, IShopCards shops, TimeProvider clock)
    : ICommandHandler<SaveShopFavoriteCommand, Result>
{
    public async Task<Result> Handle(SaveShopFavoriteCommand command, CancellationToken cancellationToken)
    {
        var shopId = new ShopId(command.ShopId);
        if (customer.CustomerId is not { } customerId || (await shops.GetAsync([shopId], cancellationToken)).Count == 0)
        {
            return FavoriteErrors.ShopNotFound();
        }

        var mine = db.Set<Favorite>().Where(f => f.ProfessionalId == null);
        if (await mine.AnyAsync(f => f.ShopId == shopId, cancellationToken))
        {
            return Result.Success();
        }

        if (await mine.CountAsync(cancellationToken) >= Favorite.MaxPerKind)
        {
            return FavoriteErrors.LimitReached();
        }

        return await FavoriteWrites.AddAsync(db, Favorite.ForShop(new FavoriteId(Guid.CreateVersion7()), customerId, shopId, clock.GetUtcNow()), cancellationToken);
    }
}

/// <summary>Saves an active professional of a shop discovery lists (404 otherwise, also when the shop is not theirs).</summary>
internal sealed class SaveProfessionalFavoriteHandler(
    TrimmeDbContext db,
    ICurrentCustomer customer,
    IShopCards shops,
    IPublicDataScope scope,
    IProfessionalDirectory professionals,
    TimeProvider clock) : ICommandHandler<SaveProfessionalFavoriteCommand, Result>
{
    public async Task<Result> Handle(SaveProfessionalFavoriteCommand command, CancellationToken cancellationToken)
    {
        var shopId = new ShopId(command.ShopId);
        var professionalId = new ProfessionalId(command.ProfessionalId);
        if (customer.CustomerId is not { } customerId || (await shops.GetAsync([shopId], cancellationToken)).Count == 0)
        {
            return FavoriteErrors.ProfessionalNotFound();
        }

        using (scope.Begin(shopId))
        {
            var active = await professionals.ListActiveProfilesAsync([shopId], cancellationToken);
            if (!active.Any(p => p.Id == professionalId && p.ShopId == shopId))
            {
                return FavoriteErrors.ProfessionalNotFound();
            }
        }

        var mine = db.Set<Favorite>().Where(f => f.ProfessionalId != null);
        if (await mine.AnyAsync(f => f.ProfessionalId == professionalId, cancellationToken))
        {
            return Result.Success();
        }

        if (await mine.CountAsync(cancellationToken) >= Favorite.MaxPerKind)
        {
            return FavoriteErrors.LimitReached();
        }

        return await FavoriteWrites.AddAsync(
            db, Favorite.ForProfessional(new FavoriteId(Guid.CreateVersion7()), customerId, shopId, professionalId, clock.GetUtcNow()), cancellationToken);
    }
}

/// <summary>Removes a saved shop; removing one that is not saved is a no-op (only the customer's own rows are visible).</summary>
internal sealed class RemoveShopFavoriteHandler(TrimmeDbContext db) : ICommandHandler<RemoveShopFavoriteCommand, Result>
{
    public async Task<Result> Handle(RemoveShopFavoriteCommand command, CancellationToken cancellationToken)
    {
        var shopId = new ShopId(command.ShopId);
        return await FavoriteWrites.RemoveAsync(
            db, await db.Set<Favorite>().Where(f => f.ProfessionalId == null && f.ShopId == shopId).ToListAsync(cancellationToken), cancellationToken);
    }
}

internal sealed class RemoveProfessionalFavoriteHandler(TrimmeDbContext db) : ICommandHandler<RemoveProfessionalFavoriteCommand, Result>
{
    public async Task<Result> Handle(RemoveProfessionalFavoriteCommand command, CancellationToken cancellationToken)
    {
        var professionalId = new ProfessionalId(command.ProfessionalId);
        return await FavoriteWrites.RemoveAsync(
            db, await db.Set<Favorite>().Where(f => f.ProfessionalId == professionalId).ToListAsync(cancellationToken), cancellationToken);
    }
}

internal static class FavoriteWrites
{
    public static async Task<Result> AddAsync(TrimmeDbContext db, Favorite favorite, CancellationToken cancellationToken)
    {
        db.Add(favorite);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DatabaseErrors.IsUniqueViolation(exception))
        {
            // Two taps raced: the other one saved it.
        }

        return Result.Success();
    }

    public static async Task<Result> RemoveAsync(TrimmeDbContext db, List<Favorite> rows, CancellationToken cancellationToken)
    {
        if (rows.Count > 0)
        {
            db.RemoveRange(rows);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Two taps raced: the other one removed it.
            }
        }

        return Result.Success();
    }
}
