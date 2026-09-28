using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.IntegrationTests.Tenancy;
using Trimme.Modules.Identity.Domain;

namespace Trimme.IntegrationTests.Bookings;

/// <summary>
/// Two shops open 09:00–21:00 every day with a subscription in force (D-078), a 30-minute haircut and a 60-minute
/// package at shop A done by two professionals, and shop B with its own service and professional.
/// </summary>
internal sealed record BookingWorld(
    TrimmeApiFactory Factory,
    FakeTimeProvider? Clock,
    ApiSession Admin,
    TwoShops Shops,
    ApiSession OwnerA,
    ApiSession StaffA,
    ApiSession OwnerB,
    string SlugA,
    Guid Haircut,
    Guid Beard,
    Guid Package,
    Guid ServiceB,
    Guid Faisal,
    Guid Omar,
    Guid ProB) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        Admin.Dispose();
        OwnerA.Dispose();
        StaffA.Dispose();
        OwnerB.Dispose();
        await Factory.DisposeAsync();
    }
}

internal static class BookingTestData
{
    public static readonly TimeZoneInfo Riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

    public static DateOnly TodayAt(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Riyadh).DateTime);

    /// <summary>A Riyadh wall-clock time as an instant.</summary>
    public static DateTimeOffset At(DateOnly day, int hour, int minute = 0) => new(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(3));

    public static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static Dictionary<string, string> Key(string? key = null) => new() { ["Idempotency-Key"] = key ?? Guid.NewGuid().ToString("N") };

    public static async Task<JsonElement> OkAsync(Task<HttpResponseMessage> call, CancellationToken ct, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var response = await call;
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(ct));
        return status == HttpStatusCode.NoContent ? default : await response.JsonAsync(ct);
    }

    public static async Task<string> FailsAsync(Task<HttpResponseMessage> call, HttpStatusCode status, string? contains, CancellationToken ct)
    {
        using var response = await call;
        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.ShouldBe(status, body);
        if (contains is not null)
        {
            body.ShouldContain(contains);
        }

        return body;
    }

    public static async Task<BookingWorld> ArrangeAsync(PostgresFixture postgres, string prefix, CancellationToken ct, FakeTimeProvider? clock = null)
    {
        var factory = await IdentityTestData.CreateFactoryAsync(postgres, prefix, ct, clock);

        var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        var ownerA = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        var staffA = await IdentityTestData.SignInStaffAsync(factory, shops.A.StaffEmail, ct);
        var ownerB = await IdentityTestData.SignInStaffAsync(factory, shops.B.OwnerEmail, ct);

        async Task<Guid> ServiceAsync(ApiSession owner, string name, int minutes) =>
            (await OkAsync(owner.PostAsync("/api/v1/shop/services", new { nameAr = name, price = 60m, durationMinutes = minutes, onlineBookable = true }, ct), ct, HttpStatusCode.Created))
                .GetProperty("id").GetGuid();
        async Task<Guid> ProfessionalAsync(Guid shopId, string ar, string en) =>
            (await OkAsync(admin.PostAsync("/api/v1/admin/professionals", new { shopId, nameAr = ar, nameEn = en }, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var haircut = await ServiceAsync(ownerA, "حلاقة", 30);
        var beard = await ServiceAsync(ownerA, "لحية", 20);
        var serviceB = await ServiceAsync(ownerB, "حلاقة", 30);
        var package = (await OkAsync(ownerA.PostAsync("/api/v1/shop/packages", new { nameAr = "باقة", price = 80m, durationMinutes = 60, serviceIds = new[] { haircut, beard } }, ct), ct, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var faisal = await ProfessionalAsync(shops.A.ShopId, "فيصل", "Faisal");
        var omar = await ProfessionalAsync(shops.A.ShopId, "عمر", "Omar");
        var proB = await ProfessionalAsync(shops.B.ShopId, "سالم", "Salem");
        foreach (var professional in new[] { faisal, omar })
        {
            await OkAsync(admin.PutAsync($"/api/v1/admin/professionals/{professional}/services", new { serviceIds = new[] { haircut, beard } }, ct), ct);
        }

        await OkAsync(admin.PutAsync($"/api/v1/admin/professionals/{proB}/services", new { serviceIds = new[] { serviceB } }, ct), ct);

        var everyDay = Enum.GetValues<DayOfWeek>().Select(d => new { day = d.ToString(), startMinute = 540, endMinute = 1260 }).ToArray();
        await OkAsync(ownerA.PutAsync("/api/v1/shop/schedule/opening-hours", new { intervals = everyDay }, ct), ct);
        await OkAsync(ownerB.PutAsync("/api/v1/shop/schedule/opening-hours", new { intervals = everyDay }, ct), ct);
        await SubscribeAsync(admin, shops.A.ShopId, ct);
        await SubscribeAsync(admin, shops.B.ShopId, ct, "B");

        var slugA = (await OkAsync(admin.GetAsync($"/api/v1/admin/shops/{shops.A.ShopId}", ct), ct)).GetProperty("slug").GetString()!;
        return new BookingWorld(factory, clock, admin, shops, ownerA, staffA, ownerB, slugA, haircut, beard, package, serviceB, faisal, omar, proB);
    }

    /// <summary>A signed-in customer who completed the profile (a name is required to book).</summary>
    public static async Task<ApiSession> CustomerAsync(TrimmeApiFactory factory, string name, CancellationToken ct)
    {
        var session = await IdentityTestData.SignInCustomerAsync(factory, IdentityTestData.NewPhone(), ct);
        await OkAsync(session.PostAsync("/api/v1/auth/profile/complete", new { displayName = name, preferredLocale = "ar", termsAccepted = true }, ct), ct);
        return session;
    }

    public static Task<HttpResponseMessage> BookAsync(
        ApiSession customer, string slug, Guid serviceId, Guid? professionalId, DateTimeOffset start, CancellationToken ct, string? key = null, Guid? packageId = null) =>
        customer.SendAsync(
            HttpMethod.Post,
            "/api/v1/bookings",
            new { shopSlug = slug, serviceId = packageId is null ? serviceId : (Guid?)null, packageId, professionalId, startsAt = start, note = (string?)null },
            ct,
            headers: Key(key));

    private static async Task SubscribeAsync(ApiSession admin, Guid shopId, CancellationToken ct, string suffix = "A")
    {
        var plan = new
        {
            nameAr = $"باقة {suffix}", nameEn = $"Plan {suffix}", descriptionAr = (string?)null, descriptionEn = (string?)null, features = new[] { new { ar = "ظهور", en = "Listed" } },
            maxProfessionals = (int?)null, maxServices = (int?)null, intervalUnit = "Month", intervalCount = 12, trialDays = (int?)null, graceDays = (int?)null,
            availableToNewShops = true, initialPrice = 1900m,
        };
        var planId = (await OkAsync(admin.PostAsync("/api/v1/admin/subscription-plans", plan, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await OkAsync(admin.PostAsync($"/api/v1/admin/subscription-plans/{planId}/publish", null, ct), ct);
        await OkAsync(admin.PostAsync($"/api/v1/admin/shops/{shopId}/subscription/assign", new { planId }, ct), ct);
    }
}
