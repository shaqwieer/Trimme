using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Bookings.Application;
using Trimme.Modules.Bookings.Domain;

namespace Trimme.Modules.Bookings.Infrastructure.Seeding;

/// <summary>
/// Sample appointments (spec §20): completed, no-show and cancelled history, and upcoming bookings placed on real free
/// slots found by the availability rules, so they are valid. Faisal gets none (the schedule E2E asserts his exact slots).
/// Idempotent by fixed ids; development only; no outbox rows, so seeding never sends messages.
/// </summary>
internal sealed class DemoBookingsSeeder : IDevSeeder
{
    private static readonly ProfessionalId Sultan = new(Guid.Parse("0199a0de-5a10-7000-8000-000000000102"));
    private static readonly ProfessionalId Rakan = new(Guid.Parse("0199a0de-5a10-7000-8000-000000000103"));
    private static readonly ProfessionalId Omar = new(Guid.Parse("0199a0de-5a10-7000-8000-000000000201"));
    private static readonly Guid Haircut = Guid.Parse("0199a0de-5a10-7000-8000-000000000401");
    private static readonly Guid BeardTrim = Guid.Parse("0199a0de-5a10-7000-8000-000000000402");
    private static readonly Guid CutAndStyle = Guid.Parse("0199a0de-5a10-7000-8000-000000000411");
    private static readonly Guid KidsCut = Guid.Parse("0199a0de-5a10-7000-8000-000000000413");

    /// <summary>After customers (220), schedules (320) and subscriptions (350).</summary>
    public int Order => 400;

    public string Name => "bookings-demo";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TrimmeDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        using var scope = services.GetRequiredService<ISystemDataScope>().Begin();
        var catalog = services.GetRequiredService<IBookableOfferCatalog>();
        var professionals = services.GetRequiredService<IProfessionalDirectory>();
        var availability = services.GetRequiredService<IAvailabilityChecker>();
        var riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, riyadh).DateTime);
        DateTimeOffset At(DateOnly day, int hour, int minute = 0) => new(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(3));

        async Task<(BookedItem Item, BookedProfessional Professional)?> PartsAsync(ShopId shop, Guid service, ProfessionalId professional)
        {
            var offer = await catalog.FindAsync(shop, service, null, cancellationToken);
            var summary = await professionals.FindAsync(professional, cancellationToken);
            return offer is null || summary is null ? null : (BookingMapping.Snapshot(offer), new BookedProfessional(summary.Id, summary.NameAr, summary.NameEn));
        }

        async Task PastAsync(Guid id, ShopId shop, DemoCustomer customer, Guid service, ProfessionalId professional, DateTimeOffset start, BookingStatus[] path, string? reason)
        {
            if (await db.Set<Booking>().AnyAsync(b => b.Id == new BookingId(id), cancellationToken) || await PartsAsync(shop, service, professional) is not { } parts)
            {
                return;
            }

            db.Add(Booking.Seeded(new BookingId(id), shop, customer.Id, customer.Name, parts.Professional, parts.Item, start, BookingChannel.Online, path, reason, start.AddDays(-2)));
        }

        async Task UpcomingAsync(Guid id, ShopId shop, DemoCustomer customer, Guid service, ProfessionalId professional, DateOnly firstDay, int fromHour)
        {
            if (await db.Set<Booking>().AnyAsync(b => b.Id == new BookingId(id), cancellationToken) || await PartsAsync(shop, service, professional) is not { } parts)
            {
                return;
            }

            // The first free offered start from firstDay (half-hour steps, 10:00–20:00), within a week.
            for (var day = firstDay; day < firstDay.AddDays(7); day = day.AddDays(1))
            {
                for (var minutes = fromHour * 60; minutes <= 20 * 60; minutes += 30)
                {
                    var start = At(day, minutes / 60, minutes % 60);
                    var free = await availability.FreeProfessionalsAsync(shop, [professional], start, parts.Item.DurationMinutes, AvailabilityCheckMode.Online, null, cancellationToken);
                    if (free.Count > 0)
                    {
                        db.Add(Booking.Seeded(
                            new BookingId(id), shop, customer.Id, customer.Name, parts.Professional, parts.Item, start, BookingChannel.Online,
                            [BookingStatus.Confirmed], null, now));
                        return;
                    }
                }
            }
        }

        await PastAsync(Guid.Parse("0199a0de-5a10-7000-8000-000000000a01"), DemoData.AlAsala.Id, DemoCustomers.Noura, Haircut, Rakan,
            At(today.AddDays(-7), 17), [BookingStatus.Confirmed, BookingStatus.Arrived, BookingStatus.Completed], null);
        await PastAsync(Guid.Parse("0199a0de-5a10-7000-8000-000000000a02"), DemoData.BarberHouse.Id, DemoCustomers.Khalid, KidsCut, Omar,
            At(today.AddDays(-3), 18, 30), [BookingStatus.Confirmed, BookingStatus.NoShow], null);
        await PastAsync(Guid.Parse("0199a0de-5a10-7000-8000-000000000a03"), DemoData.AlAsala.Id, DemoCustomers.Khalid, BeardTrim, Sultan,
            At(today.AddDays(4), 20), [BookingStatus.Confirmed, BookingStatus.CancelledByCustomer], "تغيّر موعد السفر");
        await UpcomingAsync(Guid.Parse("0199a0de-5a10-7000-8000-000000000a11"), DemoData.BarberHouse.Id, DemoCustomers.Noura, CutAndStyle, Omar, today.AddDays(1), 11);
        await UpcomingAsync(Guid.Parse("0199a0de-5a10-7000-8000-000000000a12"), DemoData.AlAsala.Id, DemoCustomers.Khalid, BeardTrim, Sultan, today.AddDays(2), 13);

        await db.SaveChangesAsync(cancellationToken);
    }
}
