using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Application.Admin;

/// <summary>A shop as the platform admin sees it.</summary>
public sealed record AdminShopResponse(
    Guid Id,
    string Slug,
    string NameAr,
    string NameEn,
    string Status,
    string TimeZone,
    bool RequireManualConfirmation,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal sealed record CreateShopCommand(string Slug, string NameAr, string NameEn, string? TimeZone) : ICommand<Result<AdminShopResponse>>;

internal sealed record ListShopsQuery(PageRequest Page, string? Search, ShopStatus? Status) : IQuery<PagedResponse<AdminShopResponse>>;

internal sealed record GetShopQuery(Guid ShopId) : IQuery<AdminShopResponse?>;

internal enum ShopStatusChange
{
    Activate,
    Suspend,
}

internal sealed record ChangeShopStatusCommand(Guid ShopId, ShopStatusChange Change, string? Reason) : ICommand<Result<AdminShopResponse>>;

internal sealed class CreateShopValidator : AbstractValidator<CreateShopCommand>
{
    public CreateShopValidator()
    {
        RuleFor(c => c.Slug).NotEmpty().WithErrorCode("validation.required")
            .Must(s => Shop.IsValidSlug(s.Trim().ToLowerInvariant())).WithErrorCode("validation.slug_invalid");
        RuleFor(c => c.NameAr).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("validation.required")
            .MaximumLength(Shop.MaxNameLength).WithErrorCode("validation.too_long");
        RuleFor(c => c.NameEn).Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("validation.required")
            .MaximumLength(Shop.MaxNameLength).WithErrorCode("validation.too_long");
        RuleFor(c => c.TimeZone)
            .Must(tz => tz is null || TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _)).WithErrorCode("validation.invalid");
    }
}

internal sealed class ChangeShopStatusValidator : AbstractValidator<ChangeShopStatusCommand>
{
    public ChangeShopStatusValidator()
    {
        RuleFor(c => c.Reason).MaximumLength(500).WithErrorCode("validation.too_long");
    }
}

internal sealed class CreateShopHandler(TrimmeDbContext db, TimeProvider clock, IAuditLog audit)
    : ICommandHandler<CreateShopCommand, Result<AdminShopResponse>>
{
    public async Task<Result<AdminShopResponse>> Handle(CreateShopCommand command, CancellationToken cancellationToken)
    {
        var created = Shop.Create(EntityId.New<ShopId>(), command.Slug, command.NameAr, command.NameEn, command.TimeZone, clock.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error;
        }

        var shop = created.Value;
        if (await db.Set<Shop>().AnyAsync(s => s.Slug == shop.Slug, cancellationToken))
        {
            return ShopErrors.SlugTaken();
        }

        db.Add(shop);
        audit.Record(new AuditRecord("shop.created", nameof(Shop), shop.Id.ToString(), shop.Id, $"Created as {shop.Status}"));
        await db.SaveChangesAsync(cancellationToken);
        return ShopMapping.ToAdmin(shop);
    }
}

internal sealed class ListShopsHandler(TrimmeDbContext db) : IQueryHandler<ListShopsQuery, PagedResponse<AdminShopResponse>>
{
    public async Task<PagedResponse<AdminShopResponse>> Handle(ListShopsQuery query, CancellationToken cancellationToken)
    {
        var shops = db.Set<Shop>().AsNoTracking();
        if (query.Status is { } status)
        {
            shops = shops.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            shops = shops.Where(s => EF.Functions.ILike(s.NameAr, term) || EF.Functions.ILike(s.NameEn, term) || EF.Functions.ILike(s.Slug, term));
        }

        var total = await shops.CountAsync(cancellationToken);
        var page = await shops
            .OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id)
            .Skip(query.Page.Skip).Take(query.Page.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<AdminShopResponse>([.. page.Select(ShopMapping.ToAdmin)], query.Page.Page, query.Page.PageSize, total);
    }
}

internal sealed class GetShopHandler(TrimmeDbContext db) : IQueryHandler<GetShopQuery, AdminShopResponse?>
{
    public async Task<AdminShopResponse?> Handle(GetShopQuery query, CancellationToken cancellationToken)
    {
        var id = new ShopId(query.ShopId);
        var shop = await db.Set<Shop>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return shop is null ? null : ShopMapping.ToAdmin(shop);
    }
}

internal sealed class ChangeShopStatusHandler(TrimmeDbContext db, TimeProvider clock, IAuditLog audit)
    : ICommandHandler<ChangeShopStatusCommand, Result<AdminShopResponse>>
{
    public async Task<Result<AdminShopResponse>> Handle(ChangeShopStatusCommand command, CancellationToken cancellationToken)
    {
        var id = new ShopId(command.ShopId);
        var shop = await db.Set<Shop>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shop is null)
        {
            return ShopErrors.NotFound();
        }

        var before = shop.Status;
        var changed = command.Change == ShopStatusChange.Activate ? shop.Activate(clock.GetUtcNow()) : shop.Suspend(clock.GetUtcNow());
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        var action = command.Change == ShopStatusChange.Activate ? "shop.activated" : "shop.suspended";
        audit.Record(new AuditRecord(action, nameof(Shop), shop.Id.ToString(), shop.Id, $"{before} → {shop.Status}", command.Reason?.Trim()));
        await db.SaveChangesAsync(cancellationToken);
        return ShopMapping.ToAdmin(shop);
    }
}

internal static class ShopMapping
{
    public static AdminShopResponse ToAdmin(Shop shop) => new(
        shop.Id.Value,
        shop.Slug,
        shop.NameAr,
        shop.NameEn,
        shop.Status.ToString(),
        shop.TimeZone,
        shop.RequireManualConfirmation,
        shop.CreatedAt,
        shop.UpdatedAt);
}
