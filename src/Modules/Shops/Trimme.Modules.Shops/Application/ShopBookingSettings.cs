using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Application;

// Booking settings per shop (D-149, the client's «إعدادات الحجز تكون خاصة بكل محل»): instead of one set for every shop
// from the admin, each shop sets the start-time interval (10:00, 10:15,
// 10:30…), the minimum notice, the furthest bookable day and the cleaning time after each booking. The start interval is
// not a service's length: starts every 15 minutes, a service of 45. The platform's values are the defaults of a shop that
// has not set its own; the cancellation window, the review window and the reminder stay platform-wide. The owner edits
// them (Shop.Profile.Edit: barbers cannot), and so does the admin on the shop's page; both are audited. Every online
// booking is confirmed at once (D-155): there is no confirmation setting any more.

/// <summary>The shop's booking settings in force (its own, or the platform's where it has none).</summary>
public sealed record ShopBookingSettingsResponse(
    int SlotStepMinutes, int MinLeadTimeMinutes, int BookingHorizonDays, int BufferMinutes);

public sealed record ShopBookingSettingsRequest(
    int SlotStepMinutes, int MinLeadTimeMinutes, int BookingHorizonDays, int BufferMinutes);

internal sealed record GetOwnBookingSettingsQuery : IQuery<ShopBookingSettingsResponse?>;

internal sealed record UpdateOwnBookingSettingsCommand(ShopBookingSettingsRequest Settings) : ICommand<Result<ShopBookingSettingsResponse>>;

internal sealed record GetShopBookingSettingsQuery(Guid ShopId) : IQuery<ShopBookingSettingsResponse?>;

internal sealed record UpdateShopBookingSettingsCommand(Guid ShopId, ShopBookingSettingsRequest Settings) : ICommand<Result<ShopBookingSettingsResponse>>;

internal static class ShopBookingSettingsMapping
{
    public static ShopBookingOverrides Overrides(Shop shop) =>
        new(shop.SlotStepMinutes, shop.MinLeadTimeMinutes, shop.BookingHorizonDays, shop.BufferMinutes);

    public static ShopBookingSettingsResponse ToResponse(Shop shop, PlatformSettingsSnapshot platform)
    {
        var rules = EffectiveBookingRules.Of(platform, Overrides(shop));
        return new(rules.SlotStepMinutes, rules.MinLeadTimeMinutes, rules.BookingHorizonDays, rules.BufferMinutes);
    }

    public static string Describe(Shop shop) =>
        $"Starts every {shop.SlotStepMinutes} min; "
        + $"notice {shop.MinLeadTimeMinutes} min; up to {shop.BookingHorizonDays} days ahead; cleaning {shop.BufferMinutes} min";
}

/// <summary>Reads and writes one shop's booking settings, through the caller's scope (the tenant, or an admin's).</summary>
internal sealed class ShopBookingSettingsStore(TrimmeDbContext db, IPlatformSettings settings, IAuditLog audit, TimeProvider clock)
{
    public async Task<ShopBookingSettingsResponse?> GetAsync(ShopId shopId, CancellationToken cancellationToken) =>
        await db.Set<Shop>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == shopId, cancellationToken) is { } shop
            ? ShopBookingSettingsMapping.ToResponse(shop, await settings.GetAsync(cancellationToken))
            : null;

    public async Task<Result<ShopBookingSettingsResponse>> UpdateAsync(ShopId shopId, ShopBookingSettingsRequest request, string by, CancellationToken cancellationToken)
    {
        if (await db.Set<Shop>().SingleOrDefaultAsync(s => s.Id == shopId, cancellationToken) is not { } shop)
        {
            return ShopErrors.NotFound();
        }

        var set = shop.SetBookingSettings(
            request.SlotStepMinutes, request.MinLeadTimeMinutes, request.BookingHorizonDays, request.BufferMinutes,
            clock.GetUtcNow());
        if (set.IsFailure)
        {
            return set.Error;
        }

        // D-155: the settings are the summary's arguments, not the reason (the reason is an operator's own words).
        audit.Record(new AuditRecord(
            "shop.booking_settings_changed", nameof(Shop), shop.Id.ToString(), shop.Id, $"Booking settings set by {by}: {ShopBookingSettingsMapping.Describe(shop)}",
            SummaryArgs: AuditArgs.Of(
                ("by", by), ("slotStep", shop.SlotStepMinutes), ("minLeadTime", shop.MinLeadTimeMinutes), ("horizonDays", shop.BookingHorizonDays),
                ("buffer", shop.BufferMinutes))));
        await db.SaveChangesAsync(cancellationToken);
        return ShopBookingSettingsMapping.ToResponse(shop, await settings.GetAsync(cancellationToken));
    }
}

/// <summary>The shop comes from <see cref="ICurrentTenant"/>, never from the request (R-TEN-01).</summary>
internal sealed class GetOwnBookingSettingsHandler(ICurrentTenant tenant, ShopBookingSettingsStore store)
    : IQueryHandler<GetOwnBookingSettingsQuery, ShopBookingSettingsResponse?>
{
    public async Task<ShopBookingSettingsResponse?> Handle(GetOwnBookingSettingsQuery query, CancellationToken cancellationToken) =>
        tenant.ShopId is { } shopId ? await store.GetAsync(shopId, cancellationToken) : null;
}

internal sealed class UpdateOwnBookingSettingsHandler(ICurrentTenant tenant, ShopBookingSettingsStore store)
    : ICommandHandler<UpdateOwnBookingSettingsCommand, Result<ShopBookingSettingsResponse>>
{
    public async Task<Result<ShopBookingSettingsResponse>> Handle(UpdateOwnBookingSettingsCommand command, CancellationToken cancellationToken) =>
        tenant.ShopId is { } shopId ? await store.UpdateAsync(shopId, command.Settings, "shop", cancellationToken) : ShopErrors.NotFound();
}

internal sealed class GetShopBookingSettingsHandler(ShopBookingSettingsStore store) : IQueryHandler<GetShopBookingSettingsQuery, ShopBookingSettingsResponse?>
{
    public Task<ShopBookingSettingsResponse?> Handle(GetShopBookingSettingsQuery query, CancellationToken cancellationToken) =>
        store.GetAsync(new ShopId(query.ShopId), cancellationToken);
}

internal sealed class UpdateShopBookingSettingsHandler(ShopBookingSettingsStore store)
    : ICommandHandler<UpdateShopBookingSettingsCommand, Result<ShopBookingSettingsResponse>>
{
    public Task<Result<ShopBookingSettingsResponse>> Handle(UpdateShopBookingSettingsCommand command, CancellationToken cancellationToken) =>
        store.UpdateAsync(new ShopId(command.ShopId), command.Settings, "admin", cancellationToken);
}
