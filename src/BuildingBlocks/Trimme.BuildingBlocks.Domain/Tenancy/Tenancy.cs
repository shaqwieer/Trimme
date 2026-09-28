using Trimme.BuildingBlocks.Domain.Primitives;

namespace Trimme.BuildingBlocks.Domain.Tenancy;

/// <summary>Identifier of a shop, the tenant of every shop-owned row (spec §7).</summary>
public readonly record struct ShopId(Guid Value) : IEntityId<ShopId>
{
    public static ShopId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Shop lifecycle (spec §14): created as Draft by an admin, then Active; an admin can suspend it. Users of a suspended
/// shop can still sign in and see the status, but have no tenant (their shop's data is not reachable) until reactivated.
/// </summary>
public enum ShopStatus
{
    Draft,
    Active,
    Suspended,
}

/// <summary>
/// A row that belongs to exactly one shop. The persistence layer applies the tenant query filter, stamps
/// <see cref="ShopId"/> on insert from the signed-in shop user, rejects any change to it, and adds a foreign key to the
/// tenant root (R-TEN-02/03/04). Implementations expose <c>ShopId { get; private set; }</c>.
/// </summary>
public interface IShopOwned
{
    ShopId ShopId { get; }
}

/// <summary>
/// A shop-owned row that also belongs to one customer, such as a booking (D-085). Besides the shop's own users, the
/// customer it belongs to may read it and change it; nobody else. <see cref="CustomerId"/> is null for rows that have no
/// customer account (walk-ins) and never changes.
/// </summary>
public interface ICustomerOwned : IShopOwned
{
    Guid? CustomerId { get; }
}

/// <summary>The tenant root entity (the shop itself). Exactly one entity type in the model implements it.</summary>
public interface ITenantRoot
{
    ShopId Id { get; }
}

/// <summary>
/// An account that may belong to one shop (shop users). The link is set once, when the account is created, and never
/// changes (spec §7: tenant from claims; no moving between shops). A foreign key to the tenant root is added by convention.
/// </summary>
public interface ITenantMember
{
    ShopId? ShopId { get; }
}

/// <summary>Raised when a write would cross or change a tenant boundary. Always a bug or an attack, never user error.</summary>
public sealed class TenantViolationException : InvalidOperationException
{
    public TenantViolationException(string message)
        : base(message)
    {
    }

    public TenantViolationException()
        : base("The operation violates tenant isolation.")
    {
    }

    public TenantViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
