using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.QrAnalytics.Domain;

namespace Trimme.Modules.QrAnalytics.Application.Shop;

// The shop's own QR codes (open question 8, D-114): read-only for the owner — the printed files, the A5 poster and the
// codes' own scans and bookings. Shops never create, change or switch codes, and see no other shop's figures: codes come
// through the tenant filter, and scans and bookings only through those codes' ids.

/// <summary>The shop's codes with the period's figures (the last 30 days by default).</summary>
public sealed record ShopQrCodesResponse(IReadOnlyList<QrCodeResponse> Items, QrFigures Totals, DateOnly From, DateOnly To, int AttributionDays);

internal sealed record ListShopQrCodesQuery(DateOnly? From, DateOnly? To) : IQuery<Result<ShopQrCodesResponse>>;

internal sealed record GetShopQrCodeQuery(Guid CodeId) : IQuery<Result<QrCodeResponse>>;

internal sealed record GetShopQrImageQuery(Guid CodeId, string? Format, int? Size) : IQuery<Result<QrImageFile>>;

internal sealed class ListShopQrCodesHandler(TrimmeDbContext db, ICurrentTenant tenant, QrReadModel read)
    : IQueryHandler<ListShopQrCodesQuery, Result<ShopQrCodesResponse>>
{
    public async Task<Result<ShopQrCodesResponse>> Handle(ListShopQrCodesQuery query, CancellationToken cancellationToken)
    {
        if (await read.PeriodAsync(query.From, query.To, cancellationToken) is not { } period)
        {
            return QrErrors.InvalidPeriod();
        }

        if (tenant.ShopId is not { } shopId)
        {
            return QrErrors.NotFound();
        }

        var codes = await db.Set<QrCodeLink>().AsNoTracking().Where(l => l.ShopId == shopId)
            .OrderByDescending(l => l.IsActive).ThenBy(l => l.TargetType).ThenBy(l => l.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        var (totals, _) = await read.FiguresAsync(period, shopId, [.. codes.Select(l => l.Id)], cancellationToken);
        return new ShopQrCodesResponse(await read.MapAsync(codes, period, cancellationToken), totals, period.From, period.To, read.AttributionDays);
    }
}

internal sealed class GetShopQrCodeHandler(TrimmeDbContext db, ICurrentTenant tenant, QrReadModel read)
    : IQueryHandler<GetShopQrCodeQuery, Result<QrCodeResponse>>
{
    public async Task<Result<QrCodeResponse>> Handle(GetShopQrCodeQuery query, CancellationToken cancellationToken)
    {
        var period = (await read.PeriodAsync(null, null, cancellationToken))!;
        var id = new QrCodeLinkId(query.CodeId);

        // The tenant filter hides another shop's code: its id answers 404 like a code that does not exist (R-TEN-06).
        return tenant.ShopId is not null
               && await db.Set<QrCodeLink>().AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, cancellationToken) is { } link
            ? (await read.MapAsync([link], period, cancellationToken))[0]
            : QrErrors.NotFound();
    }
}

internal sealed class GetShopQrImageHandler(TrimmeDbContext db, ICurrentTenant tenant, QrReadModel read)
    : IQueryHandler<GetShopQrImageQuery, Result<QrImageFile>>
{
    public async Task<Result<QrImageFile>> Handle(GetShopQrImageQuery query, CancellationToken cancellationToken)
    {
        if (!QrImages.TryParseFormat(query.Format, out var format))
        {
            return QrErrors.InvalidFormat();
        }

        if (tenant.ShopId is null)
        {
            return QrErrors.NotFound();
        }

        var id = new QrCodeLinkId(query.CodeId);
        var code = await db.Set<QrCodeLink>().AsNoTracking().Where(l => l.Id == id).Select(l => l.Code).SingleOrDefaultAsync(cancellationToken);
        return code is null ? QrErrors.NotFound() : QrImages.Render(read.UrlFor(code), code, format, query.Size);
    }
}
