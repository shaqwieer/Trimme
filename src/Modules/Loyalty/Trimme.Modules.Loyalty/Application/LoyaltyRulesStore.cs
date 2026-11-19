using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Loyalty;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Application;

/// <summary>
/// Reads and saves one shop's loyalty rules (D-149) through the caller's scope: the owner's tenant, or the admin's scope.
/// A free-service reward must name one of the shop's own published services. Versioned (a stale save is 409) and audited.
/// </summary>
internal sealed class LoyaltyRulesStore(TrimmeDbContext db, IBookableOfferCatalog catalog, ILoyaltyAddOnStatus addOn, IAuditLog audit, TimeProvider clock)
{
    public async Task<LoyaltyRulesEditor> GetAsync(ShopId shopId, CancellationToken cancellationToken)
    {
        var settings = await db.Set<LoyaltySettings>().AsNoTracking().SingleOrDefaultAsync(s => s.ShopId == shopId, cancellationToken);
        return new LoyaltyRulesEditor(await addOn.IsActiveAsync(shopId, cancellationToken), settings is null ? null : LoyaltyMapping.Rules(settings), settings?.Version);
    }

    public async Task<Result<LoyaltyRulesEditor>> SaveAsync(ShopId shopId, LoyaltyRules rules, uint? version, string by, CancellationToken cancellationToken)
    {
        if (LoyaltySettings.Validate(rules) is { } invalid)
        {
            return invalid;
        }

        (string Ar, string? En)? names = null;
        if (rules.RewardType == LoyaltyRewardType.FreeService)
        {
            if (await catalog.FindAsync(shopId, rules.RewardServiceId, null, cancellationToken) is not { IsPackage: false } service)
            {
                return LoyaltyPointErrors.InvalidService();
            }

            names = (service.NameAr, service.NameEn);
        }

        var now = clock.GetUtcNow();
        var settings = await db.Set<LoyaltySettings>().SingleOrDefaultAsync(s => s.ShopId == shopId, cancellationToken);
        if (settings is null)
        {
            var created = LoyaltySettings.Create(EntityId.New<LoyaltySettingsId>(), shopId, rules, names, now);
            if (created.IsFailure)
            {
                return created.Error;
            }

            settings = created.Value;
            db.Add(settings);
        }
        else
        {
            if (version is { } expected)
            {
                db.Entry(settings).Property(s => s.Version).OriginalValue = expected;
            }

            var updated = settings.Update(rules, names, now);
            if (updated.IsFailure)
            {
                return updated.Error;
            }
        }

        audit.Record(new AuditRecord(
            "loyalty.rules_saved", nameof(LoyaltySettings), settings.Id.ToString(), shopId,
            string.Create(CultureInfo.InvariantCulture,
                $"By {by}: first visit {settings.FirstVisitPoints}, then {settings.VisitPoints}; reward every {settings.PointsForReward} → {settings.RewardType}{(settings.RewardAmount is { } a ? $" {a:0.##}" : string.Empty)}; without visits {settings.InactivityRule}{(settings.InactivityDays is { } d ? $" {d} days" : string.Empty)}")));
        await db.SaveChangesAsync(cancellationToken);
        return new LoyaltyRulesEditor(await addOn.IsActiveAsync(shopId, cancellationToken), LoyaltyMapping.Rules(settings), settings.Version);
    }
}
