using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Subscriptions.Domain;

namespace Trimme.Modules.Subscriptions.Application.Admin;

// The loyalty add-on (D-149, the client's 6-2: «إضافة مدفوعة، الأدمن يفعّلها أو يوقفها بس»). The admin switches it on for
// a shop until an end date, or off; there is no payment screen. Its price is SuperAdmin's, versioned like a plan's: a
// period keeps the version in force on its start. The coverage row carries its first and last day, so earning and
// redeeming stop by themselves after the end. It goes with a subscription: a shop without one cannot have it.

internal sealed record SwitchOnLoyaltyAddOnCommand(Guid ShopId, DateOnly EndDate, string? Notes) : ICommand<Result<LoyaltyAddOnResponse>>;

internal sealed record StopLoyaltyAddOnCommand(Guid ShopId) : ICommand<Result<LoyaltyAddOnResponse>>;

internal sealed record ListLoyaltyAddOnPricesQuery : IQuery<LoyaltyAddOnPricesResponse>;

internal sealed record AddLoyaltyAddOnPriceCommand(decimal Amount, DateOnly EffectiveFrom) : ICommand<Result<LoyaltyAddOnPricesResponse>>;

internal sealed class SwitchOnLoyaltyAddOnValidator : AbstractValidator<SwitchOnLoyaltyAddOnCommand>
{
    public SwitchOnLoyaltyAddOnValidator() =>
        RuleFor(c => c.Notes).MaximumLength(LoyaltyAddOnPeriod.MaxNotesLength).WithErrorCode("validation.too_long");
}

internal sealed class AddLoyaltyAddOnPriceValidator : AbstractValidator<AddLoyaltyAddOnPriceCommand>
{
    public AddLoyaltyAddOnPriceValidator() => RuleFor(c => c.Amount).Must(PlanPricing.IsValidAmount).WithErrorCode("validation.price_invalid");
}

/// <summary>
/// Switches the add-on on until <c>EndDate</c>: from today, or, while one is in force, from the day after its end (a
/// renewal), at the add-on price in force on that start. Audited.
/// </summary>
internal sealed class SwitchOnLoyaltyAddOnHandler(
    TrimmeDbContext db, SubscriptionExtras extras, IAdminDataScope scope, IShopDirectory shops, IPlatformSettings settings, ICurrentUser user,
    IAuditLog audit, TimeProvider clock)
    : ICommandHandler<SwitchOnLoyaltyAddOnCommand, Result<LoyaltyAddOnResponse>>
{
    public async Task<Result<LoyaltyAddOnResponse>> Handle(SwitchOnLoyaltyAddOnCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var shopId = new ShopId(command.ShopId);
        if (await shops.FindAsync(shopId, cancellationToken) is null)
        {
            return SubscriptionAdminReader.ShopNotFound();
        }

        if (await db.Set<SubscriptionCoverage>().SingleOrDefaultAsync(c => c.ShopId == shopId, cancellationToken) is not { } coverage
            || !await db.Set<ShopSubscription>().AnyAsync(s => s.ShopId == shopId, cancellationToken))
        {
            return LoyaltyAddOnErrors.NoSubscription();
        }

        var now = clock.GetUtcNow();
        var today = (await settings.GetAsync(cancellationToken)).LocalDate(now);
        var running = LoyaltyAddOnRules.IsActive(coverage.LoyaltyAddOnStart, coverage.LoyaltyAddOnEnd, today);
        var start = running ? coverage.LoyaltyAddOnEnd!.Value.AddDays(1) : today;
        if (command.EndDate < start)
        {
            return LoyaltyAddOnErrors.EndBeforeStart();
        }

        var prices = await db.Set<LoyaltyAddOnPrice>().AsNoTracking().ToListAsync(cancellationToken);
        if (LoyaltyAddOnPrice.On(prices, start) is not { } price)
        {
            return LoyaltyAddOnErrors.NoPrice(start);
        }

        var period = LoyaltyAddOnPeriod.Record(shopId, start, command.EndDate, price, command.Notes, user.UserId, now);
        db.Add(period);
        coverage.SetLoyaltyAddOn(running ? coverage.LoyaltyAddOnStart!.Value : start, command.EndDate);
        audit.Record(new AuditRecord(
            "loyalty_add_on.switched_on", nameof(LoyaltyAddOnPeriod), period.Id.ToString(), shopId,
            string.Create(CultureInfo.InvariantCulture, $"{start:yyyy-MM-dd} → {command.EndDate:yyyy-MM-dd}, price v{price.VersionNumber} {price.Amount:0.00} {price.Currency}"),
            period.Notes));
        await db.SaveChangesAsync(cancellationToken);
        return await extras.AddOnAsync(shopId, today, cancellationToken);
    }
}

/// <summary>Switches the add-on off from today: points stop being earned and redeemed, and are kept. Audited.</summary>
internal sealed class StopLoyaltyAddOnHandler(
    TrimmeDbContext db, SubscriptionExtras extras, IAdminDataScope scope, IShopDirectory shops, IPlatformSettings settings, ICurrentUser user,
    IAuditLog audit, TimeProvider clock)
    : ICommandHandler<StopLoyaltyAddOnCommand, Result<LoyaltyAddOnResponse>>
{
    public async Task<Result<LoyaltyAddOnResponse>> Handle(StopLoyaltyAddOnCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var shopId = new ShopId(command.ShopId);
        if (await shops.FindAsync(shopId, cancellationToken) is null)
        {
            return SubscriptionAdminReader.ShopNotFound();
        }

        var today = (await settings.GetAsync(cancellationToken)).LocalDate(clock.GetUtcNow());
        if (await db.Set<SubscriptionCoverage>().SingleOrDefaultAsync(c => c.ShopId == shopId, cancellationToken) is not { } coverage
            || !LoyaltyAddOnRules.IsActive(coverage.LoyaltyAddOnStart, coverage.LoyaltyAddOnEnd, today))
        {
            return LoyaltyAddOnErrors.NotActive();
        }

        foreach (var period in await db.Set<LoyaltyAddOnPeriod>().Where(p => p.ShopId == shopId).ToListAsync(cancellationToken))
        {
            if (period.CoversOrFollows(today))
            {
                period.Stop(today, user.UserId);
            }
        }

        // Its last day is yesterday (a period started today is simply not in force).
        coverage.SetLoyaltyAddOn(coverage.LoyaltyAddOnStart!.Value, today.AddDays(-1));
        audit.Record(new AuditRecord(
            "loyalty_add_on.stopped", nameof(SubscriptionCoverage), shopId.ToString(), shopId,
            string.Create(CultureInfo.InvariantCulture, $"Switched off from {today:yyyy-MM-dd}")));
        await db.SaveChangesAsync(cancellationToken);
        return await extras.AddOnAsync(shopId, today, cancellationToken);
    }
}

internal sealed class ListLoyaltyAddOnPricesHandler(TrimmeDbContext db, IPlatformSettings settings, TimeProvider clock)
    : IQueryHandler<ListLoyaltyAddOnPricesQuery, LoyaltyAddOnPricesResponse>
{
    public async Task<LoyaltyAddOnPricesResponse> Handle(ListLoyaltyAddOnPricesQuery query, CancellationToken cancellationToken) =>
        LoyaltyAddOnPrices.Response(
            await db.Set<LoyaltyAddOnPrice>().AsNoTracking().ToListAsync(cancellationToken),
            (await settings.GetAsync(cancellationToken)).LocalDate(clock.GetUtcNow()));
}

/// <summary>SuperAdmin adds a price version from a date (today or later); versions are never changed. Audited.</summary>
internal sealed class AddLoyaltyAddOnPriceHandler(TrimmeDbContext db, IPlatformSettings settings, ICurrentUser user, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<AddLoyaltyAddOnPriceCommand, Result<LoyaltyAddOnPricesResponse>>
{
    public async Task<Result<LoyaltyAddOnPricesResponse>> Handle(AddLoyaltyAddOnPriceCommand command, CancellationToken cancellationToken)
    {
        var platform = await settings.GetAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var today = platform.LocalDate(now);
        var prices = await db.Set<LoyaltyAddOnPrice>().AsNoTracking().ToListAsync(cancellationToken);
        var added = LoyaltyAddOnPrice.Add(prices, command.Amount, platform.Currency, platform.Currency, command.EffectiveFrom, today, user.UserId, now);
        if (added.IsFailure)
        {
            return added.Error;
        }

        db.Add(added.Value);
        audit.Record(new AuditRecord(
            "loyalty_add_on.price_added", nameof(LoyaltyAddOnPrice), added.Value.Id.ToString(), null,
            string.Create(CultureInfo.InvariantCulture, $"v{added.Value.VersionNumber}: {added.Value.Amount:0.00} {added.Value.Currency} from {added.Value.EffectiveFrom:yyyy-MM-dd}")));
        await db.SaveChangesAsync(cancellationToken);
        return LoyaltyAddOnPrices.Response([.. prices, added.Value], today);
    }
}

internal static class LoyaltyAddOnPrices
{
    public static LoyaltyAddOnPricesResponse Response(IReadOnlyCollection<LoyaltyAddOnPrice> prices, DateOnly today) =>
        new(
            LoyaltyAddOnPrice.On(prices, today) is { } current ? SubscriptionExtras.Price(current) : null,
            prices.Where(p => p.EffectiveFrom > today).OrderBy(p => p.EffectiveFrom).FirstOrDefault() is { } next ? SubscriptionExtras.Price(next) : null,
            [.. prices.OrderByDescending(p => p.EffectiveFrom).Select(SubscriptionExtras.Price)]);
}
