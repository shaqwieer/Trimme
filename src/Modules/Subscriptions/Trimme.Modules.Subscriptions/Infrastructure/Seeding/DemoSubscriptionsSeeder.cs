using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Subscriptions.Domain;

namespace Trimme.Modules.Subscriptions.Infrastructure.Seeding;

/// <summary>
/// Demo plans with versioned prices and one subscription per status (spec §20): Al Asala Active (two annual periods,
/// charged at the old and the new price), Barber House ExpiringSoon, Lamsat Al Rajul Expired, Al Madina Suspended.
/// Plan and price rows are fixed; subscription dates are relative to the day the seed runs, so the statuses hold on a
/// fresh database but drift in a long-lived one (re-create the volume to refresh them). Development only.
/// </summary>
internal sealed class DemoSubscriptionsSeeder : IDevSeeder
{
    private static readonly DateTimeOffset SeededAt = new(2026, 9, 1, 11, 0, 0, TimeSpan.Zero);

    private static readonly DemoPlan Monthly = new(
        Guid.Parse("0199a0de-5a10-7000-8000-000000000601"), "شهري", "Monthly", "للمحلات الجديدة التي تبدأ بالتجربة", "For new shops starting out",
        BillingIntervalUnit.Month, 1, 3, [(Guid.Parse("0199a0de-5a10-7000-8000-000000000611"), 199m, new DateOnly(2025, 1, 1))]);

    private static readonly DemoPlan SemiAnnual = new(
        Guid.Parse("0199a0de-5a10-7000-8000-000000000602"), "نصف سنوي", "Semi-annual", null, null,
        BillingIntervalUnit.Month, 6, 6, [(Guid.Parse("0199a0de-5a10-7000-8000-000000000621"), 1100m, new DateOnly(2024, 1, 1))]);

    private static readonly DemoPlan Annual = new(
        Guid.Parse("0199a0de-5a10-7000-8000-000000000603"), "سنوي", "Annual", "أفضل قيمة للمحلات القائمة", "Best value for established shops",
        BillingIntervalUnit.Month, 12, null,
        [
            (Guid.Parse("0199a0de-5a10-7000-8000-000000000631"), 1900m, new DateOnly(2023, 1, 1)),
            (Guid.Parse("0199a0de-5a10-7000-8000-000000000632"), 2400m, new DateOnly(2025, 1, 1)),
        ]);

    /// <summary>After the demo shops (200) and catalogue (300).</summary>
    public int Order => 350;

    public string Name => "subscriptions-demo";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TrimmeDbContext>();
        using var scope = services.GetRequiredService<ISystemDataScope>().Begin();
        var platform = await services.GetRequiredService<IPlatformSettings>().GetAsync(cancellationToken);
        var today = platform.LocalDate(services.GetRequiredService<TimeProvider>().GetUtcNow());

        var plans = new Dictionary<Guid, SubscriptionPlan>();
        var order = 1;
        foreach (var demo in new[] { Monthly, SemiAnnual, Annual })
        {
            var plan = await db.Set<SubscriptionPlan>().Include(p => p.Prices).SingleOrDefaultAsync(p => p.Id == new SubscriptionPlanId(demo.Id), cancellationToken);
            if (plan is null)
            {
                plan = SubscriptionPlan.Create(new SubscriptionPlanId(demo.Id), demo.Details, order, SeededAt).Value;
                foreach (var (id, amount, from) in demo.Prices)
                {
                    // Seeded history: each version was "added" on its own start date.
                    plan.AddPrice(new PlanPriceId(id), amount, platform.Currency, platform.Currency, from, from, null, SeededAt);
                }

                plan.Publish(SeededAt);
                db.Add(plan);
            }

            plans[demo.Id] = plan;
            order++;
        }

        await db.SaveChangesAsync(cancellationToken);

        // Al Asala: last year's period at the old annual price, then the current one (~65 days left) → Active.
        var asalaStart = today.AddDays(-300).AddMonths(-12);
        await SeedAsync(db, DemoData.AlAsala.Id, plans[Annual.Id], asalaStart, today, renewals: 1, suspendReason: null, cancellationToken);

        // Barber House: a semi-annual period ending in about a week → ExpiringSoon.
        await SeedAsync(db, DemoData.BarberHouse.Id, plans[SemiAnnual.Id], today.AddDays(9).AddMonths(-6), today, renewals: 0, suspendReason: null, cancellationToken);

        // Lamsat Al Rajul: a monthly period that ended about two weeks ago → Expired (hidden from discovery by D-014).
        await SeedAsync(db, DemoData.LamsatAlRajul.Id, plans[Monthly.Id], today.AddDays(-45), today, renewals: 0, suspendReason: null, cancellationToken);

        // Al Madina: covered, but suspended by the platform → Suspended.
        await SeedAsync(db, DemoData.AlMadina.Id, plans[Annual.Id], today.AddDays(-100), today, renewals: 0, "بانتظار مراجعة العقد", cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedAsync(
        TrimmeDbContext db, ShopId shopId, SubscriptionPlan plan, DateOnly start, DateOnly today, int renewals, string? suspendReason, CancellationToken cancellationToken)
    {
        if (await db.Set<ShopSubscription>().AnyAsync(s => s.ShopId == shopId, cancellationToken))
        {
            return;
        }

        var subscription = ShopSubscription.Assign(
            EntityId.New<ShopSubscriptionId>(), shopId, plan, start, plan.PeriodEnd(start), Snapshot(plan, start), null, null, today, SeededAt).Value;
        for (var i = 0; i < renewals; i++)
        {
            var next = subscription.EndDate.AddDays(1);
            subscription.Renew(plan, next, plan.PeriodEnd(next), Snapshot(plan, next), null, null, today, SeededAt);
        }

        if (suspendReason is not null)
        {
            subscription.Suspend(suspendReason, SeededAt);
        }

        db.Add(subscription);
        var coverage = new SubscriptionCoverage(shopId);
        coverage.CopyFrom(subscription);
        db.Add(coverage);
    }

    private static PriceSnapshot Snapshot(SubscriptionPlan plan, DateOnly start) =>
        plan.PriceOn(start) is { } price ? new PriceSnapshot(price.Id, price.Amount, price.Currency) : throw new InvalidOperationException("Demo plan has no price.");

    private sealed record DemoPlan(
        Guid Id, string NameAr, string NameEn, string? DescriptionAr, string? DescriptionEn, BillingIntervalUnit Unit, int Count, int? MaxProfessionals,
        (Guid Id, decimal Amount, DateOnly From)[] Prices)
    {
        public PlanDetails Details => new(
            NameAr, NameEn, DescriptionAr, DescriptionEn,
            [PlanFeature.Of("ظهور في البحث والخريطة", "Listed in search and on the map"), PlanFeature.Of("حجوزات أونلاين غير محدودة", "Unlimited online bookings")],
            MaxProfessionals, null, Unit, Count, null, 7, AvailableToNewShops: true);
    }
}
