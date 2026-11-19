using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Qr;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.QrAnalytics.Domain;

namespace Trimme.Modules.QrAnalytics.Application;

/// <summary>
/// <see cref="IProfessionalQrCodes"/> (D-155, the client's 7-2-3): a barber gets their own code without anyone asking. A
/// professional who already has one (turned off by an admin or not) keeps it. The new code is checked against the
/// stored ones; the unique index has the last word (a clash fails the caller's save, which the 31-bit alphabet makes
/// vanishingly rare). Audited as the admin's own «qr.created».
/// </summary>
internal sealed class ProfessionalQrCodes(TrimmeDbContext db, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IProfessionalQrCodes
{
    private const int Attempts = 5;

    public async Task<bool> EnsureAsync(ShopId shopId, ProfessionalId professionalId, CancellationToken cancellationToken)
    {
        if (db.Set<QrCodeLink>().Local.Any(l => l.ProfessionalId == professionalId)
            || await db.Set<QrCodeLink>().AnyAsync(l => l.ProfessionalId == professionalId, cancellationToken))
        {
            return false;
        }

        var code = QrCodeFormat.NewCode();
        for (var attempt = 1; attempt < Attempts && (db.Set<QrCodeRoute>().Local.Any(r => r.Code == code)
                                                     || await db.Set<QrCodeRoute>().AnyAsync(r => r.Code == code, cancellationToken)); attempt++)
        {
            code = QrCodeFormat.NewCode();
        }

        var link = QrCodeLink.Create(EntityId.New<QrCodeLinkId>(), shopId, code, professionalId, label: null, user.UserId, clock.GetUtcNow()).Value;
        db.Add(link);
        db.Add(QrCodeRoute.For(link));
        audit.Record(new AuditRecord(
            "qr.created", nameof(QrCodeLink), link.Id.ToString(), shopId, $"Professional code {code} ({professionalId}), made with the barber",
            SummaryArgs: AuditArgs.Of(("kind", "professional"), ("code", code), ("automatic", true))));
        return true;
    }
}
