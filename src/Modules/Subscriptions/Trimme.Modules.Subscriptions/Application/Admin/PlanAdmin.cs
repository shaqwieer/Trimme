using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Subscriptions.Domain;

namespace Trimme.Modules.Subscriptions.Application.Admin;

// SuperAdmin plan management (spec §7 "SuperAdmin subscription authority", §15; R-SUB-01/02, R-NEG-08). Everything
// about a plan is data: names, interval, limits and prices. A price change adds a version; nothing already recorded
// on a subscription changes. Reading plans needs Admin.Subscriptions.View; every write is SuperAdmin-only (endpoints).

public enum PlanStateChange
{
    Publish,
    Deactivate,
    Archive,
}

internal interface IPlanFields
{
    string NameAr { get; }

    string NameEn { get; }

    string? DescriptionAr { get; }

    string? DescriptionEn { get; }

    IReadOnlyList<PlanFeatureDto>? Features { get; }

    int? MaxProfessionals { get; }

    int? MaxServices { get; }

    BillingIntervalUnit IntervalUnit { get; }

    int IntervalCount { get; }

    int? TrialDays { get; }

    int? GraceDays { get; }

    bool AvailableToNewShops { get; }
}

internal sealed record ListPlansQuery : IQuery<IReadOnlyList<PlanResponse>>;

internal sealed record GetPlanQuery(Guid PlanId) : IQuery<PlanResponse?>;

internal sealed record CreatePlanCommand(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    IReadOnlyList<PlanFeatureDto>? Features,
    int? MaxProfessionals,
    int? MaxServices,
    BillingIntervalUnit IntervalUnit,
    int IntervalCount,
    int? TrialDays,
    int? GraceDays,
    bool AvailableToNewShops,
    decimal? InitialPrice) : ICommand<Result<PlanResponse>>, IPlanFields;

internal sealed record UpdatePlanCommand(
    Guid PlanId,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    IReadOnlyList<PlanFeatureDto>? Features,
    int? MaxProfessionals,
    int? MaxServices,
    BillingIntervalUnit IntervalUnit,
    int IntervalCount,
    int? TrialDays,
    int? GraceDays,
    bool AvailableToNewShops,
    uint Version) : ICommand<Result<PlanResponse>>, IPlanFields;

internal sealed record ChangePlanStateCommand(Guid PlanId, PlanStateChange Change) : ICommand<Result<PlanResponse>>;

internal sealed record ReorderPlansCommand(IReadOnlyList<Guid> OrderedIds) : ICommand<Result<IReadOnlyList<PlanResponse>>>;

internal sealed record AddPlanPriceCommand(Guid PlanId, decimal Amount, DateOnly EffectiveFrom, uint Version) : ICommand<Result<PlanResponse>>;

internal sealed class PlanRules<T> : AbstractValidator<T>
    where T : IPlanFields
{
    public PlanRules()
    {
        RuleFor(c => c.NameAr).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("validation.required")
            .MaximumLength(SubscriptionPlan.MaxNameLength).WithErrorCode("validation.too_long");
        RuleFor(c => c.NameEn).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("validation.required")
            .MaximumLength(SubscriptionPlan.MaxNameLength).WithErrorCode("validation.too_long");
        RuleFor(c => c.DescriptionAr).MaximumLength(SubscriptionPlan.MaxDescriptionLength).WithErrorCode("validation.too_long");
        RuleFor(c => c.DescriptionEn).MaximumLength(SubscriptionPlan.MaxDescriptionLength).WithErrorCode("validation.too_long");
        RuleFor(c => c.Features).Must(f => f is null || f.Count <= SubscriptionPlan.MaxFeatures).WithErrorCode("validation.too_many");
        RuleForEach(c => c.Features).Must(f => !string.IsNullOrWhiteSpace(f.Ar) && !string.IsNullOrWhiteSpace(f.En) && f.Ar.Length <= 120 && f.En.Length <= 120)
            .WithErrorCode("validation.invalid");
        RuleFor(c => c.IntervalUnit).IsInEnum().WithErrorCode("validation.invalid");
        RuleFor(c => c.IntervalCount).Must((c, n) => n >= 1 && n <= (c.IntervalUnit == BillingIntervalUnit.Month ? 36 : 1095))
            .WithErrorCode("validation.out_of_range");
        RuleFor(c => c.MaxProfessionals).InclusiveBetween(1, 10_000).When(c => c.MaxProfessionals is not null).WithErrorCode("validation.out_of_range");
        RuleFor(c => c.MaxServices).InclusiveBetween(1, 10_000).When(c => c.MaxServices is not null).WithErrorCode("validation.out_of_range");
        RuleFor(c => c.TrialDays).InclusiveBetween(0, 365).When(c => c.TrialDays is not null).WithErrorCode("validation.out_of_range");
        RuleFor(c => c.GraceDays).InclusiveBetween(0, 365).When(c => c.GraceDays is not null).WithErrorCode("validation.out_of_range");
    }
}

internal sealed class CreatePlanValidator : AbstractValidator<CreatePlanCommand>
{
    public CreatePlanValidator()
    {
        Include(new PlanRules<CreatePlanCommand>());
        RuleFor(c => c.InitialPrice).Must(p => p is null || PlanPricing.IsValidAmount(p.Value)).WithErrorCode("validation.price_invalid");
    }
}

internal sealed class UpdatePlanValidator : AbstractValidator<UpdatePlanCommand>
{
    public UpdatePlanValidator() => Include(new PlanRules<UpdatePlanCommand>());
}

internal sealed class AddPlanPriceValidator : AbstractValidator<AddPlanPriceCommand>
{
    public AddPlanPriceValidator() => RuleFor(c => c.Amount).Must(PlanPricing.IsValidAmount).WithErrorCode("validation.price_invalid");
}

internal static class PlanReader
{
    public static PlanDetails Details(IPlanFields f) => new(
        f.NameAr, f.NameEn, f.DescriptionAr, f.DescriptionEn,
        [.. (f.Features ?? []).Select(x => PlanFeature.Of(x.Ar, x.En))],
        f.MaxProfessionals, f.MaxServices, f.IntervalUnit, f.IntervalCount, f.TrialDays, f.GraceDays, f.AvailableToNewShops);

    public static Task<SubscriptionPlan?> FindAsync(TrimmeDbContext db, Guid planId, CancellationToken cancellationToken)
    {
        var id = new SubscriptionPlanId(planId);
        return db.Set<SubscriptionPlan>().Include(p => p.Prices).SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    /// <summary>Subscriptions whose latest period is on each plan (read inside the admin scope).</summary>
    public static async Task<IReadOnlyDictionary<SubscriptionPlanId, int>> CountsAsync(TrimmeDbContext db, IAdminDataScope scope, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        return await db.Set<ShopSubscription>().GroupBy(s => s.PlanId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
    }

    public static async Task<PlanResponse> ResponseAsync(
        TrimmeDbContext db, IAdminDataScope scope, IPlatformSettings settings, TimeProvider clock, SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        var today = (await settings.GetAsync(cancellationToken)).LocalDate(clock.GetUtcNow());
        var counts = await CountsAsync(db, scope, cancellationToken);
        return SubscriptionView.Plan(plan, today, counts.GetValueOrDefault(plan.Id));
    }

    public static string Money(decimal amount, string currency) => $"{amount.ToString("0.00", CultureInfo.InvariantCulture)} {currency}";

    public static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

internal sealed class ListPlansHandler(TrimmeDbContext db, IAdminDataScope scope, IPlatformSettings settings, TimeProvider clock)
    : IQueryHandler<ListPlansQuery, IReadOnlyList<PlanResponse>>
{
    public async Task<IReadOnlyList<PlanResponse>> Handle(ListPlansQuery query, CancellationToken cancellationToken)
    {
        var today = (await settings.GetAsync(cancellationToken)).LocalDate(clock.GetUtcNow());
        var plans = await db.Set<SubscriptionPlan>().AsNoTracking().Include(p => p.Prices)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.CreatedAt).ToListAsync(cancellationToken);
        var counts = await PlanReader.CountsAsync(db, scope, cancellationToken);
        return [.. plans.Select(p => SubscriptionView.Plan(p, today, counts.GetValueOrDefault(p.Id)))];
    }
}

internal sealed class GetPlanHandler(TrimmeDbContext db, IAdminDataScope scope, IPlatformSettings settings, TimeProvider clock)
    : IQueryHandler<GetPlanQuery, PlanResponse?>
{
    public async Task<PlanResponse?> Handle(GetPlanQuery query, CancellationToken cancellationToken) =>
        await PlanReader.FindAsync(db, query.PlanId, cancellationToken) is { } plan
            ? await PlanReader.ResponseAsync(db, scope, settings, clock, plan, cancellationToken)
            : null;
}

internal sealed class CreatePlanHandler(TrimmeDbContext db, IAdminDataScope scope, IPlatformSettings settings, ICurrentUser user, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<CreatePlanCommand, Result<PlanResponse>>
{
    public async Task<Result<PlanResponse>> Handle(CreatePlanCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var order = (await db.Set<SubscriptionPlan>().MaxAsync(p => (int?)p.DisplayOrder, cancellationToken) ?? 0) + 1;
        var created = SubscriptionPlan.Create(EntityId.New<SubscriptionPlanId>(), PlanReader.Details(command), order, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var plan = created.Value;
        if (command.InitialPrice is { } amount)
        {
            var platform = await settings.GetAsync(cancellationToken);
            var today = platform.LocalDate(now);
            var price = plan.AddPrice(EntityId.New<PlanPriceId>(), amount, platform.Currency, platform.Currency, today, today, user.UserId, now);
            if (price.IsFailure)
            {
                return price.Error;
            }
        }

        db.Add(plan);
        audit.Record(new AuditRecord("plan.created", "SubscriptionPlan", plan.Id.ToString(), null, $"Plan created (Draft){(command.InitialPrice is { } p ? $", price {PlanReader.Money(p, plan.Prices[0].Currency)}" : string.Empty)}", null));
        await db.SaveChangesAsync(cancellationToken);
        return await PlanReader.ResponseAsync(db, scope, settings, clock, plan, cancellationToken);
    }
}

internal sealed class UpdatePlanHandler(TrimmeDbContext db, IAdminDataScope scope, IPlatformSettings settings, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<UpdatePlanCommand, Result<PlanResponse>>
{
    public async Task<Result<PlanResponse>> Handle(UpdatePlanCommand command, CancellationToken cancellationToken)
    {
        if (await PlanReader.FindAsync(db, command.PlanId, cancellationToken) is not { } plan)
        {
            return PlanErrors.NotFound();
        }

        db.Entry(plan).Property(p => p.Version).OriginalValue = command.Version;
        var updated = plan.Update(PlanReader.Details(command), clock.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        audit.Record(new AuditRecord("plan.updated", "SubscriptionPlan", plan.Id.ToString(), null, "Plan details updated", null));
        await db.SaveChangesAsync(cancellationToken);
        return await PlanReader.ResponseAsync(db, scope, settings, clock, plan, cancellationToken);
    }
}

internal sealed class ChangePlanStateHandler(TrimmeDbContext db, IAdminDataScope scope, IPlatformSettings settings, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<ChangePlanStateCommand, Result<PlanResponse>>
{
    public async Task<Result<PlanResponse>> Handle(ChangePlanStateCommand command, CancellationToken cancellationToken)
    {
        if (await PlanReader.FindAsync(db, command.PlanId, cancellationToken) is not { } plan)
        {
            return PlanErrors.NotFound();
        }

        var now = clock.GetUtcNow();
        var (result, action) = command.Change switch
        {
            PlanStateChange.Publish => (plan.Publish(now), "plan.published"),
            PlanStateChange.Deactivate => (plan.Deactivate(now), "plan.deactivated"),
            _ => (plan.Archive(now), "plan.archived"),
        };
        if (result.IsFailure)
        {
            return result.Error;
        }

        audit.Record(new AuditRecord(action, "SubscriptionPlan", plan.Id.ToString(), null, $"Plan is now {plan.Status}", null));
        await db.SaveChangesAsync(cancellationToken);
        return await PlanReader.ResponseAsync(db, scope, settings, clock, plan, cancellationToken);
    }
}

internal sealed class ReorderPlansHandler(TrimmeDbContext db, IAdminDataScope scope, IPlatformSettings settings, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<ReorderPlansCommand, Result<IReadOnlyList<PlanResponse>>>
{
    public async Task<Result<IReadOnlyList<PlanResponse>>> Handle(ReorderPlansCommand command, CancellationToken cancellationToken)
    {
        var plans = await db.Set<SubscriptionPlan>().Include(p => p.Prices).Where(p => p.Status != PlanStatus.Archived).ToListAsync(cancellationToken);
        var ids = command.OrderedIds.Select(i => new SubscriptionPlanId(i)).ToArray();
        if (ids.Length != plans.Count || ids.Distinct().Count() != ids.Length || !plans.All(p => ids.Contains(p.Id)))
        {
            return PlanErrors.Field("orderedIds", "validation.order_mismatch");
        }

        for (var i = 0; i < ids.Length; i++)
        {
            plans.Single(p => p.Id == ids[i]).MoveTo(i + 1);
        }

        audit.Record(new AuditRecord("plan.reordered", "SubscriptionPlan", "*", null, $"{ids.Length} plans reordered", null));
        await db.SaveChangesAsync(cancellationToken);
        var today = (await settings.GetAsync(cancellationToken)).LocalDate(clock.GetUtcNow());
        var counts = await PlanReader.CountsAsync(db, scope, cancellationToken);
        return plans.OrderBy(p => p.DisplayOrder).Select(p => SubscriptionView.Plan(p, today, counts.GetValueOrDefault(p.Id))).ToList();
    }
}

internal sealed class AddPlanPriceHandler(TrimmeDbContext db, IAdminDataScope scope, IPlatformSettings settings, ICurrentUser user, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<AddPlanPriceCommand, Result<PlanResponse>>
{
    public async Task<Result<PlanResponse>> Handle(AddPlanPriceCommand command, CancellationToken cancellationToken)
    {
        if (await PlanReader.FindAsync(db, command.PlanId, cancellationToken) is not { } plan)
        {
            return PlanErrors.NotFound();
        }

        db.Entry(plan).Property(p => p.Version).OriginalValue = command.Version;
        var platform = await settings.GetAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var added = plan.AddPrice(EntityId.New<PlanPriceId>(), command.Amount, platform.Currency, platform.Currency, command.EffectiveFrom, platform.LocalDate(now), user.UserId, now);
        if (added.IsFailure)
        {
            return added.Error;
        }

        // A child with a preset key reached through a navigation would be taken for an existing row; add it explicitly.
        var price = added.Value;
        db.Add(price);
        audit.Record(new AuditRecord(
            "plan.price_added", "SubscriptionPlan", plan.Id.ToString(), null,
            $"Price version {price.VersionNumber}: {PlanReader.Money(price.Amount, price.Currency)} from {PlanReader.Day(price.EffectiveFrom)}", null));
        await db.SaveChangesAsync(cancellationToken);
        return await PlanReader.ResponseAsync(db, scope, settings, clock, plan, cancellationToken);
    }
}
