using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.QrAnalytics.Domain;

namespace Trimme.Modules.QrAnalytics.Application.Admin;

// QR codes and their analytics for platform admins (R-AD-09, DV-A14, D-114): every shop's codes with filters, creation of
// a shop or professional code, switching a code off and on (audited), the period's scans, bookings and conversion per
// shop, and the PNG, SVG and PDF files. There is no delete: a code's scans and bookings keep their meaning.

public sealed record QrCodeListResponse(IReadOnlyList<QrCodeResponse> Items, int Page, int PageSize, int Total, DateOnly From, DateOnly To);

internal sealed record ListAdminQrCodesQuery(
    Guid? ShopId, QrTargetType? TargetType, bool? Active, string? Search, DateOnly? From, DateOnly? To, int? Days, PageRequest Page)
    : IQuery<Result<QrCodeListResponse>>;

internal sealed record GetAdminQrCodeQuery(Guid CodeId) : IQuery<Result<QrCodeResponse>>;

internal sealed record CreateQrCodeCommand(Guid ShopId, Guid? ProfessionalId, string? Label) : ICommand<Result<QrCodeResponse>>;

internal sealed record SetQrCodeActiveCommand(Guid CodeId, bool Active, uint Version) : ICommand<Result<QrCodeResponse>>;

internal sealed record GetQrAnalyticsQuery(DateOnly? From, DateOnly? To, int? Days, Guid? ShopId) : IQuery<Result<QrAnalyticsResponse>>;

internal sealed record GetAdminQrImageQuery(Guid CodeId, string? Format, int? Size) : IQuery<Result<QrImageFile>>;

internal sealed class ListAdminQrCodesHandler(TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops, QrReadModel read)
    : IQueryHandler<ListAdminQrCodesQuery, Result<QrCodeListResponse>>
{
    public async Task<Result<QrCodeListResponse>> Handle(ListAdminQrCodesQuery query, CancellationToken cancellationToken)
    {
        if (await read.PeriodAsync(query.From, query.To, cancellationToken, query.Days) is not { } period)
        {
            return QrErrors.InvalidPeriod();
        }

        using var _ = scope.Begin();
        var codes = db.Set<QrCodeLink>().AsNoTracking();
        if (query.ShopId is { } shop)
        {
            var shopId = new ShopId(shop);
            codes = codes.Where(l => l.ShopId == shopId);
        }

        if (query.TargetType is { } target)
        {
            codes = codes.Where(l => l.TargetType == target);
        }

        if (query.Active is { } active)
        {
            codes = codes.Where(l => l.IsActive == active);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var matchingShops = (await shops.SearchIdsAsync(term, 50, cancellationToken)).ToArray();
            var lower = term.ToLowerInvariant();
            codes = codes.Where(l => l.Code == lower || (l.Label != null && EF.Functions.ILike(l.Label, $"%{term}%")) || matchingShops.Contains(l.ShopId));
        }

        var total = await codes.CountAsync(cancellationToken);
        var page = await codes.OrderByDescending(l => l.IsActive).ThenByDescending(l => l.CreatedAt).ThenBy(l => l.Id)
            .Skip(query.Page.Skip).Take(query.Page.PageSize).ToListAsync(cancellationToken);
        return new QrCodeListResponse(await read.MapAsync(page, period, cancellationToken), query.Page.Page, query.Page.PageSize, total, period.From, period.To);
    }
}

internal sealed class GetAdminQrCodeHandler(TrimmeDbContext db, IAdminDataScope scope, QrReadModel read)
    : IQueryHandler<GetAdminQrCodeQuery, Result<QrCodeResponse>>
{
    public async Task<Result<QrCodeResponse>> Handle(GetAdminQrCodeQuery query, CancellationToken cancellationToken)
    {
        var period = (await read.PeriodAsync(null, null, cancellationToken))!;
        using var _ = scope.Begin();
        var id = new QrCodeLinkId(query.CodeId);
        return await db.Set<QrCodeLink>().AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, cancellationToken) is { } link
            ? (await read.MapAsync([link], period, cancellationToken))[0]
            : QrErrors.NotFound();
    }
}

/// <summary>
/// A new code for a shop, or for one of its active professionals (R-QR-01). The short code is random and unique; the
/// database's unique index is the final word, so a collision (astronomically rare) is retried with a fresh code.
/// </summary>
internal sealed class CreateQrCodeHandler(
    TrimmeDbContext db, IAdminDataScope scope, IShopDirectory shops, IProfessionalDirectory professionals, ICurrentUser user, IAuditLog audit,
    QrReadModel read, TimeProvider clock)
    : ICommandHandler<CreateQrCodeCommand, Result<QrCodeResponse>>
{
    private const int Attempts = 5;

    public async Task<Result<QrCodeResponse>> Handle(CreateQrCodeCommand command, CancellationToken cancellationToken)
    {
        var period = (await read.PeriodAsync(null, null, cancellationToken))!;
        using var _ = scope.Begin();
        var shopId = new ShopId(command.ShopId);
        if (await shops.FindAsync(shopId, cancellationToken) is null)
        {
            return QrErrors.ShopNotFound();
        }

        ProfessionalId? professionalId = null;
        if (command.ProfessionalId is { } requested)
        {
            // Only an active professional of this very shop (no cross-shop reference; the database checks it again).
            if (await professionals.FindAsync(new ProfessionalId(requested), cancellationToken) is not { IsActive: true } professional
                || professional.ShopId != shopId)
            {
                return QrErrors.ProfessionalNotFound();
            }

            professionalId = professional.Id;
        }

        for (var attempt = 1; ; attempt++)
        {
            var code = QrCodeFormat.NewCode();
            if (await db.Set<QrCodeRoute>().AnyAsync(r => r.Code == code, cancellationToken) && attempt < Attempts)
            {
                continue;
            }

            var created = QrCodeLink.Create(EntityId.New<QrCodeLinkId>(), shopId, code, professionalId, command.Label, user.UserId, clock.GetUtcNow());
            if (created.IsFailure)
            {
                return created.Error;
            }

            var link = created.Value;
            db.Add(link);
            db.Add(QrCodeRoute.For(link));
            audit.Record(new AuditRecord(
                "qr.created", nameof(QrCodeLink), link.Id.ToString(), shopId,
                professionalId is null ? $"Shop code {code}" : $"Professional code {code} ({professionalId})"));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (attempt < Attempts && DatabaseErrors.IsUniqueViolation(exception))
            {
                db.ChangeTracker.Clear();
                continue;
            }

            return (await read.MapAsync([link], period, cancellationToken))[0];
        }
    }
}

internal sealed class SetQrCodeActiveHandler(TrimmeDbContext db, IAdminDataScope scope, IAuditLog audit, QrReadModel read, TimeProvider clock)
    : ICommandHandler<SetQrCodeActiveCommand, Result<QrCodeResponse>>
{
    public async Task<Result<QrCodeResponse>> Handle(SetQrCodeActiveCommand command, CancellationToken cancellationToken)
    {
        var period = (await read.PeriodAsync(null, null, cancellationToken))!;
        using var _ = scope.Begin();
        var id = new QrCodeLinkId(command.CodeId);
        if (await db.Set<QrCodeLink>().SingleOrDefaultAsync(l => l.Id == id, cancellationToken) is not { } link)
        {
            return QrErrors.NotFound();
        }

        db.Entry(link).Property(l => l.Version).OriginalValue = command.Version;
        var result = command.Active ? link.Activate() : link.Deactivate(clock.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error;
        }

        audit.Record(new AuditRecord(command.Active ? "qr.activated" : "qr.deactivated", nameof(QrCodeLink), link.Id.ToString(), link.ShopId, $"Code {link.Code}"));
        await db.SaveChangesAsync(cancellationToken);
        return (await read.MapAsync([link], period, cancellationToken))[0];
    }
}

internal sealed class GetQrAnalyticsHandler(IAdminDataScope scope, QrReadModel read) : IQueryHandler<GetQrAnalyticsQuery, Result<QrAnalyticsResponse>>
{
    public async Task<Result<QrAnalyticsResponse>> Handle(GetQrAnalyticsQuery query, CancellationToken cancellationToken)
    {
        if (await read.PeriodAsync(query.From, query.To, cancellationToken, query.Days) is not { } period)
        {
            return QrErrors.InvalidPeriod();
        }

        using var _ = scope.Begin();
        var (totals, byShop) = await read.FiguresAsync(period, query.ShopId is { } shop ? new ShopId(shop) : null, null, cancellationToken);
        return new QrAnalyticsResponse(period.From, period.To, read.AttributionDays, totals, byShop);
    }
}

internal sealed class GetAdminQrImageHandler(TrimmeDbContext db, IAdminDataScope scope, QrReadModel read)
    : IQueryHandler<GetAdminQrImageQuery, Result<QrImageFile>>
{
    public async Task<Result<QrImageFile>> Handle(GetAdminQrImageQuery query, CancellationToken cancellationToken)
    {
        if (!QrImages.TryParseFormat(query.Format, out var format))
        {
            return QrErrors.InvalidFormat();
        }

        using var _ = scope.Begin();
        var id = new QrCodeLinkId(query.CodeId);
        var code = await db.Set<QrCodeLink>().AsNoTracking().Where(l => l.Id == id).Select(l => l.Code).SingleOrDefaultAsync(cancellationToken);
        return code is null ? QrErrors.NotFound() : QrImages.Render(read.UrlFor(code), code, format, query.Size);
    }
}
