using System.Net;
using Shouldly;
using Trimme.IntegrationTests.Bookings;
using Trimme.IntegrationTests.Infrastructure;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Identity;

/// <summary>
/// D-145: a barber's own login. The owner (or the admin) turns it on with a temporary password the server generates
/// (D-146); the barber signs in with the WhatsApp number on their profile, replaces it, and is linked to their
/// professional. The login follows the number, and ends when it is turned off or the barber is disabled.
/// </summary>
public sealed class BarberLoginTests(PostgresFixture postgres)
{
    /// <summary>Any password before a login exists: it signs nobody in.</summary>
    private const string Temporary = "barber-temp-2026";
    internal const string Chosen = "barber-chosen-2026";

    internal static Task SetNumberAsync(BookingWorld w, Guid professionalId, string number, CancellationToken ct) => SetNumberAsync(w.Admin, professionalId, number, ct);

    internal static async Task SetNumberAsync(ApiSession admin, Guid professionalId, string number, CancellationToken ct) =>
        await OkAsync(admin.PutAsync($"/api/v1/admin/professionals/{professionalId}/whatsapp", new { whatsAppNumber = number, notificationsEnabled = false }, ct), ct);

    internal static Task<HttpResponseMessage> SignInAsync(ApiSession session, string phone, string password, CancellationToken ct) =>
        session.PostAsync("/api/v1/auth/staff/sign-in", new { phone, password }, ct);

    /// <summary>Turns the barber's login on as the shop's owner and signs the barber in for good (temporary password replaced).</summary>
    internal static Task<ApiSession> BarberAsync(BookingWorld w, Guid professionalId, string number, CancellationToken ct) =>
        BarberAsync(w.Factory, w.Admin, w.OwnerA, professionalId, number, ct);

    /// <summary>
    /// The same in any test world: the admin sets the barber's number, their shop's owner turns the login on and reads the
    /// generated temporary password (D-146), and the barber replaces it.
    /// </summary>
    internal static async Task<ApiSession> BarberAsync(TrimmeApiFactory factory, ApiSession admin, ApiSession owner, Guid professionalId, string number, CancellationToken ct)
    {
        await SetNumberAsync(admin, professionalId, number, ct);
        var temporary = await TurnOnAsync(owner, $"/api/v1/shop/professionals/{professionalId}/login", ct);
        var session = ApiSession.Create(factory);
        await OkAsync(session.PostAsync("/api/v1/auth/password/change-temporary", new { phone = number, password = temporary, newPassword = Chosen }, ct), ct);
        return session;
    }

    /// <summary>Turns a login on (or on again) at <paramref name="path"/> and returns the temporary password the server generated.</summary>
    internal static async Task<string> TurnOnAsync(ApiSession caller, string path, CancellationToken ct)
    {
        var on = await OkAsync(caller.PutAsync(path, body: null, ct), ct);
        on.GetProperty("login").GetProperty("enabled").GetBoolean().ShouldBeTrue();
        on.GetProperty("login").GetProperty("mustChangePassword").GetBoolean().ShouldBeTrue();
        return on.GetProperty("temporaryPassword").GetString().ShouldNotBeNull();
    }

    /// <summary>A barber whose login is on signs in again (for example after the test clock moved past the access cookie).</summary>
    internal static async Task<ApiSession> SignInBarberAsync(TrimmeApiFactory factory, string number, CancellationToken ct)
    {
        var session = ApiSession.Create(factory);
        await OkAsync(SignInAsync(session, number, Chosen, ct), ct);
        return session;
    }

    /// <summary>A barber at a shop of a <see cref="Tenancy.TwoShops"/> world, with a login (the world has no barbers of its own).</summary>
    internal static async Task<ApiSession> NewBarberAsync(TrimmeApiFactory factory, ApiSession admin, ApiSession owner, Guid shopId, string number, CancellationToken ct)
    {
        var professional = (await OkAsync(admin.PostAsync("/api/v1/admin/professionals", new { shopId, nameAr = "حلاق", nameEn = "Barber" }, ct), ct, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        return await BarberAsync(factory, admin, owner, professional, number, ct);
    }

    [Fact]
    public async Task ABarber_SignsInWithTheirNumber_AfterReplacingTheTemporaryPassword_AndIsLinkedToTheirProfessional()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "barber_login", ct);
        const string number = "+966512340001";
        await SetNumberAsync(w, w.Faisal, number, ct);

        var before = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/professionals/{w.Faisal}/login", ct), ct);
        before.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        before.GetProperty("hasNumber").GetBoolean().ShouldBeTrue();

        // Another shop's owner cannot see or touch this barber; a barber's number with no login signs nobody in.
        await FailsAsync(w.OwnerB.GetAsync($"/api/v1/shop/professionals/{w.Faisal}/login", ct), HttpStatusCode.NotFound, "professional.not_found", ct);
        using (var none = ApiSession.Create(w.Factory))
        {
            await FailsAsync(SignInAsync(none, number, Temporary, ct), HttpStatusCode.Unauthorized, "auth.invalid_credentials", ct);
        }

        // The server generates the temporary password (D-146): 12 easy-to-read characters, a new one each time, and the
        // previous one stops working.
        var first = await TurnOnAsync(w.OwnerA, $"/api/v1/shop/professionals/{w.Faisal}/login", ct);
        var temporary = await TurnOnAsync(w.OwnerA, $"/api/v1/shop/professionals/{w.Faisal}/login", ct);
        temporary.Length.ShouldBe(12);
        temporary.ShouldAllBe(c => "abcdefghjkmnpqrstuvwxyz23456789".Contains(c));
        temporary.ShouldNotBe(first);
        using (var stale = ApiSession.Create(w.Factory))
        {
            await FailsAsync(SignInAsync(stale, number, first, ct), HttpStatusCode.Unauthorized, "auth.invalid_credentials", ct);
        }

        // The temporary password starts no session; the number may be typed the local way.
        using var barber = ApiSession.Create(w.Factory);
        await FailsAsync(SignInAsync(barber, "0512340001", temporary, ct), HttpStatusCode.Forbidden, "auth.password_change_required", ct);
        await FailsAsync(barber.GetAsync("/api/v1/me", ct), HttpStatusCode.Unauthorized, null, ct);
        var signedIn = await OkAsync(
            barber.PostAsync("/api/v1/auth/password/change-temporary", new { phone = "0512340001", password = temporary, newPassword = Chosen }, ct), ct);
        var me = signedIn.GetProperty("user");
        me.GetProperty("userType").GetString().ShouldBe("ShopUser");
        me.GetProperty("professionalId").GetGuid().ShouldBe(w.Faisal);
        me.GetProperty("shopId").GetGuid().ShouldBe(w.Shops.A.ShopId);
        var permissions = me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToArray();
        permissions.ShouldContain("Shop.Bookings.Reschedule");
        permissions.ShouldContain("Shop.Bookings.ChangeProfessional");
        permissions.ShouldContain("Shop.Breaks.Manage");
        permissions.ShouldNotContain("Shop.Services.Manage");
        permissions.ShouldNotContain("Shop.Barbers.ManageLogin");
        permissions.ShouldNotContain("Shop.Notifications.Manage");

        // The refreshed session keeps the professional (read from the account again, never lost).
        await OkAsync(barber.PostAsync("/api/v1/auth/refresh", body: null, ct), ct, HttpStatusCode.NoContent);
        (await OkAsync(barber.GetAsync("/api/v1/me", ct), ct)).GetProperty("professionalId").GetGuid().ShouldBe(w.Faisal);

        // A barber cannot turn logins on, theirs or a colleague's.
        await FailsAsync(barber.PutAsync($"/api/v1/shop/professionals/{w.Omar}/login", body: null, ct), HttpStatusCode.Forbidden, null, ct);

        // Wrong password and unknown number answer alike.
        using var other = ApiSession.Create(w.Factory);
        await FailsAsync(SignInAsync(other, number, "not-the-password", ct), HttpStatusCode.Unauthorized, "auth.invalid_credentials", ct);
        await FailsAsync(SignInAsync(other, "+966599999999", Chosen, ct), HttpStatusCode.Unauthorized, "auth.invalid_credentials", ct);
        await FailsAsync(SignInAsync(other, "not a number", Chosen, ct), HttpStatusCode.Unauthorized, "auth.invalid_credentials", ct);
        await OkAsync(SignInAsync(other, number, Chosen, ct), ct);

        // The same number can also be a customer's: the two never collide.
        using var customer = await IdentityTestData.SignInCustomerAsync(w.Factory, number, ct);
        (await OkAsync(customer.GetAsync("/api/v1/me", ct), ct)).GetProperty("userType").GetString().ShouldBe("Customer");
    }

    [Fact]
    public async Task TheLogin_FollowsTheNumber_AndEndsWhenTurnedOff_OrTheBarberIsDisabled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "barber_login_life", ct);

        // Without a number there is nothing to sign in with.
        await FailsAsync(w.OwnerA.PutAsync($"/api/v1/shop/professionals/{w.Omar}/login", body: null, ct), HttpStatusCode.BadRequest, "barber.number_required", ct);

        using var barber = await BarberAsync(w, w.Faisal, "+966512340002", ct);

        // The admin changes the number: the new one signs in, the old one no longer does.
        await SetNumberAsync(w, w.Faisal, "+966512340003", ct);
        using (var session = ApiSession.Create(w.Factory))
        {
            await FailsAsync(SignInAsync(session, "+966512340002", Chosen, ct), HttpStatusCode.Unauthorized, "auth.invalid_credentials", ct);
            await OkAsync(SignInAsync(session, "+966512340003", Chosen, ct), ct);
        }

        // Turned off: every session ends at once, and signing in says so.
        await OkAsync(w.OwnerA.DeleteAsync($"/api/v1/shop/professionals/{w.Faisal}/login", ct), ct);
        await FailsAsync(barber.GetAsync("/api/v1/me", ct), HttpStatusCode.Unauthorized, null, ct);
        using (var session = ApiSession.Create(w.Factory))
        {
            await FailsAsync(SignInAsync(session, "+966512340003", Chosen, ct), HttpStatusCode.Forbidden, "auth.account_disabled", ct);
        }

        // On again (here by the admin): a new temporary password, replaced at the first sign-in.
        var again = await TurnOnAsync(w.Admin, $"/api/v1/admin/professionals/{w.Faisal}/login", ct);
        using var back = ApiSession.Create(w.Factory);
        await FailsAsync(SignInAsync(back, "+966512340003", again, ct), HttpStatusCode.Forbidden, "auth.password_change_required", ct);
        await OkAsync(back.PostAsync("/api/v1/auth/password/change-temporary", new { phone = "+966512340003", password = again, newPassword = Chosen }, ct), ct);
        await OkAsync(back.GetAsync("/api/v1/me", ct), ct);

        // The admin disables the barber: their login ends with them, and is not turned back on by enabling them.
        await OkAsync(w.Admin.PostAsync($"/api/v1/admin/professionals/{w.Faisal}/disable", new { reason = "left the shop" }, ct), ct);
        await FailsAsync(back.GetAsync("/api/v1/me", ct), HttpStatusCode.Unauthorized, null, ct);
        await FailsAsync(w.OwnerA.PutAsync($"/api/v1/shop/professionals/{w.Faisal}/login", body: null, ct), HttpStatusCode.Conflict, "barber.professional_inactive", ct);
        await OkAsync(w.Admin.PostAsync($"/api/v1/admin/professionals/{w.Faisal}/enable", new { reason = "back" }, ct), ct);
        (await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/professionals/{w.Faisal}/login", ct), ct)).GetProperty("enabled").GetBoolean().ShouldBeFalse();
    }
}
