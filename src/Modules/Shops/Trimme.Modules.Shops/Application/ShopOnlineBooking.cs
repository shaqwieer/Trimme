using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Application;

/// <summary>
/// Whether the shop takes online bookings right now (D-013). While paused the shop gets no new online bookings and no
/// availability; confirmed appointments stay, and walk-ins still work. D-155 (the client's 1-1): <c>Reason</c> is shown
/// to customers, <c>ResumesAt</c> is when booking opens again by itself (null until someone resumes it), and
/// <c>ByAdmin</c> says the platform paused it. A pause past its return time reads as not paused.
/// </summary>
public sealed record OnlineBookingStateResponse(bool Paused, DateTimeOffset? PausedAt, string? Reason, DateTimeOffset? ResumesAt = null, bool ByAdmin = false);

internal sealed record GetOnlineBookingStateQuery : IQuery<OnlineBookingStateResponse?>;

internal sealed record PauseOnlineBookingCommand(string? Reason, DateTimeOffset? ResumesAt = null) : ICommand<Result<OnlineBookingStateResponse>>;

internal sealed record ResumeOnlineBookingCommand : ICommand<Result<OnlineBookingStateResponse>>;

internal sealed class PauseOnlineBookingValidator : AbstractValidator<PauseOnlineBookingCommand>
{
    public PauseOnlineBookingValidator() =>
        RuleFor(c => c.Reason).MaximumLength(OnlineBookingPause.MaxReasonLength).WithErrorCode("validation.too_long");
}

/// <summary>
/// Pause and resume (D-013, D-155) for the owner (tenant from claims) and the admin (any shop, inside the admin scope).
/// They write only the pause row, never the shop row, so the profile's version is untouched (D-083). Pausing tells the
/// shop how many confirmed bookings fall inside the pause (until the return time, or the next week without one): they
/// are kept, and the shop handles them (D-155).
/// </summary>
internal sealed class OnlineBookingPauses(TrimmeDbContext db, IAuditLog audit, INotificationCenter notices, IBookedTimeReader bookings, TimeProvider clock)
{
    /// <summary>The kind of the shop's notice when online booking is paused (D-155).</summary>
    public const string PausedNoticeKind = "shop.online_booking_paused";

    /// <summary>Without a return time, the bookings of the next week are counted.</summary>
    private static readonly TimeSpan OpenEndedWindow = TimeSpan.FromDays(7);

    public async Task<OnlineBookingStateResponse?> StateAsync(ShopId shopId, CancellationToken cancellationToken) =>
        await db.Set<Shop>().AnyAsync(s => s.Id == shopId, cancellationToken)
            ? OnlineBookingMapping.ToResponse(
                await db.Set<OnlineBookingPause>().AsNoTracking().SingleOrDefaultAsync(p => p.ShopId == shopId, cancellationToken), clock.GetUtcNow())
            : null;

    public async Task<Result<OnlineBookingStateResponse>> PauseAsync(
        ShopId shopId, string? reason, DateTimeOffset? resumesAt, bool byAdmin, CancellationToken cancellationToken)
    {
        if (!await db.Set<Shop>().AnyAsync(s => s.Id == shopId, cancellationToken))
        {
            return ShopErrors.NotFound();
        }

        var now = clock.GetUtcNow();
        if (resumesAt is { } until && (until <= now || until > now.AddDays(OnlineBookingPause.MaxDays)))
        {
            return Error.Validation("validation.failed", "The return time is out of range.",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["resumesAt"] = [until <= now ? "validation.date_in_past" : "validation.out_of_range"] });
        }

        // Pausing twice is refused so the original time is kept; a pause past its return time is over and is replaced. A
        // concurrent double pause hits the key (409, D-080).
        if (await db.Set<OnlineBookingPause>().SingleOrDefaultAsync(p => p.ShopId == shopId, cancellationToken) is { } current)
        {
            if (current.InForce(now))
            {
                return ShopErrors.OnlineBookingAlreadyPaused();
            }

            db.Remove(current);
            await db.SaveChangesAsync(cancellationToken);
        }

        var pause = new OnlineBookingPause(shopId, now, reason, resumesAt, byAdmin);
        db.Add(pause);
        var by = byAdmin ? "admin" : "shop";
        audit.Record(new AuditRecord(
            "shop.online_booking_paused", nameof(Shop), shopId.ToString(), shopId,
            byAdmin ? "Online booking paused by the platform" : "Online booking paused by shop", pause.Reason,
            SummaryArgs: AuditArgs.Of(("by", by), ("resumesAt", resumesAt))));

        // The confirmed bookings inside the pause stay; the shop is told how many to look after.
        var held = await bookings.GetAppointmentsAsync(shopId, null, now, resumesAt ?? now.Add(OpenEndedWindow), cancellationToken);
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bookings"] = held.Count.ToString(CultureInfo.InvariantCulture),
            ["by"] = by,
        };
        if (resumesAt is { } back)
        {
            parameters["resumesAt"] = back.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        await notices.NotifyShopAsync(
            shopId,
            new InAppNotice(PausedNoticeKind, string.Create(CultureInfo.InvariantCulture, $"online-booking-paused:{now.UtcTicks}"), parameters),
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await notices.PushPendingAsync(cancellationToken);
        return OnlineBookingMapping.ToResponse(pause, now);
    }

    public async Task<Result<OnlineBookingStateResponse>> ResumeAsync(ShopId shopId, bool byAdmin, CancellationToken cancellationToken)
    {
        if (!await db.Set<Shop>().AnyAsync(s => s.Id == shopId, cancellationToken))
        {
            return ShopErrors.NotFound();
        }

        var now = clock.GetUtcNow();
        if (await db.Set<OnlineBookingPause>().SingleOrDefaultAsync(p => p.ShopId == shopId, cancellationToken) is not { } pause || !pause.InForce(now))
        {
            return ShopErrors.OnlineBookingNotPaused();
        }

        db.Remove(pause);
        audit.Record(new AuditRecord(
            "shop.online_booking_resumed", nameof(Shop), shopId.ToString(), shopId,
            byAdmin ? "Online booking resumed by the platform" : "Online booking resumed by shop",
            SummaryArgs: AuditArgs.Of(("by", byAdmin ? "admin" : "shop"))));
        await db.SaveChangesAsync(cancellationToken);
        return OnlineBookingMapping.ToResponse(null, now);
    }
}

internal sealed class GetOnlineBookingStateHandler(ICurrentTenant tenant, OnlineBookingPauses pauses) : IQueryHandler<GetOnlineBookingStateQuery, OnlineBookingStateResponse?>
{
    public async Task<OnlineBookingStateResponse?> Handle(GetOnlineBookingStateQuery query, CancellationToken cancellationToken) =>
        tenant.ShopId is { } shopId ? await pauses.StateAsync(shopId, cancellationToken) : null;
}

/// <summary>The owner pauses their own shop (D-155: «للمالك والإدارة بس», no longer the barbers).</summary>
internal sealed class PauseOnlineBookingHandler(ICurrentTenant tenant, OnlineBookingPauses pauses)
    : ICommandHandler<PauseOnlineBookingCommand, Result<OnlineBookingStateResponse>>
{
    public async Task<Result<OnlineBookingStateResponse>> Handle(PauseOnlineBookingCommand command, CancellationToken cancellationToken) =>
        tenant.ShopId is { } shopId
            ? await pauses.PauseAsync(shopId, command.Reason, command.ResumesAt, byAdmin: false, cancellationToken)
            : ShopErrors.NotFound();
}

internal sealed class ResumeOnlineBookingHandler(ICurrentTenant tenant, OnlineBookingPauses pauses)
    : ICommandHandler<ResumeOnlineBookingCommand, Result<OnlineBookingStateResponse>>
{
    public async Task<Result<OnlineBookingStateResponse>> Handle(ResumeOnlineBookingCommand command, CancellationToken cancellationToken) =>
        tenant.ShopId is { } shopId ? await pauses.ResumeAsync(shopId, byAdmin: false, cancellationToken) : ShopErrors.NotFound();
}

internal static class OnlineBookingMapping
{
    public static OnlineBookingStateResponse ToResponse(OnlineBookingPause? pause, DateTimeOffset now) =>
        pause is not null && pause.InForce(now)
            ? new(true, pause.PausedAt, pause.Reason, pause.ResumesAt, pause.ByAdmin)
            : new(false, null, null);
}
