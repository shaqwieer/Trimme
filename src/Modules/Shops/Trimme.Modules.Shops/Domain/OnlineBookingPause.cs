using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Shops.Domain;

/// <summary>
/// A shop's online-booking pause (D-013, D-083): the row exists while the shop is paused. It is kept apart from the
/// shop row so pausing never changes the profile's concurrency version (an owner who pauses in one tab can still save
/// the profile in another). It is read for any shop by the bookability gate and discovery, so it is not tenant-filtered;
/// only the shop's own pause and resume commands (tenant from claims) and the admin's write it.
/// <para>
/// D-155 (the client's 1-1): the owner or the admin pauses, with a reason customers see and an optional return time; at
/// that time booking opens again by itself (the pause reads as over at once, and a job removes the row). A paused shop
/// stays listed; its confirmed bookings stay.
/// </para>
/// </summary>
public sealed class OnlineBookingPause : IPublicContent
{
    public const int MaxReasonLength = 300;

    /// <summary>The furthest return time a pause may set.</summary>
    public const int MaxDays = 90;

    public OnlineBookingPause(ShopId shopId, DateTimeOffset pausedAt, string? reason, DateTimeOffset? resumesAt = null, bool byAdmin = false)
    {
        ShopId = shopId;
        PausedAt = pausedAt;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ResumesAt = resumesAt;
        ByAdmin = byAdmin;
    }

    private OnlineBookingPause()
    {
    }

    public ShopId ShopId { get; private set; }

    public DateTimeOffset PausedAt { get; private set; }

    /// <summary>The shop's reason (optional), shown to customers on the shop's page (D-155) and in the audit trail.</summary>
    public string? Reason { get; private set; }

    /// <summary>When online booking opens again by itself (D-155); <see langword="null"/> until someone resumes it.</summary>
    public DateTimeOffset? ResumesAt { get; private set; }

    /// <summary>Paused by the platform's admin rather than the shop's owner (D-155).</summary>
    public bool ByAdmin { get; private set; }

    /// <summary>Still in force: no return time, or one still ahead.</summary>
    public bool InForce(DateTimeOffset now) => ResumesAt is not { } until || until > now;
}
