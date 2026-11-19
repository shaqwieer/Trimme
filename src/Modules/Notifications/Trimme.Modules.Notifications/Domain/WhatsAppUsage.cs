using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Notifications.Domain;

/// <summary>
/// Marketing messages (D-155: the win-back message only) of one shop in one window, a month from the day its
/// subscription period started: the plan's limit is checked against <see cref="Used"/> before each one goes to the
/// provider. Written only by atomic SQL in the dispatch job (an increment that cannot pass the limit, so two sends at the
/// last slot never both pass), read by the owner's and the admin's subscription page. <see cref="OwnerToldAt"/> marks the
/// window's first refusal, when the owner was told. (D-149 counted every customer message per calendar month here; the
/// D-155 migration cleared those counts.)
/// </summary>
public sealed class WhatsAppUsageMonth : IShopOwned
{
    private WhatsAppUsageMonth()
    {
    }

    public ShopId ShopId { get; private set; }

    /// <summary>The first day of the window.</summary>
    public DateOnly Month { get; private set; }

    public int Used { get; private set; }

    public DateTimeOffset? OwnerToldAt { get; private set; }
}
