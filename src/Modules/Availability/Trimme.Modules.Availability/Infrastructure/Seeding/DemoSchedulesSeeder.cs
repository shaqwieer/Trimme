using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Availability.Domain;
using Trimme.Modules.Availability.Domain.Engine;

namespace Trimme.Modules.Availability.Infrastructure.Seeding;

/// <summary>
/// Opening hours, a professional's own hours, prayer and lunch breaks, a closure and time off for the demo shops
/// (spec §20; s-hours sample data). Idempotent by fixed ids; development only. Dates of the closure and time off are
/// relative to the day the seed first runs, so the demo always shows one "in force now" and one scheduled entry.
/// </summary>
internal sealed class DemoSchedulesSeeder : IDevSeeder
{
    private const int Hour = 60;

    private static readonly DayOfWeek[] EveryDay = Enum.GetValues<DayOfWeek>();

    private static readonly DayOfWeek[] SundayToThursday = [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday];

    /// <summary>Al Asala as designed: Sun–Wed 9 AM–11 PM, Thu 9 AM–midnight, Fri 2 PM–midnight, closed Saturday.</summary>
    private static readonly WeeklyInterval[] AlAsalaHours =
    [
        .. new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday }.Select(d => new WeeklyInterval(d, 9 * Hour, 23 * Hour)),
        new(DayOfWeek.Thursday, 9 * Hour, 24 * Hour),
        new(DayOfWeek.Friday, 14 * Hour, 24 * Hour),
    ];

    /// <summary>Barber House: Sat–Wed 10 AM–10 PM, Thu 10 AM–1 AM (past midnight), Fri 4 PM–11 PM.</summary>
    private static readonly WeeklyInterval[] BarberHouseHours =
    [
        .. new[] { DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday }.Select(d => new WeeklyInterval(d, 10 * Hour, 22 * Hour)),
        new(DayOfWeek.Thursday, 10 * Hour, 25 * Hour),
        new(DayOfWeek.Friday, 16 * Hour, 23 * Hour),
    ];

    private static readonly (Guid Id, ShopId Shop, string Label, DayOfWeek[] Days, int Start, int End)[] Breaks =
    [
        (Guid.Parse("0199a0de-5a10-7000-8000-000000000701"), DemoData.AlAsala.Id, "استراحة صلاة العصر", EveryDay, (15 * Hour) + 30, 16 * Hour),
        (Guid.Parse("0199a0de-5a10-7000-8000-000000000702"), DemoData.AlAsala.Id, "استراحة الغداء", SundayToThursday, 13 * Hour, (13 * Hour) + 45),
        (Guid.Parse("0199a0de-5a10-7000-8000-000000000703"), DemoData.AlAsala.Id, "صلاة المغرب", EveryDay, (18 * Hour) + 5, (18 * Hour) + 25),
        (Guid.Parse("0199a0de-5a10-7000-8000-000000000711"), DemoData.BarberHouse.Id, "صلاة المغرب", EveryDay, (18 * Hour) + 10, (18 * Hour) + 30),
    ];

    private static readonly Guid SultanId = Guid.Parse("0199a0de-5a10-7000-8000-000000000102");
    private static readonly Guid RakanId = Guid.Parse("0199a0de-5a10-7000-8000-000000000103");
    private static readonly Guid MajedId = Guid.Parse("0199a0de-5a10-7000-8000-000000000202");

    /// <summary>After the demo professionals (250) and services (300).</summary>
    public int Order => 320;

    public string Name => "schedules-demo";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TrimmeDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        using var scope = services.GetRequiredService<ISystemDataScope>().Begin();

        await OpeningHoursAsync(db, DemoData.AlAsala.Id, Guid.Parse("0199a0de-5a10-7000-8000-000000000601"), AlAsalaHours, now, cancellationToken);
        await OpeningHoursAsync(db, DemoData.BarberHouse.Id, Guid.Parse("0199a0de-5a10-7000-8000-000000000611"), BarberHouseHours, now, cancellationToken);

        // Sultan starts later than the shop opens: Sun–Thu noon to 10 PM.
        var sultanHours = new WorkingHoursId(Guid.Parse("0199a0de-5a10-7000-8000-000000000621"));
        if (!await db.Set<ProfessionalWorkingHours>().AnyAsync(h => h.Id == sultanHours, cancellationToken))
        {
            db.Add(ProfessionalWorkingHours.Create(
                sultanHours, DemoData.AlAsala.Id, new ProfessionalId(SultanId), followsShopHours: false,
                [.. SundayToThursday.Select(d => new WeeklyInterval(d, 12 * Hour, 22 * Hour))], now).Value);
        }

        foreach (var (id, shop, label, days, start, end) in Breaks)
        {
            if (!await db.Set<ScheduleBreak>().AnyAsync(b => b.Id == new ScheduleBreakId(id), cancellationToken))
            {
                db.Add(ScheduleBreak.Create(new ScheduleBreakId(id), shop, null, label, days, null, start, end, DateOnly.MinValue, now).Value);
            }
        }

        var clock = new ShopClock(TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh"));
        var today = clock.Date(now);

        // National Day (23 September): the next one from today.
        var closureId = new ShopClosureId(Guid.Parse("0199a0de-5a10-7000-8000-000000000631"));
        if (!await db.Set<ShopClosure>().AnyAsync(c => c.Id == closureId, cancellationToken))
        {
            var nationalDay = new DateOnly(today.Year, 9, 23);
            nationalDay = nationalDay < today ? nationalDay.AddYears(1) : nationalDay;
            db.Add(ShopClosure.Create(closureId, DemoData.AlAsala.Id, nationalDay, nationalDay, "اليوم الوطني", today, now).Value);
        }

        // Majed is on vacation now; Rakan has leave coming up.
        await TimeOffAsync(db, Guid.Parse("0199a0de-5a10-7000-8000-000000000641"), DemoData.BarberHouse.Id, MajedId, TimeOffKind.Vacation,
            new InstantRange(clock.StartOfDay(today.AddDays(-2)), clock.StartOfDay(today.AddDays(6))), "إجازة سنوية", now, cancellationToken);
        await TimeOffAsync(db, Guid.Parse("0199a0de-5a10-7000-8000-000000000642"), DemoData.AlAsala.Id, RakanId, TimeOffKind.Vacation,
            new InstantRange(clock.StartOfDay(today.AddDays(10)), clock.StartOfDay(today.AddDays(14))), null, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task OpeningHoursAsync(TrimmeDbContext db, ShopId shop, Guid id, WeeklyInterval[] hours, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!await db.Set<ShopOpeningHours>().AnyAsync(h => h.ShopId == shop, cancellationToken))
        {
            db.Add(ShopOpeningHours.Create(new OpeningHoursId(id), shop, hours, now).Value);
        }
    }

    private static async Task TimeOffAsync(
        TrimmeDbContext db, Guid id, ShopId shop, Guid professional, TimeOffKind kind, InstantRange span, string? note, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!await db.Set<ProfessionalTimeOff>().AnyAsync(t => t.Id == new TimeOffId(id), cancellationToken))
        {
            db.Add(ProfessionalTimeOff.Create(new TimeOffId(id), shop, new ProfessionalId(professional), kind, span, allDay: true, note, now).Value);
        }
    }
}
