using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Trimme.BuildingBlocks.Application.Directories;
using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.IntegrationTests.Tenancy;
using Trimme.Modules.Bookings.Application;
using Trimme.Modules.Bookings.Domain;
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
    private readonly List<Guid> barberLogins = [];

    /// <summary>
    /// A barber's own signed-in login (D-145), for what only a barber does: walk-ins are theirs, not the owner's (D-146).
    /// The first call turns the login on (with the admin and the owner, so before a test moves its clock past their
    /// sessions); every call signs in again, so a test that moved its clock gets a fresh session. The caller disposes it.
    /// </summary>
    public async Task<ApiSession> BarberAsync(Guid professionalId, CancellationToken ct)
    {
        var index = barberLogins.IndexOf(professionalId);
        if (index >= 0)
        {
            return await BarberLoginTests.SignInBarberAsync(Factory, BarberNumber(index), ct);
        }

        barberLogins.Add(professionalId);
        return await BarberLoginTests.BarberAsync(
            Factory, Admin, professionalId == ProB ? OwnerB : OwnerA, professionalId, BarberNumber(barberLogins.Count - 1), ct);
    }

    /// <summary>Turns the barber's login on now (see <see cref="BarberAsync"/>), for a test that moves its clock before signing in.</summary>
    public async Task EnsureBarberLoginAsync(Guid professionalId, CancellationToken ct) => (await BarberAsync(professionalId, ct)).Dispose();

    /// <summary>The booking's own barber, signed in: the barbers operate the bookings and the owner only watches (D-147).</summary>
    public async Task<ApiSession> BarberOfAsync(Guid bookingId, CancellationToken ct) => await BarberAsync(await ProfessionalOfAsync(bookingId, ct), ct);

    /// <summary>Whose booking it is now, read as the system (no session needed).</summary>
    public async Task<Guid> ProfessionalOfAsync(Guid bookingId, CancellationToken ct)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        using var system = scope.ServiceProvider.GetRequiredService<ISystemDataScope>().Begin();
        var id = new BookingId(bookingId);
        return (await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<Booking>().AsNoTracking()
            .Where(b => b.Id == id).Select(b => b.ProfessionalId).SingleAsync(ct)).Value;
    }

    /// <summary>The world's barber logins' numbers: a range no test types by hand.</summary>
    private static string BarberNumber(int index) => $"+96655500{index + 1:0000}";

    public async ValueTask DisposeAsync()
    {
        Admin.Dispose();
        OwnerA.Dispose();
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

    /// <summary>A shop transition at the booking's current version (D-016), by whoever <paramref name="staff"/> is.</summary>
    public static async Task<JsonElement> TransitionAsync(ApiSession staff, Guid bookingId, string to, CancellationToken ct, string? reason = null)
    {
        var current = await OkAsync(staff.GetAsync($"/api/v1/shop/bookings/{bookingId}", ct), ct);
        return await OkAsync(
            staff.PostAsync($"/api/v1/shop/bookings/{bookingId}/transitions", new { to, reason, version = current.GetProperty("booking").GetProperty("version").GetUInt32() }, ct), ct);
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

    public static async Task<BookingWorld> ArrangeAsync(
        PostgresFixture postgres, string prefix, CancellationToken ct, FakeTimeProvider? clock = null, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var factory = await IdentityTestData.CreateFactoryAsync(postgres, prefix, ct, clock, settings);

        var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        var ownerA = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
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
        return new BookingWorld(factory, clock, admin, shops, ownerA, ownerB, slugA, haircut, beard, package, serviceB, faisal, omar, proB);
    }

    /// <summary>A signed-in customer who completed the profile (a name is required to book).</summary>
    public static async Task<ApiSession> CustomerAsync(TrimmeApiFactory factory, string name, CancellationToken ct)
    {
        var session = await IdentityTestData.SignInCustomerAsync(factory, IdentityTestData.NewPhone(), ct);
        await OkAsync(session.PostAsync("/api/v1/auth/profile/complete", new { displayName = name, preferredLocale = "ar", termsAccepted = true }, ct), ct);
        return session;
    }

    /// <summary>
    /// An online booking written straight to the database, as test data (the seed's way, with no outbox message): for a
    /// test that needs one customer to hold several bookings, which the API refuses since D-155 (one active booking per
    /// customer). Confirmed, with the professional's own length.
    /// </summary>
    public static async Task<Guid> SeedOnlineBookingAsync(
        BookingWorld w, Guid shopId, Guid customerId, string customerName, Guid? serviceId, Guid professionalId, DateTimeOffset start, CancellationToken ct,
        Guid? packageId = null)
    {
        await using var scope = w.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        using var system = services.GetRequiredService<ISystemDataScope>().Begin();
        var db = services.GetRequiredService<TrimmeDbContext>();
        var shop = new ShopId(shopId);
        var offer = (await services.GetRequiredService<IBookableOfferCatalog>().FindAsync(shop, packageId is null ? serviceId : null, packageId, ct)).ShouldNotBeNull();
        var pro = (await services.GetRequiredService<IProfessionalDirectory>().FindAsync(new ProfessionalId(professionalId), ct)).ShouldNotBeNull();
        var booking = Booking.CreateOnline(
            new BookingId(Guid.CreateVersion7()), shop, customerId, customerName, new BookedProfessional(pro.Id, pro.NameAr, pro.NameEn),
            BookingMapping.Snapshot(offer, pro.Id), start, null, w.Clock?.GetUtcNow() ?? DateTimeOffset.UtcNow);
        db.Add(booking);
        await db.SaveChangesAsync(ct);
        return booking.Id.Value;
    }

    public static Task<HttpResponseMessage> BookAsync(
        ApiSession customer, string slug, Guid serviceId, Guid? professionalId, DateTimeOffset start, CancellationToken ct, string? key = null, Guid? packageId = null) =>
        customer.SendAsync(
            HttpMethod.Post,
            "/api/v1/bookings",
            new { shopSlug = slug, serviceId = packageId is null ? serviceId : (Guid?)null, packageId, professionalId, startsAt = start, note = (string?)null },
            ct,
            headers: Key(key));

    public static async Task SubscribeAsync(ApiSession admin, Guid shopId, CancellationToken ct, string suffix = "A")
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
