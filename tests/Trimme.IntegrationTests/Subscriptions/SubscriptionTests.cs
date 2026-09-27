using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.IntegrationTests.Tenancy;
using Trimme.Modules.Administration.Domain;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Subscriptions.Domain;

namespace Trimme.IntegrationTests.Subscriptions;

/// <summary>
/// SuperAdmin plans with versioned prices, shop subscriptions (assign, renew, override, suspend) with their history,
/// the status calculator on the platform calendar, the D-014 bookability gate and the platform settings
/// (R-SUB-01..05, R-AUTH-10, R-TEN-08).
/// </summary>
public sealed class SubscriptionTests(PostgresFixture postgres)
{
    /// <summary>
    /// The fake clock starts at the real "now": the API validates tokens on the fake clock, while the test cookie jar
    /// drops cookies that expired in real time, so the clock may only move forward from here.
    /// </summary>
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow;

    /// <summary>The platform (Asia/Riyadh) calendar day at <see cref="Start"/>.</summary>
    private static readonly DateOnly Today =
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Start, TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh")).DateTime);

    private static DateOnly YearEnd(DateOnly start) => SubscriptionDates.End(start, BillingIntervalUnit.Month, 12);

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static object Plan(string nameEn, string unit = "Month", int count = 12, decimal? initialPrice = 1900m, bool available = true, uint? version = null)
    {
        var features = new[] { new { ar = "ظهور في البحث", en = "Listed in search" } };
        return version is { } v
            ? new
            {
                nameAr = $"باقة {nameEn}", nameEn, descriptionAr = (string?)null, descriptionEn = (string?)null, features, maxProfessionals = 6,
                maxServices = (int?)null, intervalUnit = unit, intervalCount = count, trialDays = (int?)null, graceDays = 7, availableToNewShops = available, version = v,
            }
            : new
            {
                nameAr = $"باقة {nameEn}", nameEn, descriptionAr = (string?)null, descriptionEn = (string?)null, features, maxProfessionals = 6,
                maxServices = (int?)null, intervalUnit = unit, intervalCount = count, trialDays = (int?)null, graceDays = 7, availableToNewShops = available, initialPrice,
            };
    }

    private static async Task<JsonElement> CreatePublishedPlanAsync(ApiSession superAdmin, string nameEn, CancellationToken ct, string unit = "Month", int count = 12, decimal price = 1900m)
    {
        using var created = await superAdmin.PostAsync("/api/v1/admin/subscription-plans", Plan(nameEn, unit, count, price), ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));
        var id = (await created.JsonAsync(ct)).GetProperty("id").GetGuid();
        using var published = await superAdmin.PostAsync($"/api/v1/admin/subscription-plans/{id}/publish", null, ct);
        published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(ct));
        return await published.JsonAsync(ct);
    }

    private static async Task<JsonElement> OkJsonAsync(Task<HttpResponseMessage> call, CancellationToken ct)
    {
        using var response = await call;
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        return await response.JsonAsync(ct);
    }

    private static async Task ShouldFailAsync(Task<HttpResponseMessage> call, HttpStatusCode status, string? errorOrField, CancellationToken ct)
    {
        using var response = await call;
        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.ShouldBe(status, body);
        if (errorOrField is not null)
        {
            body.ShouldContain(errorOrField);
        }
    }

    private static async Task<List<string>> AuditAsync(TrimmeApiFactory factory, string entityId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        return await db.Set<AuditEntry>().AsNoTracking().Where(e => e.EntityId == entityId).OrderBy(e => e.OccurredAt)
            .Select(e => e.Action + "|" + e.Summary + "|" + e.Reason).ToListAsync(ct);
    }

    [Fact]
    public async Task SuperAdmin_ManagesPlans_WithAppendOnlyPriceVersions()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(Start);
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "sub_plans", ct, clock);
        using var super = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);

        using var created = await super.PostAsync("/api/v1/admin/subscription-plans", Plan("Annual"), ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));
        var plan = await created.JsonAsync(ct);
        var id = plan.GetProperty("id").GetGuid();
        plan.GetProperty("status").GetString().ShouldBe("Draft");
        plan.GetProperty("currentPrice").GetProperty("amount").GetDecimal().ShouldBe(1900m);
        plan.GetProperty("currentPrice").GetProperty("currency").GetString().ShouldBe("SAR", "the platform currency setting");
        plan.GetProperty("currentPrice").GetProperty("versionNumber").GetInt32().ShouldBe(1);
        plan.GetProperty("features")[0].GetProperty("en").GetString().ShouldBe("Listed in search");

        // A draft without a price cannot be published.
        using (var noPrice = await super.PostAsync("/api/v1/admin/subscription-plans", Plan("Free", initialPrice: null), ct))
        {
            var draft = (await noPrice.JsonAsync(ct)).GetProperty("id").GetGuid();
            await ShouldFailAsync(super.PostAsync($"/api/v1/admin/subscription-plans/{draft}/publish", null, ct), HttpStatusCode.UnprocessableEntity, "plan.no_price", ct);
        }

        // Details round-trip, including the JSON feature list.
        var updated = await OkJsonAsync(super.PutAsync($"/api/v1/admin/subscription-plans/{id}", Plan("Annual plus", version: plan.GetProperty("version").GetUInt32()), ct), ct);
        updated.GetProperty("nameEn").GetString().ShouldBe("Annual plus");
        updated.GetProperty("features").GetArrayLength().ShouldBe(1);
        await ShouldFailAsync(super.PutAsync($"/api/v1/admin/subscription-plans/{id}", Plan("Stale", version: plan.GetProperty("version").GetUInt32()), ct), HttpStatusCode.Conflict, null, ct);

        // New price version from a future date: the current price is unchanged until then.
        var version = updated.GetProperty("version").GetUInt32();
        var withPrice = await OkJsonAsync(super.PostAsync($"/api/v1/admin/subscription-plans/{id}/prices", new { amount = 2400m, effectiveFrom = Day(Today.AddDays(30)), version }, ct), ct);
        withPrice.GetProperty("currentPrice").GetProperty("amount").GetDecimal().ShouldBe(1900m);
        withPrice.GetProperty("upcomingPrice").GetProperty("amount").GetDecimal().ShouldBe(2400m);
        withPrice.GetProperty("upcomingPrice").GetProperty("versionNumber").GetInt32().ShouldBe(2);
        withPrice.GetProperty("prices").GetArrayLength().ShouldBe(2);
        version = withPrice.GetProperty("version").GetUInt32();

        // History is never rewritten: no past date, one version per date, a valid amount, a fresh version.
        await ShouldFailAsync(super.PostAsync($"/api/v1/admin/subscription-plans/{id}/prices", new { amount = 1m, effectiveFrom = Day(Today.AddDays(-1)), version }, ct), HttpStatusCode.BadRequest, "validation.date_in_past", ct);
        await ShouldFailAsync(super.PostAsync($"/api/v1/admin/subscription-plans/{id}/prices", new { amount = 1m, effectiveFrom = Day(Today.AddDays(30)), version }, ct), HttpStatusCode.BadRequest, "validation.price_date_taken", ct);
        await ShouldFailAsync(super.PostAsync($"/api/v1/admin/subscription-plans/{id}/prices", new { amount = 10.001m, effectiveFrom = Day(Today.AddDays(40)), version }, ct), HttpStatusCode.BadRequest, "validation.price_invalid", ct);
        await ShouldFailAsync(super.PostAsync($"/api/v1/admin/subscription-plans/{id}/prices", new { amount = 10m, effectiveFrom = Day(Today.AddDays(40)), version = version - 1 }, ct), HttpStatusCode.Conflict, null, ct);

        // Moving time past the version date makes it current.
        clock.Advance(TimeSpan.FromDays(30));
        using var later = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var current = await OkJsonAsync(later.GetAsync($"/api/v1/admin/subscription-plans/{id}", ct), ct);
        current.GetProperty("currentPrice").GetProperty("amount").GetDecimal().ShouldBe(2400m);
        current.GetProperty("upcomingPrice").ValueKind.ShouldBe(JsonValueKind.Null);
        var prices = await OkJsonAsync(later.GetAsync($"/api/v1/admin/subscription-plans/{id}/prices", ct), ct);
        prices.EnumerateArray().Select(p => p.GetProperty("amount").GetDecimal()).ShouldBe([2400m, 1900m]);

        // Publish, order, deactivate, archive; an archived plan is final.
        await OkJsonAsync(later.PostAsync($"/api/v1/admin/subscription-plans/{id}/publish", null, ct), ct);
        var all = await OkJsonAsync(later.GetAsync("/api/v1/admin/subscription-plans", ct), ct);
        var ids = all.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).Reverse().ToArray();
        var reordered = await OkJsonAsync(later.PutAsync("/api/v1/admin/subscription-plans/order", new { orderedIds = ids }, ct), ct);
        reordered.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe(ids);
        await ShouldFailAsync(later.PutAsync("/api/v1/admin/subscription-plans/order", new { orderedIds = ids.Take(1) }, ct), HttpStatusCode.BadRequest, "validation.order_mismatch", ct);
        (await OkJsonAsync(later.PostAsync($"/api/v1/admin/subscription-plans/{id}/deactivate", null, ct), ct)).GetProperty("status").GetString().ShouldBe("Inactive");
        var archived = await OkJsonAsync(later.PostAsync($"/api/v1/admin/subscription-plans/{id}/archive", null, ct), ct);
        archived.GetProperty("availableToNewShops").GetBoolean().ShouldBeFalse();
        await ShouldFailAsync(
            later.PutAsync($"/api/v1/admin/subscription-plans/{id}", Plan("Revived", version: archived.GetProperty("version").GetUInt32()), ct), HttpStatusCode.Conflict, "plan.archived", ct);

        var audit = await AuditAsync(factory, id.ToString(), ct);
        audit.ShouldContain(a => a.StartsWith("plan.created|", StringComparison.Ordinal));
        audit.ShouldContain($"plan.price_added|Price version 2: 2400.00 SAR from {Day(Today.AddDays(30))}|");
        audit.ShouldContain(a => a.StartsWith("plan.archived|", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlanFeatures_RoundTripThroughTheDatabase_IncludingAnEmptyList()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "sub_features", ct);
        using var super = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);

        var body = new
        {
            nameAr = "بلا مزايا", nameEn = "No features", descriptionAr = (string?)null, descriptionEn = (string?)null,
            features = Array.Empty<object>(), maxProfessionals = (int?)null, maxServices = (int?)null, intervalUnit = "Day", intervalCount = 30,
            trialDays = (int?)null, graceDays = (int?)null, availableToNewShops = true, initialPrice = (decimal?)null,
        };
        using var created = await super.PostAsync("/api/v1/admin/subscription-plans", body, ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));
        var plan = await created.JsonAsync(ct);
        var id = plan.GetProperty("id").GetGuid();

        // Re-read from the database, alone and in the list: an empty feature list stays a list.
        (await OkJsonAsync(super.GetAsync($"/api/v1/admin/subscription-plans/{id}", ct), ct)).GetProperty("features").GetArrayLength().ShouldBe(0);
        (await OkJsonAsync(super.GetAsync("/api/v1/admin/subscription-plans", ct), ct)).EnumerateArray()
            .Single(p => p.GetProperty("id").GetGuid() == id).GetProperty("features").GetArrayLength().ShouldBe(0);

        var update = new
        {
            body.nameAr, body.nameEn, body.descriptionAr, body.descriptionEn,
            features = new[] { new { ar = "ميزة أولى", en = "First" }, new { ar = "ميزة ثانية", en = "Second" } },
            body.maxProfessionals, body.maxServices, body.intervalUnit, body.intervalCount, body.trialDays, body.graceDays, body.availableToNewShops,
            version = plan.GetProperty("version").GetUInt32(),
        };
        await OkJsonAsync(super.PutAsync($"/api/v1/admin/subscription-plans/{id}", update, ct), ct);
        var reread = await OkJsonAsync(super.GetAsync($"/api/v1/admin/subscription-plans/{id}", ct), ct);
        reread.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("ar").GetString() + "|" + f.GetProperty("en").GetString())
            .ShouldBe(["ميزة أولى|First", "ميزة ثانية|Second"]);

        // And back to none.
        await OkJsonAsync(super.PutAsync($"/api/v1/admin/subscription-plans/{id}", update with { features = update.features[..0], version = reread.GetProperty("version").GetUInt32() }, ct), ct);
        (await OkJsonAsync(super.GetAsync($"/api/v1/admin/subscription-plans/{id}", ct), ct)).GetProperty("features").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task NonSuperAdmin_CannotManagePlans_OrOverride()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "sub_perm", ct);
        using var super = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, super, ct);
        var plan = await CreatePublishedPlanAsync(super, "Annual", ct);
        var planId = plan.GetProperty("id").GetGuid();
        using var ops = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.OperationsManager, ct);
        using var support = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.Support, ct);

        // Operations reads plans and records subscriptions, but never changes plans or prices, and never overrides.
        (await ops.GetAsync("/api/v1/admin/subscription-plans", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ops.PostAsync("/api/v1/admin/subscription-plans", Plan("Ops plan"), ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ops.PutAsync($"/api/v1/admin/subscription-plans/{planId}", Plan("Ops edit", version: plan.GetProperty("version").GetUInt32()), ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ops.PostAsync($"/api/v1/admin/subscription-plans/{planId}/prices", new { amount = 1m, effectiveFrom = "2030-01-01", version = 0 }, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ops.PostAsync($"/api/v1/admin/subscription-plans/{planId}/archive", null, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // D-081: custom durations, past starts and explicit prices are SuperAdmin-only, even for a role that can assign.
        foreach (var body in new object[]
                 {
                     new { planId, durationDays = 1095, price = 1m, reason = "Three years for one month's price" },
                     new { planId, startDate = DateTime.UtcNow.AddDays(-60).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), price = 1900m, reason = "Back-dated contract" },
                     new { planId, price = 1m, reason = "Special price" },
                     new { planId, durationDays = 45 },
                 })
        {
            await ShouldFailAsync(
                ops.PostAsync($"/api/v1/admin/shops/{shops.A.ShopId}/subscription/assign", body, ct), HttpStatusCode.Forbidden, "subscription.custom_pricing_required", ct);
        }

        var assigned = await OkJsonAsync(ops.PostAsync($"/api/v1/admin/shops/{shops.A.ShopId}/subscription/assign", new { planId }, ct), ct);
        assigned.GetProperty("periods")[0].GetProperty("pricingReason").ValueKind.ShouldBe(JsonValueKind.Null);
        await ShouldFailAsync(
            ops.PostAsync($"/api/v1/admin/shops/{shops.A.ShopId}/subscription/renew", new { durationDays = 45, price = 1m, reason = "Discounted period", version = assigned.GetProperty("version").GetUInt32() }, ct),
            HttpStatusCode.Forbidden, "subscription.custom_pricing_required", ct);
        (await ops.PostAsync($"/api/v1/admin/shops/{shops.A.ShopId}/subscription/override", new { price = 1m, reason = "Ops discount", version = assigned.GetProperty("version").GetUInt32() }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Support only views.
        (await support.GetAsync($"/api/v1/admin/shops/{shops.A.ShopId}/subscription", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await support.PostAsync($"/api/v1/admin/shops/{shops.B.ShopId}/subscription/assign", new { planId }, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await support.PostAsync("/api/v1/admin/subscription-plans", Plan("Support plan"), ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Shops never reach the admin surface.
        using var owner = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        (await owner.GetAsync("/api/v1/admin/subscription-plans", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.PostAsync("/api/v1/admin/subscription-plans", Plan("Shop plan"), ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.PostAsync($"/api/v1/admin/subscription-plans/{planId}/prices", new { amount = 1m, effectiveFrom = "2030-01-01", version = 0 }, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.GetAsync("/api/v1/admin/subscriptions", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.PostAsync($"/api/v1/admin/shops/{shops.A.ShopId}/subscription/renew", new { version = 0 }, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExistingSubscription_KeepsPriceSnapshot_WhenThePlanPriceChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(Start);
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "sub_snapshot", ct, clock);
        using var super = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shopId = await ShopTestData.CreateShopAsync(super, $"snap-{Guid.NewGuid():N}"[..20], ct);
        var plan = await CreatePublishedPlanAsync(super, "Annual", ct);
        var planId = plan.GetProperty("id").GetGuid();

        var assigned = await OkJsonAsync(super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/assign", new { planId, notes = "Signed contract" }, ct), ct);
        assigned.GetProperty("status").GetString().ShouldBe("Active");
        assigned.GetProperty("startDate").GetString().ShouldBe(Day(Today));
        assigned.GetProperty("endDate").GetString().ShouldBe(Day(YearEnd(Today)), "start + 12 months − 1 day");
        var first = assigned.GetProperty("periods")[0];
        first.GetProperty("amount").GetDecimal().ShouldBe(1900m);
        first.GetProperty("priceVersionNumber").GetInt32().ShouldBe(1);

        // SuperAdmin raises the price from next month and renames the plan.
        var edited = await OkJsonAsync(super.PutAsync($"/api/v1/admin/subscription-plans/{planId}", Plan("Annual renamed", version: plan.GetProperty("version").GetUInt32()), ct), ct);
        await OkJsonAsync(super.PostAsync($"/api/v1/admin/subscription-plans/{planId}/prices", new { amount = 2400m, effectiveFrom = Day(Today.AddDays(30)), version = edited.GetProperty("version").GetUInt32() }, ct), ct);

        // The recorded period is untouched: amount, price version and plan name are the snapshot.
        var after = await OkJsonAsync(super.GetAsync($"/api/v1/admin/shops/{shopId}/subscription", ct), ct);
        var kept = after.GetProperty("periods")[0];
        kept.GetProperty("amount").GetDecimal().ShouldBe(1900m);
        kept.GetProperty("priceVersionNumber").GetInt32().ShouldBe(1);
        kept.GetProperty("planNameEn").GetString().ShouldBe("Annual");
        after.GetProperty("currentAmount").GetDecimal().ShouldBe(1900m);

        // The renewal starts after the new version's date, so it is charged the new price.
        var renewed = await OkJsonAsync(super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/renew", new { version = after.GetProperty("version").GetUInt32() }, ct), ct);
        var periods = renewed.GetProperty("periods").EnumerateArray().ToArray();
        periods.Length.ShouldBe(2);
        periods[0].GetProperty("kind").GetString().ShouldBe("Renewed");
        periods[0].GetProperty("periodStart").GetString().ShouldBe(Day(YearEnd(Today).AddDays(1)));
        periods[0].GetProperty("periodEnd").GetString().ShouldBe(Day(YearEnd(YearEnd(Today).AddDays(1))));
        periods[0].GetProperty("amount").GetDecimal().ShouldBe(2400m);
        periods[0].GetProperty("priceVersionNumber").GetInt32().ShouldBe(2);
        periods[0].GetProperty("planNameEn").GetString().ShouldBe("Annual renamed");
        periods[1].GetProperty("amount").GetDecimal().ShouldBe(1900m);
        periods[1].GetProperty("priceVersionNumber").GetInt32().ShouldBe(1);
        renewed.GetProperty("currentAmount").GetDecimal().ShouldBe(1900m, "today is still inside the first period");
    }

    [Fact]
    public async Task Assign_Renew_Override_Suspend_KeepHistory_AndRejectInvalidChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(Start);
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "sub_flow", ct, clock);
        using var super = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shopId = await ShopTestData.CreateShopAsync(super, $"flow-{Guid.NewGuid():N}"[..20], ct);
        var monthly = (await CreatePublishedPlanAsync(super, "Monthly", ct, count: 1, price: 199m)).GetProperty("id").GetGuid();
        using (var draft = await super.PostAsync("/api/v1/admin/subscription-plans", Plan("Draft"), ct))
        {
            var draftId = (await draft.JsonAsync(ct)).GetProperty("id").GetGuid();
            await ShouldFailAsync(super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/assign", new { planId = draftId }, ct), HttpStatusCode.BadRequest, "validation.plan_not_offered", ct);
        }

        var none = await OkJsonAsync(super.GetAsync($"/api/v1/admin/shops/{shopId}/subscription", ct), ct);
        none.GetProperty("exists").GetBoolean().ShouldBeFalse();
        none.GetProperty("status").GetString().ShouldBe("None");
        (await super.GetAsync($"/api/v1/admin/shops/{Guid.NewGuid()}/subscription", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await ShouldFailAsync(super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/assign", new { planId = monthly, startDate = Day(Today.AddDays(1)) }, ct), HttpStatusCode.BadRequest, "validation.date_in_future", ct);

        // D-081: a custom duration is a SuperAdmin override with an explicit total and a reason.
        await ShouldFailAsync(
            super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/assign", new { planId = monthly, durationDays = 45 }, ct), HttpStatusCode.BadRequest, "price", ct);
        await ShouldFailAsync(
            super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/assign", new { planId = monthly, durationDays = 45, price = 199m }, ct),
            HttpStatusCode.BadRequest, "validation.reason_required", ct);

        // Two concurrent assignments: exactly one wins (unique shop_id), the other gets 409, never 500.
        var custom = new { planId = monthly, durationDays = 45, price = 199m, reason = "45-day launch period" };
        var racing = await Task.WhenAll(
            super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/assign", custom, ct),
            super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/assign", custom, ct));
        racing.Select(r => r.StatusCode).Order().ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        var subscription = await racing.Single(r => r.StatusCode == HttpStatusCode.OK).JsonAsync(ct);
        foreach (var r in racing)
        {
            r.Dispose();
        }

        subscription.GetProperty("endDate").GetString().ShouldBe(Day(Today.AddDays(44)), "an explicit 45-day duration (never one calendar month)");
        await ShouldFailAsync(super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/assign", new { planId = monthly }, ct), HttpStatusCode.Conflict, "subscription.already_assigned", ct);
        var version = subscription.GetProperty("version").GetUInt32();

        // Renewals never overlap and never leave a future gap.
        await ShouldFailAsync(super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/renew", new { startDate = Day(Today.AddDays(10)), version }, ct), HttpStatusCode.BadRequest, "validation.period_overlap", ct);
        await ShouldFailAsync(super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/renew", new { startDate = Day(Today.AddDays(60)), version }, ct), HttpStatusCode.BadRequest, "validation.period_gap", ct);
        var renewed = await OkJsonAsync(
            super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/renew", new { durationDays = 45, notes = "Paid by transfer", price = 180m, reason = "Second month agreed", version }, ct), ct);
        renewed.GetProperty("endDate").GetString().ShouldBe(Day(Today.AddDays(89)));
        var customRenewal = renewed.GetProperty("periods")[0];
        customRenewal.GetProperty("amount").GetDecimal().ShouldBe(180m, "the explicit total");
        customRenewal.GetProperty("standardAmount").GetDecimal().ShouldBe(199m, "the plan price it replaced");
        customRenewal.GetProperty("pricingReason").GetString().ShouldBe("Second month agreed");
        await ShouldFailAsync(super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/renew", new { version }, ct), HttpStatusCode.Conflict, null, ct);
        version = renewed.GetProperty("version").GetUInt32();

        // Override: reason required; only the latest period's end may move; the previous values are kept.
        await ShouldFailAsync(super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/override", new { price = 150m, reason = "", version }, ct), HttpStatusCode.BadRequest, "validation.reason_required", ct);
        await ShouldFailAsync(
            super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/override", new { endDate = Day(Today.AddDays(120)), reason = "Goodwill extension", version }, ct),
            HttpStatusCode.BadRequest, "validation.period_not_latest", ct);
        var overridden = await OkJsonAsync(super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/override", new { price = 150m, reason = "Launch discount agreed", version }, ct), ct);
        overridden.GetProperty("currentAmount").GetDecimal().ShouldBe(150m);
        var record = overridden.GetProperty("overrides")[0];
        record.GetProperty("previousAmount").GetDecimal().ShouldBe(199m);
        record.GetProperty("newAmount").GetDecimal().ShouldBe(150m);
        record.GetProperty("reason").GetString().ShouldBe("Launch discount agreed");
        overridden.GetProperty("periods").EnumerateArray().Single(p => p.GetProperty("amount").GetDecimal() == 150m)
            .GetProperty("periodStart").GetString().ShouldBe(Day(Today), "the period in force, not the future renewal");

        // Suspend and reinstate (version-checked), then the history is complete in the audit trail.
        version = overridden.GetProperty("version").GetUInt32();
        var suspended = await OkJsonAsync(super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/suspend", new { reason = "Contract under review", version }, ct), ct);
        suspended.GetProperty("status").GetString().ShouldBe("Suspended");
        await ShouldFailAsync(super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/reinstate", new { version }, ct), HttpStatusCode.Conflict, null, ct);
        var reinstated = await OkJsonAsync(super.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/reinstate", new { version = suspended.GetProperty("version").GetUInt32() }, ct), ct);
        reinstated.GetProperty("status").GetString().ShouldBe("Active");

        var audit = await AuditAsync(factory, await SubscriptionIdAsync(factory, shopId, ct), ct);
        audit.Select(a => a.Split('|')[0]).ShouldBe(["subscription.assigned", "subscription.renewed", "subscription.overridden", "subscription.suspended", "subscription.reinstated"]);
        audit[0].ShouldContain("SuperAdmin custom period, total 199.00 SAR instead of 199.00 SAR");
        audit[0].ShouldEndWith("|45-day launch period");
        audit[1].ShouldContain("SuperAdmin custom period, total 180.00 SAR instead of 199.00 SAR");
        audit[1].ShouldEndWith("|Second month agreed");
        audit[2].ShouldContain("amount 199.00 SAR to 150.00 SAR");
        audit[2].ShouldEndWith("|Launch discount agreed");
        audit.ShouldAllBe(a => !a.Contains("+966", StringComparison.Ordinal));
    }

    private static async Task<string> SubscriptionIdAsync(TrimmeApiFactory factory, Guid shopId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        return (await db.Set<AuditEntry>().AsNoTracking().Where(e => e.ShopId == shopId && e.Action == "subscription.assigned").SingleAsync(ct)).EntityId;
    }

    [Fact]
    public async Task Status_FollowsThePlatformCalendar_AndGatesBookability()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(Start);
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "sub_status", ct, clock);
        using var super = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var covered = await ShopTestData.CreateShopAsync(super, $"cov-{Guid.NewGuid():N}"[..20], ct);
        var uncovered = await ShopTestData.CreateShopAsync(super, $"unc-{Guid.NewGuid():N}"[..20], ct);
        var draftShop = await ShopTestData.CreateShopAsync(super, $"dra-{Guid.NewGuid():N}"[..20], ct, activate: false);
        var owner = IdentityTestData.NewEmail("owner");
        await ShopTestData.CreateShopUserAsync(factory, covered, owner, SystemRoles.ShopOwner, ct);
        var plan = (await CreatePublishedPlanAsync(super, "Monthly", ct, count: 1, price: 199m)).GetProperty("id").GetGuid();
        var thirtyDays = new { planId = plan, durationDays = 30, price = 199m, reason = "Thirty-day agreement" };
        await OkJsonAsync(super.PostAsync($"/api/v1/admin/shops/{covered}/subscription/assign", thirtyDays, ct), ct);
        await OkJsonAsync(super.PostAsync($"/api/v1/admin/shops/{draftShop}/subscription/assign", thirtyDays, ct), ct);

        async Task<ShopBookability> GateAsync(Guid shop)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IShopBookability>().GetAsync(new ShopId(shop), ct);
        }

        async Task<JsonElement> MineAsync()
        {
            using var session = await IdentityTestData.SignInStaffAsync(factory, owner, ct);
            return await OkJsonAsync(session.GetAsync("/api/v1/shop/subscription", ct), ct);
        }

        // Day 1 of 30: Active, 30 days left; bookable and visible. No subscription or an inactive shop: blocked.
        var mine = await MineAsync();
        mine.GetProperty("status").GetString().ShouldBe("Active");
        mine.GetProperty("daysRemaining").GetInt32().ShouldBe(30);
        mine.GetProperty("hiddenFromDiscovery").GetBoolean().ShouldBeFalse();
        (await GateAsync(covered)).ShouldBe(new ShopBookability(true, true, null));
        (await GateAsync(uncovered)).ShouldBe(new ShopBookability(false, false, "subscription.none"));
        (await GateAsync(draftShop)).BlockedReason.ShouldBe("shop.not_active");

        // 15 days left is still Active; 14 (the default threshold) is ExpiringSoon and still bookable.
        clock.Advance(TimeSpan.FromDays(15));
        (await MineAsync()).GetProperty("status").GetString().ShouldBe("Active");
        clock.Advance(TimeSpan.FromDays(1));
        mine = await MineAsync();
        mine.GetProperty("status").GetString().ShouldBe("ExpiringSoon");
        mine.GetProperty("daysRemaining").GetInt32().ShouldBe(14);
        (await GateAsync(covered)).AcceptsOnlineBookings.ShouldBeTrue();

        // The last day is covered; the day after, Expired: hidden and no new online bookings.
        clock.Advance(TimeSpan.FromDays(13));
        (await MineAsync()).GetProperty("daysRemaining").GetInt32().ShouldBe(1);
        clock.Advance(TimeSpan.FromDays(1));
        mine = await MineAsync();
        mine.GetProperty("status").GetString().ShouldBe("Expired");
        mine.GetProperty("hiddenFromDiscovery").GetBoolean().ShouldBeTrue();
        (await GateAsync(covered)).ShouldBe(new ShopBookability(false, false, "subscription.expired"));

        // The admin list counts over every shop and filters by status in SQL.
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var expired = await OkJsonAsync(admin.GetAsync("/api/v1/admin/subscriptions?status=Expired", ct), ct);
        expired.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("shopId").GetGuid()).ShouldBe([covered, draftShop], ignoreOrder: true);
        expired.GetProperty("counts").GetProperty("expired").GetInt32().ShouldBe(2);
        expired.GetProperty("counts").GetProperty("none").GetInt32().ShouldBe(1);
        (await OkJsonAsync(admin.GetAsync("/api/v1/admin/subscriptions?status=Active", ct), ct)).GetProperty("total").GetInt32().ShouldBe(0);

        // With enforcement off (platform setting), the status is informational only.
        var settings = await OkJsonAsync(admin.GetAsync("/api/v1/admin/settings", ct), ct);
        await OkJsonAsync(admin.PutAsync("/api/v1/admin/settings", SettingsBody(settings, enforcement: "None"), ct), ct);
        (await GateAsync(covered)).ShouldBe(new ShopBookability(true, true, null));
        (await GateAsync(draftShop)).BlockedReason.ShouldBe("shop.not_active", "an inactive shop is never bookable");

        // Renewing after a lapse starts today by default and restores coverage.
        var lapsed = await OkJsonAsync(admin.GetAsync($"/api/v1/admin/shops/{covered}/subscription", ct), ct);
        lapsed.GetProperty("nextStart").GetString().ShouldBe(Day(Today.AddDays(30)));
        clock.Advance(TimeSpan.FromDays(5));
        using var admin2 = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var back = await OkJsonAsync(admin2.PostAsync($"/api/v1/admin/shops/{covered}/subscription/renew", new { version = lapsed.GetProperty("version").GetUInt32() }, ct), ct);
        back.GetProperty("periods")[0].GetProperty("periodStart").GetString().ShouldBe(Day(Today.AddDays(35)));
        back.GetProperty("status").GetString().ShouldBe("Active");
    }

    private static object SettingsBody(JsonElement s, int? threshold = null, string? enforcement = null, int? slotStep = null, uint? version = null) => new
    {
        minLeadTimeMinutes = s.GetProperty("minLeadTimeMinutes").GetInt32(),
        bookingHorizonDays = s.GetProperty("bookingHorizonDays").GetInt32(),
        slotStepMinutes = slotStep ?? s.GetProperty("slotStepMinutes").GetInt32(),
        cancellationCutoffMinutes = s.GetProperty("cancellationCutoffMinutes").GetInt32(),
        reviewWindowDays = s.GetProperty("reviewWindowDays").GetInt32(),
        reminderOffsetMinutes = s.GetProperty("reminderOffsetMinutes").GetInt32(),
        expiringSoonThresholdDays = threshold ?? s.GetProperty("expiringSoonThresholdDays").GetInt32(),
        expiredSubscriptionEnforcement = enforcement ?? s.GetProperty("expiredSubscriptionEnforcement").GetString(),
        hidePausedShopsFromDiscovery = s.GetProperty("hidePausedShopsFromDiscovery").GetBoolean(),
        mapDefaultLatitude = s.GetProperty("mapDefaultLatitude").GetDouble(),
        mapDefaultLongitude = s.GetProperty("mapDefaultLongitude").GetDouble(),
        mapDefaultZoom = s.GetProperty("mapDefaultZoom").GetInt32(),
        version = version ?? s.GetProperty("version").GetUInt32(),
    };

    [Fact]
    public async Task PlatformSettings_HaveDefaults_AreAudited_VersionChecked_AndPermissionGated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "settings", ct);
        using var super = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, super, ct);

        var defaults = await OkJsonAsync(super.GetAsync("/api/v1/admin/settings", ct), ct);
        defaults.GetProperty("minLeadTimeMinutes").GetInt32().ShouldBe(60);
        defaults.GetProperty("bookingHorizonDays").GetInt32().ShouldBe(30);
        defaults.GetProperty("slotStepMinutes").GetInt32().ShouldBe(5);
        defaults.GetProperty("cancellationCutoffMinutes").GetInt32().ShouldBe(120);
        defaults.GetProperty("reviewWindowDays").GetInt32().ShouldBe(7);
        defaults.GetProperty("reminderOffsetMinutes").GetInt32().ShouldBe(30);
        defaults.GetProperty("expiringSoonThresholdDays").GetInt32().ShouldBe(14);
        defaults.GetProperty("expiredSubscriptionEnforcement").GetString().ShouldBe("HideAndBlockNewOnlineBookings");
        defaults.GetProperty("hidePausedShopsFromDiscovery").GetBoolean().ShouldBeTrue();
        defaults.GetProperty("defaultLocale").GetString().ShouldBe("ar");
        defaults.GetProperty("currency").GetString().ShouldBe("SAR");
        defaults.GetProperty("timeZone").GetString().ShouldBe("Asia/Riyadh");
        defaults.GetProperty("mapDefaultLatitude").GetDouble().ShouldBe(24.7136);

        await ShouldFailAsync(super.PutAsync("/api/v1/admin/settings", SettingsBody(defaults, slotStep: 7), ct), HttpStatusCode.BadRequest, "slotStepMinutes", ct);
        await ShouldFailAsync(super.PutAsync("/api/v1/admin/settings", SettingsBody(defaults, threshold: 0), ct), HttpStatusCode.BadRequest, "expiringSoonThresholdDays", ct);
        var updated = await OkJsonAsync(super.PutAsync("/api/v1/admin/settings", SettingsBody(defaults, threshold: 21, enforcement: "None"), ct), ct);
        updated.GetProperty("expiringSoonThresholdDays").GetInt32().ShouldBe(21);
        await ShouldFailAsync(super.PutAsync("/api/v1/admin/settings", SettingsBody(defaults, threshold: 30), ct), HttpStatusCode.Conflict, null, ct);

        var audit = await AuditAsync(factory, PlatformSettingsId.Singleton.Value.ToString(), ct);
        audit.ShouldBe(["platform_settings.updated|Changed: expiringSoonThresholdDays, expiredSubscriptionEnforcement|"]);

        // migrate re-runs the reference data: an admin's edit survives.
        await factory.MigrateAsync(ct);
        (await OkJsonAsync(super.GetAsync("/api/v1/admin/settings", ct), ct)).GetProperty("expiringSoonThresholdDays").GetInt32().ShouldBe(21);

        // Operations views but does not edit; shops have no access.
        using var ops = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.OperationsManager, ct);
        (await ops.GetAsync("/api/v1/admin/settings", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ops.PutAsync("/api/v1/admin/settings", SettingsBody(updated), ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var owner = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        (await owner.GetAsync("/api/v1/admin/settings", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Shop_SeesOnlyItsOwnSubscription()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "sub_shop", ct);
        using var super = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, super, ct);
        var plan = (await CreatePublishedPlanAsync(super, "Annual", ct)).GetProperty("id").GetGuid();
        await OkJsonAsync(super.PostAsync($"/api/v1/admin/shops/{shops.A.ShopId}/subscription/assign", new { planId = plan }, ct), ct);

        using var ownerA = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        using var ownerB = await IdentityTestData.SignInStaffAsync(factory, shops.B.OwnerEmail, ct);
        using var staffA = await IdentityTestData.SignInStaffAsync(factory, shops.A.StaffEmail, ct);

        var mine = await OkJsonAsync(ownerA.GetAsync("/api/v1/shop/subscription", ct), ct);
        mine.GetProperty("status").GetString().ShouldBe("Active");
        mine.GetProperty("planNameEn").GetString().ShouldBe("Annual");
        mine.GetProperty("renewals").GetArrayLength().ShouldBe(1);
        mine.TryGetProperty("overrides", out _).ShouldBeFalse("internal override reasons stay with the platform");

        var theirs = await OkJsonAsync(ownerB.GetAsync("/api/v1/shop/subscription", ct), ct);
        theirs.GetProperty("status").GetString().ShouldBe("None");
        theirs.GetProperty("renewals").GetArrayLength().ShouldBe(0);
        (await staffA.GetAsync("/api/v1/shop/subscription", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
