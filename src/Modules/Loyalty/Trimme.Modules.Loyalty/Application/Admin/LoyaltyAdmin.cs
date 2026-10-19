using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.Modules.Loyalty.Application.Admin;

/// <summary>A shop's programme as the admin edits it (null before the first save; send <c>Version</c> back) and its use.</summary>
public sealed record AdminShopLoyaltyResponse(LoyaltyProgramResponse? Program, uint? Version, LoyaltyStatsResponse Stats);

internal sealed record GetAdminShopLoyaltyQuery(Guid ShopId) : IQuery<Result<AdminShopLoyaltyResponse>>;

internal sealed record SaveShopLoyaltyCommand(Guid ShopId, LoyaltyTerms Terms, uint? Version) : ICommand<Result<AdminShopLoyaltyResponse>>;

internal sealed class GetAdminShopLoyaltyHandler(TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops, TimeProvider clock)
    : IQueryHandler<GetAdminShopLoyaltyQuery, Result<AdminShopLoyaltyResponse>>
{
    public async Task<Result<AdminShopLoyaltyResponse>> Handle(GetAdminShopLoyaltyQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var shopId = new ShopId(query.ShopId);
        if (await shops.FindAsync(shopId, cancellationToken) is null)
        {
            return LoyaltyErrors.ShopNotFound();
        }

        var program = await db.Set<LoyaltyProgram>().AsNoTracking().SingleOrDefaultAsync(p => p.ShopId == shopId, cancellationToken);
        return new AdminShopLoyaltyResponse(
            program is null ? null : LoyaltyMapping.Program(program), program?.Version,
            await LoyaltyStats.ForShopAsync(db, shopId, clock.GetUtcNow(), cancellationToken));
    }
}

/// <summary>
/// Creates or replaces a shop's loyalty terms (D-132): the platform agrees them with the shop, so only admins with
/// <c>Admin.Loyalty.Manage</c> set them. Versioned (a stale edit is 409) and audited. Stamps and rewards already earned
/// keep their terms; the new ones apply from the next completed booking.
/// </summary>
internal sealed class SaveShopLoyaltyHandler(TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<SaveShopLoyaltyCommand, Result<AdminShopLoyaltyResponse>>
{
    public async Task<Result<AdminShopLoyaltyResponse>> Handle(SaveShopLoyaltyCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var shopId = new ShopId(command.ShopId);
        if (await shops.FindAsync(shopId, cancellationToken) is null)
        {
            return LoyaltyErrors.ShopNotFound();
        }

        var now = clock.GetUtcNow();
        var program = await db.Set<LoyaltyProgram>().SingleOrDefaultAsync(p => p.ShopId == shopId, cancellationToken);
        if (program is null)
        {
            var created = LoyaltyProgram.Create(EntityId.New<LoyaltyProgramId>(), shopId, command.Terms, now);
            if (created.IsFailure)
            {
                return created.Error;
            }

            program = created.Value;
            db.Add(program);
        }
        else
        {
            if (command.Version is { } version)
            {
                db.Entry(program).Property(p => p.Version).OriginalValue = version;
            }

            var updated = program.Update(command.Terms, now);
            if (updated.IsFailure)
            {
                return updated.Error;
            }
        }

        audit.Record(new AuditRecord(
            "loyalty.program_saved", nameof(LoyaltyProgram), program.Id.ToString(), shopId,
            $"{(program.IsActive ? "active" : "inactive")}: {program.RequiredVisits} visits → {program.RewardKind}{(program.DiscountPercent is { } p ? $" {p}%" : string.Empty)}"));
        await db.SaveChangesAsync(cancellationToken);
        return new AdminShopLoyaltyResponse(LoyaltyMapping.Program(program), program.Version, await LoyaltyStats.ForShopAsync(db, shopId, now, cancellationToken));
    }
}
