using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Subscriptions.Domain;

namespace Trimme.Modules.Subscriptions.Application.Admin;

// Shop subscriptions for platform admins (spec §15; R-SUB-02/03/04). Activation and renewal are recorded manually —
// v1 collects no payment. Each period snapshots the plan name and the price version in force on its start date, and
// an override (SuperAdmin) keeps the previous values. Every write is audited and version-checked, and keeps the
// platform coverage row (D-078) in step in the same unit of work.

internal sealed record ListSubscriptionsQuery(PageRequest Page, SubscriptionStatus? Status, string? Search) : IQuery<AdminSubscriptionListResponse>;

internal sealed record GetShopSubscriptionQuery(Guid ShopId) : IQuery<Result<AdminShopSubscriptionResponse>>;

internal sealed record AssignSubscriptionCommand(Guid ShopId, Guid PlanId, DateOnly? StartDate, int? DurationDays, string? Notes, decimal? Price, string? Reason)
    : ICommand<Result<AdminShopSubscriptionResponse>>;

internal sealed record RenewSubscriptionCommand(
    Guid ShopId, Guid? PlanId, DateOnly? StartDate, int? DurationDays, string? Notes, decimal? Price, string? Reason, uint Version)
    : ICommand<Result<AdminShopSubscriptionResponse>>;

internal sealed record OverrideSubscriptionCommand(Guid ShopId, decimal? Price, DateOnly? EndDate, string Reason, uint Version)
    : ICommand<Result<AdminShopSubscriptionResponse>>;

internal sealed record SuspendSubscriptionCommand(Guid ShopId, string Reason, uint Version) : ICommand<Result<AdminShopSubscriptionResponse>>;

internal sealed record ReinstateSubscriptionCommand(Guid ShopId, uint Version) : ICommand<Result<AdminShopSubscriptionResponse>>;

internal sealed class AssignSubscriptionValidator : AbstractValidator<AssignSubscriptionCommand>
{
    public AssignSubscriptionValidator()
    {
        RuleFor(c => c.DurationDays).InclusiveBetween(1, 1095).When(c => c.DurationDays is not null).WithErrorCode("validation.out_of_range");
        RuleFor(c => c.Notes).MaximumLength(ShopSubscription.MaxReasonLength).WithErrorCode("validation.too_long");
        RuleFor(c => c.Price).Must(p => p is null || PlanPricing.IsValidAmount(p.Value)).WithErrorCode("validation.price_invalid");
        RuleFor(c => c.Reason).MaximumLength(ShopSubscription.MaxReasonLength).WithErrorCode("validation.too_long");
    }
}

internal sealed class RenewSubscriptionValidator : AbstractValidator<RenewSubscriptionCommand>
{
    public RenewSubscriptionValidator()
    {
        RuleFor(c => c.DurationDays).InclusiveBetween(1, 1095).When(c => c.DurationDays is not null).WithErrorCode("validation.out_of_range");
        RuleFor(c => c.Notes).MaximumLength(ShopSubscription.MaxReasonLength).WithErrorCode("validation.too_long");
        RuleFor(c => c.Price).Must(p => p is null || PlanPricing.IsValidAmount(p.Value)).WithErrorCode("validation.price_invalid");
        RuleFor(c => c.Reason).MaximumLength(ShopSubscription.MaxReasonLength).WithErrorCode("validation.too_long");
    }
}

internal sealed class OverrideSubscriptionValidator : AbstractValidator<OverrideSubscriptionCommand>
{
    public OverrideSubscriptionValidator()
    {
        RuleFor(c => c.Price).Must(p => p is null || PlanPricing.IsValidAmount(p.Value)).WithErrorCode("validation.price_invalid");
        RuleFor(c => c.Price).NotNull().When(c => c.EndDate is null).WithErrorCode("validation.required");
        RuleFor(c => c.Reason).Must(r => (r?.Trim().Length ?? 0) >= 5).WithErrorCode("validation.reason_required")
            .MaximumLength(ShopSubscription.MaxReasonLength).WithErrorCode("validation.too_long");
    }
}

internal sealed class SuspendSubscriptionValidator : AbstractValidator<SuspendSubscriptionCommand>
{
    public SuspendSubscriptionValidator() => RuleFor(c => c.Reason).Must(r => (r?.Trim().Length ?? 0) >= 5).WithErrorCode("validation.reason_required")
        .MaximumLength(ShopSubscription.MaxReasonLength).WithErrorCode("validation.too_long");
}

internal static class SubscriptionAdminReader
{
    public static Error ShopNotFound() => Error.NotFound("shop.not_found", "The shop was not found.");

    public static Task<ShopSubscription?> FindAsync(TrimmeDbContext db, ShopId shopId, CancellationToken cancellationToken) =>
        db.Set<ShopSubscription>().Include(s => s.Periods).Include(s => s.Overrides).AsSplitQuery()
            .SingleOrDefaultAsync(s => s.ShopId == shopId, cancellationToken);

    public static async Task<AdminShopSubscriptionResponse> ResponseAsync(
        TrimmeDbContext db, PlatformSettingsSnapshot settings, DateOnly today, ShopSummary shop, ShopSubscription? subscription, CancellationToken cancellationToken)
    {
        var status = SubscriptionView.Status(subscription, today, settings);
        if (subscription is null)
        {
            return new AdminShopSubscriptionResponse(
                shop.Id.Value, shop.NameAr, shop.NameEn, false, status, null, null, null, null, null, 0, null, null, false, null,
                today, today, settings.ExpiringSoonThresholdDays, [], [], null);
        }

        var priceIds = subscription.Periods.Where(p => p.PlanPriceId is not null).Select(p => p.PlanPriceId!.Value).Distinct().ToArray();
        var versions = await db.Set<PlanPrice>().AsNoTracking().Where(p => priceIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.VersionNumber, cancellationToken);
        var inForce = subscription.PeriodInForce(today);
        var latest = subscription.LatestPeriod;
        return new AdminShopSubscriptionResponse(
            shop.Id.Value,
            shop.NameAr,
            shop.NameEn,
            true,
            status,
            latest.PlanId.Value,
            inForce.PlanNameAr,
            inForce.PlanNameEn,
            subscription.StartDate,
            subscription.EndDate,
            SubscriptionStatusCalculator.DaysRemaining(subscription.EndDate, today),
            inForce.Amount,
            inForce.Currency,
            subscription.IsSuspended,
            subscription.SuspensionReason,
            today,
            subscription.NextRenewalStart(today),
            settings.ExpiringSoonThresholdDays,
            [.. subscription.Periods.OrderByDescending(p => p.PeriodStart).Select(p => SubscriptionView.Period(p, versions))],
            [.. subscription.Overrides.OrderByDescending(o => o.OverriddenAt).Select(o => new SubscriptionOverrideResponse(
                o.Id.Value, o.PeriodId.Value, o.PreviousAmount, o.NewAmount, o.PreviousEnd, o.NewEnd, o.Reason, o.OverriddenAt))],
            subscription.Version);
    }

    /// <summary>Keeps the platform coverage row (D-078) equal to the subscription; saved with it.</summary>
    public static async Task SyncCoverageAsync(TrimmeDbContext db, ShopSubscription subscription, CancellationToken cancellationToken)
    {
        var coverage = await db.Set<SubscriptionCoverage>().SingleOrDefaultAsync(c => c.ShopId == subscription.ShopId, cancellationToken);
        if (coverage is null)
        {
            coverage = new SubscriptionCoverage(subscription.ShopId);
            db.Add(coverage);
        }

        coverage.CopyFrom(subscription);
    }

    public static PriceSnapshot Snapshot(PlanPrice price) => new(price.Id, price.Amount, price.Currency);

    public const string OverridePermission = "SuperAdmin.Subscriptions.Override";

    /// <summary>
    /// Pricing of a new period (D-081). A standard period (starts today or later, one plan interval) records the plan
    /// price version in force on its start date. A custom duration, a past start or an explicit price needs
    /// <c>SuperAdmin.Subscriptions.Override</c>, an explicit total and a reason; the plan price it replaces is kept.
    /// </summary>
    public static async Task<Result<(PriceSnapshot Price, CustomPricing? Custom)>> PriceAsync(
        SubscriptionPlan plan, DateOnly start, DateOnly end, DateOnly today, decimal? explicitTotal, string? reason, string currency,
        ICurrentUser user, IPermissionResolver permissions, CancellationToken cancellationToken)
    {
        var inForce = plan.PriceOn(start);
        if (ShopSubscription.IsStandardPeriod(plan, start, end, today) && explicitTotal is null)
        {
            return inForce is null ? PlanErrors.NoPriceOn(start) : (Snapshot(inForce), null);
        }

        if (user.UserId is not { } userId || !(await permissions.GetPermissionsAsync(userId, cancellationToken)).Contains(OverridePermission))
        {
            return SubscriptionErrors.CustomPricingRequired();
        }

        if (explicitTotal is not { } total)
        {
            return PlanErrors.Field("price", "validation.required");
        }

        if ((reason?.Trim().Length ?? 0) < 5)
        {
            return PlanErrors.Field("reason", "validation.reason_required");
        }

        var snapshot = inForce is null ? new PriceSnapshot(null, total, currency) : Snapshot(inForce);
        return (snapshot, new CustomPricing(total, inForce?.Amount, reason!.Trim()));
    }

    public static string PricingSummary(SubscriptionPeriod period) =>
        period.PricingReason is null
            ? string.Empty
            : $"; SuperAdmin custom period, total {PlanReader.Money(period.Amount, period.Currency)}" +
              (period.StandardAmount is { } standard ? $" instead of {PlanReader.Money(standard, period.Currency)}" : " (no plan price on the start date)");

    public static string PeriodSummary(SubscriptionPeriod p, int? priceVersion) =>
        $"{p.PlanNameEn}: {PlanReader.Day(p.PeriodStart)} to {PlanReader.Day(p.PeriodEnd)}, {PlanReader.Money(p.Amount, p.Currency)}" +
        (priceVersion is { } v ? $" (price version {v})" : string.Empty);
}

internal sealed class ListSubscriptionsHandler(TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops, IPlatformSettings settings, TimeProvider clock)
    : IQueryHandler<ListSubscriptionsQuery, AdminSubscriptionListResponse>
{
    public async Task<AdminSubscriptionListResponse> Handle(ListSubscriptionsQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var platform = await settings.GetAsync(cancellationToken);
        var today = platform.LocalDate(clock.GetUtcNow());

        // Days remaining = end − today + 1, so "expiring soon" (≤ threshold days left) is end ≤ today + threshold − 1.
        var soonUntil = today.AddDays(platform.ExpiringSoonThresholdDays - 1);
        var all = db.Set<ShopSubscription>().AsNoTracking();
        var counts = new SubscriptionCounts(
            await all.CountAsync(s => !s.IsSuspended && s.EndDate > soonUntil, cancellationToken),
            await all.CountAsync(s => !s.IsSuspended && s.EndDate >= today && s.EndDate <= soonUntil, cancellationToken),
            await all.CountAsync(s => !s.IsSuspended && s.EndDate < today, cancellationToken),
            await all.CountAsync(s => s.IsSuspended, cancellationToken),
            Math.Max(0, await shops.CountAsync(cancellationToken) - await all.CountAsync(cancellationToken)));

        var subscriptions = query.Status switch
        {
            SubscriptionStatus.Active => all.Where(s => !s.IsSuspended && s.EndDate > soonUntil),
            SubscriptionStatus.ExpiringSoon => all.Where(s => !s.IsSuspended && s.EndDate >= today && s.EndDate <= soonUntil),
            SubscriptionStatus.Expired => all.Where(s => !s.IsSuspended && s.EndDate < today),
            SubscriptionStatus.Suspended => all.Where(s => s.IsSuspended),
            SubscriptionStatus.None => all.Where(s => false),
            _ => all,
        };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var matches = await shops.SearchIdsAsync(query.Search, 500, cancellationToken);
            subscriptions = subscriptions.Where(s => matches.Contains(s.ShopId));
        }

        var total = await subscriptions.CountAsync(cancellationToken);
        var page = await subscriptions.Include(s => s.Periods).OrderBy(s => s.EndDate).ThenBy(s => s.ShopId)
            .Skip(query.Page.Skip).Take(query.Page.PageSize).ToListAsync(cancellationToken);
        var names = await shops.FindManyAsync([.. page.Select(s => s.ShopId)], cancellationToken);
        var items = page.Select(s =>
        {
            var shop = names.GetValueOrDefault(s.ShopId);
            var inForce = s.PeriodInForce(today);
            return new AdminSubscriptionListItem(
                s.ShopId.Value, shop?.NameAr ?? string.Empty, shop?.NameEn ?? string.Empty, shop?.Slug ?? string.Empty,
                s.PlanId.Value, inForce.PlanNameAr, inForce.PlanNameEn, s.StartDate, s.EndDate,
                SubscriptionStatusCalculator.DaysRemaining(s.EndDate, today), SubscriptionView.Status(s, today, platform),
                inForce.Amount, inForce.Currency, s.Periods.Count(p => p.Kind == PeriodKind.Renewed));
        });
        return new AdminSubscriptionListResponse([.. items], query.Page.Page, query.Page.PageSize, total, counts, platform.ExpiringSoonThresholdDays, today);
    }
}

internal sealed class GetShopSubscriptionHandler(TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops, IPlatformSettings settings, TimeProvider clock)
    : IQueryHandler<GetShopSubscriptionQuery, Result<AdminShopSubscriptionResponse>>
{
    public async Task<Result<AdminShopSubscriptionResponse>> Handle(GetShopSubscriptionQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var shopId = new ShopId(query.ShopId);
        if (await shops.FindAsync(shopId, cancellationToken) is not { } shop)
        {
            return SubscriptionAdminReader.ShopNotFound();
        }

        var platform = await settings.GetAsync(cancellationToken);
        var subscription = await SubscriptionAdminReader.FindAsync(db, shopId, cancellationToken);
        return await SubscriptionAdminReader.ResponseAsync(db, platform, platform.LocalDate(clock.GetUtcNow()), shop, subscription, cancellationToken);
    }
}

internal sealed class AssignSubscriptionHandler(
    TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops, IPlatformSettings settings, ICurrentUser user, IPermissionResolver permissions,
    IAuditLog audit, TimeProvider clock)
    : ICommandHandler<AssignSubscriptionCommand, Result<AdminShopSubscriptionResponse>>
{
    public async Task<Result<AdminShopSubscriptionResponse>> Handle(AssignSubscriptionCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var shopId = new ShopId(command.ShopId);
        if (await shops.FindAsync(shopId, cancellationToken) is not { } shop)
        {
            return SubscriptionAdminReader.ShopNotFound();
        }

        if (await db.Set<ShopSubscription>().AnyAsync(s => s.ShopId == shopId, cancellationToken))
        {
            return SubscriptionErrors.AlreadyAssigned();
        }

        // Only a published plan that is offered to new shops can start a subscription.
        if (await PlanReader.FindAsync(db, command.PlanId, cancellationToken) is not { Status: PlanStatus.Published, AvailableToNewShops: true } plan)
        {
            return PlanErrors.NotOffered();
        }

        var platform = await settings.GetAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var today = platform.LocalDate(now);
        var start = command.StartDate ?? today;
        var end = command.DurationDays is { } days ? SubscriptionDates.EndAfterDays(start, days) : plan.PeriodEnd(start);
        var pricing = await SubscriptionAdminReader.PriceAsync(
            plan, start, end, today, command.Price, command.Reason, platform.Currency, user, permissions, cancellationToken);
        if (pricing.IsFailure)
        {
            return pricing.Error;
        }

        var (price, custom) = pricing.Value;
        var assigned = ShopSubscription.Assign(
            EntityId.New<ShopSubscriptionId>(), shopId, plan, start, end, price, custom, Trim(command.Notes), user.UserId, today, now);
        if (assigned.IsFailure)
        {
            return assigned.Error;
        }

        var subscription = assigned.Value;
        db.Add(subscription);
        await SubscriptionAdminReader.SyncCoverageAsync(db, subscription, cancellationToken);
        var period = subscription.LatestPeriod;
        audit.Record(new AuditRecord(
            "subscription.assigned", "ShopSubscription", subscription.Id.ToString(), shopId,
            SubscriptionAdminReader.PeriodSummary(period, VersionOf(plan, period)) + SubscriptionAdminReader.PricingSummary(period), custom?.Reason));
        await db.SaveChangesAsync(cancellationToken);
        return await SubscriptionAdminReader.ResponseAsync(db, platform, today, shop, subscription, cancellationToken);
    }

    private static string? Trim(string? notes) => string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    internal static int? VersionOf(SubscriptionPlan plan, SubscriptionPeriod period) =>
        plan.Prices.FirstOrDefault(p => p.Id == period.PlanPriceId)?.VersionNumber;
}

internal sealed class RenewSubscriptionHandler(
    TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops, IPlatformSettings settings, ICurrentUser user, IPermissionResolver permissions,
    IAuditLog audit, TimeProvider clock)
    : ICommandHandler<RenewSubscriptionCommand, Result<AdminShopSubscriptionResponse>>
{
    public async Task<Result<AdminShopSubscriptionResponse>> Handle(RenewSubscriptionCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var shopId = new ShopId(command.ShopId);
        if (await shops.FindAsync(shopId, cancellationToken) is not { } shop)
        {
            return SubscriptionAdminReader.ShopNotFound();
        }

        if (await SubscriptionAdminReader.FindAsync(db, shopId, cancellationToken) is not { } subscription)
        {
            return SubscriptionErrors.NoSubscription();
        }

        db.Entry(subscription).Property(s => s.Version).OriginalValue = command.Version;

        // A renewal may keep an existing shop on a plan no longer offered to new shops (Inactive), never on a draft or
        // archived one.
        var planId = command.PlanId ?? subscription.PlanId.Value;
        if (await PlanReader.FindAsync(db, planId, cancellationToken) is not { Status: PlanStatus.Published or PlanStatus.Inactive } plan)
        {
            return PlanErrors.NotOffered();
        }

        var platform = await settings.GetAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var today = platform.LocalDate(now);
        var start = command.StartDate ?? subscription.NextRenewalStart(today);
        var end = command.DurationDays is { } days ? SubscriptionDates.EndAfterDays(start, days) : plan.PeriodEnd(start);
        var pricing = await SubscriptionAdminReader.PriceAsync(
            plan, start, end, today, command.Price, command.Reason, platform.Currency, user, permissions, cancellationToken);
        if (pricing.IsFailure)
        {
            return pricing.Error;
        }

        var (price, custom) = pricing.Value;
        var notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim();
        var renewed = subscription.Renew(plan, start, end, price, custom, notes, user.UserId, today, now);
        if (renewed.IsFailure)
        {
            return renewed.Error;
        }

        db.Add(renewed.Value);
        await SubscriptionAdminReader.SyncCoverageAsync(db, subscription, cancellationToken);
        audit.Record(new AuditRecord(
            "subscription.renewed", "ShopSubscription", subscription.Id.ToString(), shopId,
            SubscriptionAdminReader.PeriodSummary(renewed.Value, AssignSubscriptionHandler.VersionOf(plan, renewed.Value)) +
            SubscriptionAdminReader.PricingSummary(renewed.Value), custom?.Reason));
        await db.SaveChangesAsync(cancellationToken);
        return await SubscriptionAdminReader.ResponseAsync(db, platform, today, shop, subscription, cancellationToken);
    }
}

internal sealed class OverrideSubscriptionHandler(
    TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops, IPlatformSettings settings, ICurrentUser user, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<OverrideSubscriptionCommand, Result<AdminShopSubscriptionResponse>>
{
    public async Task<Result<AdminShopSubscriptionResponse>> Handle(OverrideSubscriptionCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var shopId = new ShopId(command.ShopId);
        if (await shops.FindAsync(shopId, cancellationToken) is not { } shop)
        {
            return SubscriptionAdminReader.ShopNotFound();
        }

        if (await SubscriptionAdminReader.FindAsync(db, shopId, cancellationToken) is not { } subscription)
        {
            return SubscriptionErrors.NoSubscription();
        }

        db.Entry(subscription).Property(s => s.Version).OriginalValue = command.Version;
        var platform = await settings.GetAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var today = platform.LocalDate(now);
        var reason = command.Reason.Trim();
        var overridden = subscription.Override(EntityId.New<SubscriptionOverrideId>(), command.Price, command.EndDate, reason, user.UserId, today, now);
        if (overridden.IsFailure)
        {
            return overridden.Error;
        }

        var record = overridden.Value;
        db.Add(record);
        await SubscriptionAdminReader.SyncCoverageAsync(db, subscription, cancellationToken);
        var currency = subscription.PeriodInForce(today).Currency;
        var changes = new List<string>();
        if (record.NewAmount is { } amount)
        {
            changes.Add($"amount {PlanReader.Money(record.PreviousAmount, currency)} to {PlanReader.Money(amount, currency)}");
        }

        if (record.NewEnd is { } newEnd)
        {
            changes.Add($"end {PlanReader.Day(record.PreviousEnd)} to {PlanReader.Day(newEnd)}");
        }

        audit.Record(new AuditRecord("subscription.overridden", "ShopSubscription", subscription.Id.ToString(), shopId, $"Override: {string.Join("; ", changes)}", reason));
        await db.SaveChangesAsync(cancellationToken);
        return await SubscriptionAdminReader.ResponseAsync(db, platform, today, shop, subscription, cancellationToken);
    }
}

internal sealed class SuspendSubscriptionHandler(SubscriptionSuspension suspension)
    : ICommandHandler<SuspendSubscriptionCommand, Result<AdminShopSubscriptionResponse>>
{
    public Task<Result<AdminShopSubscriptionResponse>> Handle(SuspendSubscriptionCommand command, CancellationToken cancellationToken) =>
        suspension.ChangeAsync(command.ShopId, command.Version, command.Reason.Trim(), cancellationToken);
}

internal sealed class ReinstateSubscriptionHandler(SubscriptionSuspension suspension)
    : ICommandHandler<ReinstateSubscriptionCommand, Result<AdminShopSubscriptionResponse>>
{
    public Task<Result<AdminShopSubscriptionResponse>> Handle(ReinstateSubscriptionCommand command, CancellationToken cancellationToken) =>
        suspension.ChangeAsync(command.ShopId, command.Version, null, cancellationToken);
}

/// <summary>Suspend (with a reason) or reinstate; registered by the module.</summary>
internal sealed class SubscriptionSuspension(
    TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops, IPlatformSettings settings, IAuditLog audit, TimeProvider clock)
{
    public async Task<Result<AdminShopSubscriptionResponse>> ChangeAsync(Guid id, uint version, string? suspendReason, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var shopId = new ShopId(id);
        if (await shops.FindAsync(shopId, cancellationToken) is not { } shop)
        {
            return SubscriptionAdminReader.ShopNotFound();
        }

        if (await SubscriptionAdminReader.FindAsync(db, shopId, cancellationToken) is not { } subscription)
        {
            return SubscriptionErrors.NoSubscription();
        }

        db.Entry(subscription).Property(s => s.Version).OriginalValue = version;
        var now = clock.GetUtcNow();
        if (suspendReason is not null)
        {
            subscription.Suspend(suspendReason, now);
            audit.Record(new AuditRecord("subscription.suspended", "ShopSubscription", subscription.Id.ToString(), shopId, "Subscription suspended", suspendReason));
        }
        else
        {
            subscription.Reinstate(now);
            audit.Record(new AuditRecord("subscription.reinstated", "ShopSubscription", subscription.Id.ToString(), shopId, "Subscription reinstated", null));
        }

        await SubscriptionAdminReader.SyncCoverageAsync(db, subscription, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var platform = await settings.GetAsync(cancellationToken);
        return await SubscriptionAdminReader.ResponseAsync(db, platform, platform.LocalDate(now), shop, subscription, cancellationToken);
    }
}
