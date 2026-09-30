using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.QrAnalytics.Domain;

namespace Trimme.Modules.QrAnalytics.Application.Public;

// The scanned code (c-qr, R-CUS-13, D-114): resolve it to the page it opens and record the visit. Anonymous; the code →
// shop map finds the shop, then the code is read inside that one shop's public scope.

/// <summary>
/// What a scanned code opens. A professional code whose professional is no longer active opens the shop instead, so a
/// sticker on a mirror never leads to a dead page.
/// </summary>
public sealed record QrTargetResponse(string Code, QrTargetType TargetType, string ShopSlug, Guid? ProfessionalId, string? ProfessionalSlug);

internal sealed record ResolveQrCodeQuery(string Code) : IQuery<QrTargetResponse?>;

/// <summary>Records a scan; <c>CurrentVisitId</c> is the visit already in the browser's attribution cookie, if any.</summary>
internal sealed record RecordQrVisitCommand(string Code, Guid? CurrentVisitId, string? IpAddress, string? UserAgent, string? Locale)
    : ICommand<Result<Guid>>;

/// <summary>An active code of an active shop, with its target.</summary>
internal sealed record ResolvedQrCode(QrCodeLink Link, QrTargetResponse Target);

internal sealed class QrCodeResolver(TrimmeDbContext db, IPublicDataScope scope, IShopDirectory shops, IProfessionalDirectory professionals)
{
    public async Task<ResolvedQrCode?> ResolveAsync(string rawCode, CancellationToken cancellationToken)
    {
        if (QrCodeFormat.Normalize(rawCode) is not { } code)
        {
            return null;
        }

        var route = await db.Set<QrCodeRoute>().AsNoTracking().SingleOrDefaultAsync(r => r.Code == code, cancellationToken);
        if (route is null)
        {
            return null;
        }

        ShopSummary? shop;
        using (scope.Begin(shopId: null))
        {
            shop = await shops.FindAsync(route.ShopId, cancellationToken);
        }

        if (shop is not { Status: ShopStatus.Active })
        {
            return null;
        }

        using (scope.BeginMany([shop.Id]))
        {
            var link = await db.Set<QrCodeLink>().AsNoTracking()
                .SingleOrDefaultAsync(l => l.Id == route.LinkId && l.ShopId == shop.Id && l.IsActive, cancellationToken);
            if (link is null)
            {
                return null;
            }

            if (link.ProfessionalId is { } professionalId
                && (await professionals.ListActiveProfilesAsync([shop.Id], cancellationToken)).FirstOrDefault(p => p.Id == professionalId) is { } professional)
            {
                return new ResolvedQrCode(link, new QrTargetResponse(link.Code, QrTargetType.Professional, shop.Slug, professional.Id.Value, professional.Slug));
            }

            return new ResolvedQrCode(link, new QrTargetResponse(link.Code, QrTargetType.Shop, shop.Slug, null, null));
        }
    }
}

internal sealed class ResolveQrCodeHandler(QrCodeResolver resolver) : IQueryHandler<ResolveQrCodeQuery, QrTargetResponse?>
{
    public async Task<QrTargetResponse?> Handle(ResolveQrCodeQuery query, CancellationToken cancellationToken) =>
        (await resolver.ResolveAsync(query.Code, cancellationToken))?.Target;
}

/// <summary>
/// Records one scan without invasive tracking (R-QR-02): the time, the device class, the page language and a keyed hash of
/// the day and the IP address, never the address itself. A reload within the reload window, with the same code's visit
/// still in the cookie, reuses that visit, so refreshing the page does not count as another scan.
/// </summary>
internal sealed class RecordQrVisitHandler(
    TrimmeDbContext db, QrCodeResolver resolver, IPersonalDataProtector protector, IOptions<QrOptions> options, TimeProvider clock)
    : ICommandHandler<RecordQrVisitCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(RecordQrVisitCommand command, CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(command.Code, cancellationToken) is not { } resolved)
        {
            return QrErrors.NotFound();
        }

        var now = clock.GetUtcNow();
        if (command.CurrentVisitId is { } current)
        {
            var currentId = new QrVisitId(current);
            var since = now.AddMinutes(-options.Value.ReloadWindowMinutes);
            if (await db.Set<QrVisit>().AnyAsync(v => v.Id == currentId && v.LinkId == resolved.Link.Id && v.VisitedAt >= since, cancellationToken))
            {
                return current;
            }
        }

        var visit = QrVisit.Record(
            EntityId.New<QrVisitId>(), resolved.Link, now, VisitorHash(command.IpAddress, now), QrDevices.Classify(command.UserAgent), command.Locale);
        db.Add(visit);
        await db.SaveChangesAsync(cancellationToken);
        return visit.Id.Value;
    }

    /// <summary>The day and the address hashed together with the platform's lookup key, truncated; unlinkable across days.</summary>
    private string VisitorHash(string? ipAddress, DateTimeOffset now) =>
        protector.LookupHash($"{now.UtcDateTime:yyyy-MM-dd}|{ipAddress ?? "unknown"}", PersonalDataPurposes.QrVisitor)[..QrVisit.VisitorHashLength];
}
