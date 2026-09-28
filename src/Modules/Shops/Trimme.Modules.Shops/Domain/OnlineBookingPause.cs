using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Shops.Domain;

/// <summary>
/// A shop's online-booking pause (D-013, D-083): the row exists while the shop is paused. It is kept apart from the
/// shop row so pausing never changes the profile's concurrency version (an owner who pauses in one tab can still save
/// the profile in another). It is read for any shop by the bookability gate and discovery, so it is not tenant-filtered;
/// only the shop's own pause and resume commands (tenant from claims) write it.
/// </summary>
public sealed class OnlineBookingPause
{
    public const int MaxReasonLength = 300;

    public OnlineBookingPause(ShopId shopId, DateTimeOffset pausedAt, string? reason)
    {
        ShopId = shopId;
        PausedAt = pausedAt;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    private OnlineBookingPause()
    {
    }

    public ShopId ShopId { get; private set; }

    public DateTimeOffset PausedAt { get; private set; }

    /// <summary>The shop's own note (optional, shown to the shop and in the audit trail).</summary>
    public string? Reason { get; private set; }
}
