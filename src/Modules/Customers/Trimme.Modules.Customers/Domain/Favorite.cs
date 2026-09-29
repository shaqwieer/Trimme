using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Customers.Domain;

public readonly record struct FavoriteId(Guid Value) : IEntityId<FavoriteId>
{
    public static FavoriteId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// A shop or a professional the customer saved (R-CUS-10, D-098). Customer-owned: only its customer can read, add or
/// remove it (D-085). <see cref="ShopId"/> is the saved shop, or the professional's own shop; <see cref="ProfessionalId"/>
/// is set for a professional. The row stays when the shop or professional leaves discovery; the list simply skips it.
/// </summary>
public sealed class Favorite : Entity<FavoriteId>, ICustomerOwned
{
    /// <summary>At most this many saved shops, and as many saved professionals, per customer.</summary>
    public const int MaxPerKind = 200;

    private Favorite(FavoriteId id, Guid customerId, ShopId shopId, ProfessionalId? professionalId, DateTimeOffset now)
        : base(id)
    {
        CustomerId = customerId;
        ShopId = shopId;
        ProfessionalId = professionalId;
        CreatedAt = now;
    }

    private Favorite()
    {
    }

    public ShopId ShopId { get; private set; }

    public Guid? CustomerId { get; private set; }

    public ProfessionalId? ProfessionalId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Favorite ForShop(FavoriteId id, Guid customerId, ShopId shopId, DateTimeOffset now) =>
        new(id, customerId, shopId, null, now);

    public static Favorite ForProfessional(FavoriteId id, Guid customerId, ShopId shopId, ProfessionalId professionalId, DateTimeOffset now) =>
        new(id, customerId, shopId, professionalId, now);
}

public static class FavoriteErrors
{
    public static Error ShopNotFound() => Error.NotFound("shop.not_found", "The shop was not found.");

    public static Error ProfessionalNotFound() => Error.NotFound("professional.not_found", "The professional was not found.");

    public static Error LimitReached() =>
        Error.BusinessRule("favorites.limit_reached", $"At most {Favorite.MaxPerKind} favorites of each kind can be saved.");
}
