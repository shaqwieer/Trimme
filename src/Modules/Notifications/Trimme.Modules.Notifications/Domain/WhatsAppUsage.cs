using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Notifications.Domain;

/// <summary>
/// WhatsApp messages to one shop's customers in one calendar month of the shop's time zone (D-149): the plan's allowance
/// is checked against <see cref="Used"/> before each message goes to the provider. Written only by atomic SQL in the
/// dispatch job (an increment that cannot pass the allowance, so two sends at the last slot never both pass), read by the
/// owner's and the admin's subscription page. <see cref="OwnerToldAt"/> marks the month's first refusal, when the owner
/// was told.
/// </summary>
public sealed class WhatsAppUsageMonth : IShopOwned
{
    private WhatsAppUsageMonth()
    {
    }

    public ShopId ShopId { get; private set; }

    /// <summary>The first day of the month.</summary>
    public DateOnly Month { get; private set; }

    public int Used { get; private set; }

    public DateTimeOffset? OwnerToldAt { get; private set; }
}
