using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Application;

/// <summary>
/// Whether the shop takes online bookings right now (s-hours pause card, D-013). While paused the shop gets no new
/// online bookings and no availability; confirmed appointments stay, and walk-ins still work.
/// </summary>
public sealed record OnlineBookingStateResponse(bool Paused, DateTimeOffset? PausedAt, string? Reason);

internal sealed record GetOnlineBookingStateQuery : IQuery<OnlineBookingStateResponse?>;

internal sealed record PauseOnlineBookingCommand(string? Reason) : ICommand<Result<OnlineBookingStateResponse>>;

internal sealed record ResumeOnlineBookingCommand : ICommand<Result<OnlineBookingStateResponse>>;

internal sealed class PauseOnlineBookingValidator : AbstractValidator<PauseOnlineBookingCommand>
{
    public PauseOnlineBookingValidator() =>
        RuleFor(c => c.Reason).MaximumLength(OnlineBookingPause.MaxReasonLength).WithErrorCode("validation.too_long");
}

internal sealed class GetOnlineBookingStateHandler(TrimmeDbContext db, ICurrentTenant tenant) : IQueryHandler<GetOnlineBookingStateQuery, OnlineBookingStateResponse?>
{
    public async Task<OnlineBookingStateResponse?> Handle(GetOnlineBookingStateQuery query, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId)
        {
            return null;
        }

        return await db.Set<Shop>().AnyAsync(s => s.Id == shopId, cancellationToken)
            ? OnlineBookingMapping.ToResponse(await db.Set<OnlineBookingPause>().AsNoTracking().SingleOrDefaultAsync(p => p.ShopId == shopId, cancellationToken))
            : null;
    }
}

/// <summary>
/// Pause and resume write only the pause row, never the shop row, so the profile's version is untouched: pausing in an
/// emergency never makes an open settings form stale, and the form never blocks a pause (D-083).
/// </summary>
internal sealed class PauseOnlineBookingHandler(TrimmeDbContext db, ICurrentTenant tenant, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<PauseOnlineBookingCommand, Result<OnlineBookingStateResponse>>
{
    public async Task<Result<OnlineBookingStateResponse>> Handle(PauseOnlineBookingCommand command, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId || !await db.Set<Shop>().AnyAsync(s => s.Id == shopId, cancellationToken))
        {
            return ShopErrors.NotFound();
        }

        // Pausing twice is refused so the original time is kept; a concurrent double pause hits the key (409, D-080).
        if (await db.Set<OnlineBookingPause>().AnyAsync(p => p.ShopId == shopId, cancellationToken))
        {
            return ShopErrors.OnlineBookingAlreadyPaused();
        }

        var pause = new OnlineBookingPause(shopId, clock.GetUtcNow(), command.Reason);
        db.Add(pause);
        audit.Record(new AuditRecord("shop.online_booking_paused", nameof(Shop), shopId.ToString(), shopId, "Online booking paused by shop", pause.Reason));
        await db.SaveChangesAsync(cancellationToken);
        return OnlineBookingMapping.ToResponse(pause);
    }
}

internal sealed class ResumeOnlineBookingHandler(TrimmeDbContext db, ICurrentTenant tenant, IAuditLog audit)
    : ICommandHandler<ResumeOnlineBookingCommand, Result<OnlineBookingStateResponse>>
{
    public async Task<Result<OnlineBookingStateResponse>> Handle(ResumeOnlineBookingCommand command, CancellationToken cancellationToken)
    {
        if (tenant.ShopId is not { } shopId || !await db.Set<Shop>().AnyAsync(s => s.Id == shopId, cancellationToken))
        {
            return ShopErrors.NotFound();
        }

        if (await db.Set<OnlineBookingPause>().SingleOrDefaultAsync(p => p.ShopId == shopId, cancellationToken) is not { } pause)
        {
            return ShopErrors.OnlineBookingNotPaused();
        }

        db.Remove(pause);
        audit.Record(new AuditRecord("shop.online_booking_resumed", nameof(Shop), shopId.ToString(), shopId, "Online booking resumed by shop"));
        await db.SaveChangesAsync(cancellationToken);
        return OnlineBookingMapping.ToResponse(null);
    }
}

internal static class OnlineBookingMapping
{
    public static OnlineBookingStateResponse ToResponse(OnlineBookingPause? pause) =>
        new(pause is not null, pause?.PausedAt, pause?.Reason);
}
