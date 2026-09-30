using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.QrAnalytics.Domain;

namespace Trimme.Modules.QrAnalytics.Infrastructure.Seeding;

/// <summary>
/// Demo QR codes and scans (spec §20, D-114): the codes of <see cref="DemoQr"/>, a deterministic number of scans per code
/// on each of the 27 days before seeding (the retired code only before it was switched off, 20 days ago), and the scans
/// that three demo bookings are credited to. Before the Bookings seeder (400), whose credited bookings reference the codes.
/// Idempotent by fixed ids; development only.
/// </summary>
internal sealed class DemoQrSeeder : IDevSeeder
{
    public const int Days = 27;
    private const int RetiredDaysAgo = 20;

    /// <summary>After shops (200), professionals and schedules (≤ 350); before bookings (400).</summary>
    public int Order => 380;

    public string Name => "qr-demo";

    /// <summary>Scans of code <paramref name="index"/> (0-based) on the day <paramref name="daysAgo"/> before seeding: 0–4, fixed.</summary>
    public static int ScansOn(int index, int daysAgo) => ((index * 7) + (daysAgo * 3) + (daysAgo * index)) % 5;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TrimmeDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        using var scope = services.GetRequiredService<ISystemDataScope>().Begin();
        var riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, riyadh).DateTime);
        DateTimeOffset At(int daysAgo, int hour, int minute = 0) =>
            new DateTimeOffset(today.AddDays(-daysAgo).ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(3)).ToUniversalTime();

        for (var index = 0; index < DemoQr.Codes.Count; index++)
        {
            var demo = DemoQr.Codes[index];
            var id = new QrCodeLinkId(demo.Id);
            if (await db.Set<QrCodeLink>().AnyAsync(l => l.Id == id, cancellationToken))
            {
                continue;
            }

            var link = QrCodeLink.Create(
                id, demo.ShopId, demo.Code, demo.ProfessionalId is { } p ? new ProfessionalId(p) : null, demo.Label, null, At(Days + 3, 9)).Value;
            if (!demo.IsActive)
            {
                link.Deactivate(At(RetiredDaysAgo, 9));
            }

            db.Add(link);
            db.Add(QrCodeRoute.For(link));
            var lastDay = demo.IsActive ? 1 : RetiredDaysAgo + 1;
            var sequence = 0;
            for (var daysAgo = Days; daysAgo >= lastDay; daysAgo--)
            {
                for (var n = 0; n < ScansOn(index, daysAgo); n++)
                {
                    sequence++;
                    var visitId = new QrVisitId(Guid.Parse($"0199a0de-5a10-7000-8000-0000000c{index + 1:X1}{sequence:X3}"));
                    db.Add(QrVisit.Seeded(
                        visitId, id, demo.ShopId, At(daysAgo, 10 + (n * 3), (7 * n) + index), Hash($"{demo.Code}:{daysAgo}:{n}"),
                        (n + daysAgo) % 4 == 0 ? QrDeviceClass.Desktop : QrDeviceClass.Mobile, (n + index) % 3 == 0 ? "en" : "ar"));
                }
            }
        }

        foreach (var credited in DemoQr.Bookings)
        {
            var visitId = new QrVisitId(credited.VisitId);
            if (await db.Set<QrVisit>().AnyAsync(v => v.Id == visitId, cancellationToken))
            {
                continue;
            }

            var code = DemoQr.Codes.Single(c => c.Id == credited.CodeId);
            db.Add(QrVisit.Seeded(
                visitId, new QrCodeLinkId(code.Id), code.ShopId, At(credited.VisitDaysAgo, credited.VisitHour), Hash($"booking:{credited.BookingId}"),
                QrDeviceClass.Mobile, "ar"));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A stand-in visitor hash for seeded scans (no address was ever involved).</summary>
    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..QrVisit.VisitorHashLength];
}
