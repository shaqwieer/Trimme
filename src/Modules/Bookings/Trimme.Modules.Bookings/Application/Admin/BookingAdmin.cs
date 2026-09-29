using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Application.Admin;

// Platform admins read bookings across shops and may cancel one on the shop's behalf (D-016: CancelledByShop with the
// PlatformAdmin actor), audited. The full admin UI is Phase 14.

internal sealed record ListAdminBookingsQuery(Guid? ShopId, BookingStatus? Status, DateTimeOffset? From, DateTimeOffset? To, string? Search, PageRequest Page)
    : IQuery<PagedResponse<AdminBookingResponse>>;

internal sealed record GetAdminBookingQuery(Guid BookingId) : IQuery<AdminBookingResponse?>;

internal sealed record AdminCancelBookingCommand(Guid BookingId, string Reason, uint Version) : ICommand<Result<AdminBookingResponse>>;

internal sealed class AdminBookingMapper(IShopDirectory shops, TimeProvider clock)
{
    public async Task<IReadOnlyList<AdminBookingResponse>> MapAsync(IReadOnlyList<Booking> bookings, bool withHistory, CancellationToken cancellationToken)
    {
        var byId = await shops.FindManyAsync([.. bookings.Select(b => b.ShopId).Distinct()], cancellationToken);
        var now = clock.GetUtcNow();
        return
        [
            .. bookings.Select(b => new AdminBookingResponse(
                BookingMapping.Shop(byId.GetValueOrDefault(b.ShopId), b.ShopId), b.CustomerId, BookingMapping.ToShop(b, now, outsideSchedule: false),
                withHistory ? BookingMapping.History(b) : [])),
        ];
    }
}

internal sealed class ListAdminBookingsHandler(TrimmeDbContext db, IAdminDataScope scope, AdminBookingMapper mapper)
    : IQueryHandler<ListAdminBookingsQuery, PagedResponse<AdminBookingResponse>>
{
    public async Task<PagedResponse<AdminBookingResponse>> Handle(ListAdminBookingsQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var bookings = db.Set<Booking>().AsNoTracking();
        if (query.ShopId is { } shop)
        {
            var shopId = new ShopId(shop);
            bookings = bookings.Where(b => b.ShopId == shopId);
        }

        var filtered = ShopBookingReader.Filter(bookings, query.From, query.To, query.Status is { } status ? [status] : null, null, query.Search).OrderByDescending(b => b.StartsAt).ThenBy(b => b.Id);
        var total = await filtered.CountAsync(cancellationToken);
        var page = await filtered.Skip(query.Page.Skip).Take(query.Page.PageSize).ToListAsync(cancellationToken);
        return new PagedResponse<AdminBookingResponse>(await mapper.MapAsync(page, withHistory: false, cancellationToken), query.Page.Page, query.Page.PageSize, total);
    }
}

internal sealed class GetAdminBookingHandler(TrimmeDbContext db, IAdminDataScope scope, AdminBookingMapper mapper) : IQueryHandler<GetAdminBookingQuery, AdminBookingResponse?>
{
    public async Task<AdminBookingResponse?> Handle(GetAdminBookingQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var id = new BookingId(query.BookingId);
        return await db.Set<Booking>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is { } booking
            ? (await mapper.MapAsync([booking], withHistory: true, cancellationToken))[0]
            : null;
    }
}

/// <summary>Admin intervention (spec §14): cancel on the shop's behalf with a reason; audited; the outbox tells both audiences (Phase 15).</summary>
internal sealed class AdminCancelBookingHandler(TrimmeDbContext db, IAdminDataScope scope, ICurrentUser user, IAuditLog audit, AdminBookingMapper mapper, TimeProvider clock)
    : ICommandHandler<AdminCancelBookingCommand, Result<AdminBookingResponse>>
{
    public async Task<Result<AdminBookingResponse>> Handle(AdminCancelBookingCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var id = new BookingId(command.BookingId);
        if (await db.Set<Booking>().SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } booking)
        {
            return BookingErrors.NotFound();
        }

        db.Entry(booking).Property(b => b.Version).OriginalValue = command.Version;
        var now = clock.GetUtcNow();
        var from = booking.Status;
        var cancelled = booking.ApplyShopTransition(BookingStatus.CancelledByShop, new BookingActor(user.UserId, ActorType.PlatformAdmin), command.Reason, now);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        BookingEvents.Add(db, BookingEvents.Cancelled, booking, now);
        audit.Record(new AuditRecord("booking.cancelled_by_admin", nameof(Booking), booking.Id.ToString(), booking.ShopId, $"{from} → CancelledByShop", booking.CancellationReason));
        await db.SaveChangesAsync(cancellationToken);
        return (await mapper.MapAsync([booking], withHistory: true, cancellationToken))[0];
    }
}
