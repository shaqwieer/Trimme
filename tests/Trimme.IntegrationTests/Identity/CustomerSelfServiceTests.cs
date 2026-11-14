using System.Net;
using Npgsql;
using Shouldly;
using Trimme.IntegrationTests.Infrastructure;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Identity;

/// <summary>
/// D-144: the customer's personal info (an email of their own, a new number they then sign in with) and deleting the
/// account (refused while bookings are ahead; erases the personal data, signs out everywhere, frees the number).
/// </summary>
public sealed class CustomerSelfServiceTests(PostgresFixture postgres)
{
    private static readonly Dictionary<string, string?> CodesOff = new() { ["Identity:Otp:Enabled"] = "false" };

    private static async Task<(ApiSession Session, Guid Id)> PhoneSignInAsync(TrimmeApiFactory factory, string phone, bool expectNew, CancellationToken ct)
    {
        var session = ApiSession.Create(factory);
        var signedIn = await OkAsync(session.PostAsync("/api/v1/auth/phone/sign-in", new { phone, termsAccepted = true, locale = "ar" }, ct), ct);
        signedIn.GetProperty("isNewUser").GetBoolean().ShouldBe(expectNew);
        return (session, signedIn.GetProperty("user").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Profile_KeepsAnEmail_AndMovesTheAccountToANewNumber_ThatSignsInFromThenOn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "customer_profile", ct, settings: CodesOff);
        var (oldPhone, newPhone, otherPhone) = (IdentityTestData.NewPhone(), IdentityTestData.NewPhone(), IdentityTestData.NewPhone());
        var (customer, id) = await PhoneSignInAsync(factory, oldPhone, expectNew: true, ct);
        using var _ = customer;
        await OkAsync(customer.PostAsync("/api/v1/auth/profile/complete", new { displayName = "هند", preferredLocale = "ar", termsAccepted = true }, ct), ct);
        (await PhoneSignInAsync(factory, otherPhone, expectNew: true, ct)).Session.Dispose();

        var profile = await OkAsync(customer.GetAsync("/api/v1/me/profile", ct), ct);
        profile.GetProperty("displayName").GetString().ShouldBe("هند");
        profile.GetProperty("email").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        profile.GetProperty("phoneChangeAllowed").GetBoolean().ShouldBeTrue();
        var maskedBefore = profile.GetProperty("phoneMasked").GetString()!;
        maskedBefore.ShouldNotContain(oldPhone[4..]);

        await FailsAsync(
            customer.PutAsync("/api/v1/me/profile", new { displayName = "هند", preferredLocale = "ar", email = "not-an-email" }, ct),
            HttpStatusCode.BadRequest, "validation.email_invalid", ct);
        await FailsAsync(
            customer.PutAsync("/api/v1/me/profile", new { displayName = "هند", preferredLocale = "ar", phone = otherPhone }, ct),
            HttpStatusCode.Conflict, "auth.phone_taken", ct);
        await FailsAsync(
            customer.PutAsync("/api/v1/me/profile", new { displayName = "هند", preferredLocale = "ar", phone = "12345" }, ct),
            HttpStatusCode.BadRequest, "validation.phone_invalid", ct);

        var saved = await OkAsync(
            customer.PutAsync("/api/v1/me/profile", new { displayName = "هند القحطاني", preferredLocale = "en", email = " hind@example.com ", phone = newPhone }, ct), ct);
        saved.GetProperty("displayName").GetString().ShouldBe("هند القحطاني");
        saved.GetProperty("preferredLocale").GetString().ShouldBe("en");
        saved.GetProperty("email").GetString().ShouldBe("hind@example.com");
        saved.GetProperty("phoneMasked").GetString().ShouldNotBe(maskedBefore);

        // The email is the customer's own, apart from Identity's sign-in email; the new number waits for a code to confirm it.
        (await ScalarAsync(factory, "SELECT contact_email FROM identity.users WHERE id = @p", ct, id)).ShouldBe("hind@example.com");
        (await ScalarAsync(factory, "SELECT email IS NULL AND normalized_email IS NULL FROM identity.users WHERE id = @p", ct, id)).ShouldBe(true);
        (await ScalarAsync(factory, "SELECT phone_number_confirmed FROM identity.users WHERE id = @p", ct, id)).ShouldBe(false);

        // From now on the new number is this account, and the old one is free for a new account.
        var (again, sameId) = await PhoneSignInAsync(factory, newPhone, expectNew: false, ct);
        again.Dispose();
        sameId.ShouldBe(id);
        var (fresh, freshId) = await PhoneSignInAsync(factory, oldPhone, expectNew: true, ct);
        fresh.Dispose();
        freshId.ShouldNotBe(id);

        // Audited by field name only: never the number or the address.
        var summary = (string)(await ScalarAsync(
            factory, "SELECT summary FROM administration.audit_entries WHERE action = 'customer.contact_changed' AND entity_id = @p", ct, id.ToString()))!;
        summary.ShouldBe("Changed: phone, email");
    }

    [Fact]
    public async Task Profile_WhileCodesAreOn_KeepsTheNumber_ButSavesTheRest()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "customer_profile_codes", ct);
        using var customer = await CustomerAsync(factory, "سارة", ct);

        (await OkAsync(customer.GetAsync("/api/v1/me/profile", ct), ct)).GetProperty("phoneChangeAllowed").GetBoolean().ShouldBeFalse();
        await FailsAsync(
            customer.PutAsync("/api/v1/me/profile", new { displayName = "سارة", preferredLocale = "ar", phone = IdentityTestData.NewPhone() }, ct),
            HttpStatusCode.Forbidden, "auth.phone_change_otp_required", ct);
        (await OkAsync(customer.PutAsync("/api/v1/me/profile", new { displayName = "سارة علي", preferredLocale = "ar", email = "sara@example.com" }, ct), ct))
            .GetProperty("email").GetString().ShouldBe("sara@example.com");

        // Staff accounts have no customer profile.
        using var staff = await IdentityTestData.SignInNewStaffAsync(factory, Trimme.Modules.Identity.Domain.SystemRoles.Support, ct);
        await FailsAsync(staff.GetAsync("/api/v1/me/profile", ct), HttpStatusCode.Forbidden, null, ct);
        await FailsAsync(staff.DeleteAsync("/api/v1/me", ct), HttpStatusCode.Forbidden, null, ct);
    }

    [Fact]
    public async Task DeleteAccount_IsRefusedWithABookingAhead_ThenErasesTheCustomer_EndsEverySession_AndFreesTheNumber()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "customer_delete", ct, settings: CodesOff);
        var phone = IdentityTestData.NewPhone();
        var (customer, id) = await PhoneSignInAsync(w.Factory, phone, expectNew: true, ct);
        using var first = customer;
        await OkAsync(customer.PostAsync("/api/v1/auth/profile/complete", new { displayName = "خالد", preferredLocale = "ar", termsAccepted = true }, ct), ct);
        var (otherDevice, otherId) = await PhoneSignInAsync(w.Factory, phone, expectNew: false, ct);
        using var second = otherDevice;
        otherId.ShouldBe(id);
        // A shop is listed (and can be saved) once it has a location.
        await OkAsync(w.Admin.PutAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/location", new
        {
            latitude = 24.7743, longitude = 46.6380, addressLine = (string?)null, district = "الملقا", city = "الرياض", formattedAddress = "حي الملقا، الرياض", source = "Manual",
        }, ct), ct);
        await OkAsync(customer.PutAsync($"/api/v1/me/favorites/shops/{w.Shops.A.ShopId}", null, ct), ct, HttpStatusCode.NoContent);
        var day = TodayAt(DateTimeOffset.UtcNow).AddDays(2);
        var booking = await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(day, 10), ct), ct, HttpStatusCode.Created);

        await FailsAsync(customer.DeleteAsync("/api/v1/me", ct), HttpStatusCode.Conflict, "account.has_upcoming_bookings", ct);
        await OkAsync(
            customer.PostAsync($"/api/v1/me/bookings/{booking.GetProperty("id").GetGuid()}/cancel", new { version = booking.GetProperty("version").GetUInt32() }, ct), ct);

        using (var deleted = await customer.DeleteAsync("/api/v1/me", ct))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync(ct));
        }

        // Signed out here and on the other device; the browser forgets it was signed in.
        customer.Cookie("trimme-access").ShouldBeNull();
        customer.Cookie("trimme-signed-in").ShouldBeNull();
        await FailsAsync(customer.GetAsync("/api/v1/me", ct), HttpStatusCode.Unauthorized, null, ct);
        await FailsAsync(otherDevice.GetAsync("/api/v1/me", ct), HttpStatusCode.Unauthorized, null, ct);

        // The name, number and email are gone, the account is closed, and so are its saved shops.
        (await ScalarAsync(
            w.Factory,
            "SELECT display_name IS NULL AND protected_phone IS NULL AND phone_lookup_hash IS NULL AND contact_email IS NULL AND deleted_at IS NOT NULL AND disabled_at IS NOT NULL FROM identity.users WHERE id = @p",
            ct, id)).ShouldBe(true);
        (await ScalarAsync(w.Factory, "SELECT count(*) FROM customers.favorites WHERE customer_id = @p", ct, id)).ShouldBe(0L);
        (await ScalarAsync(w.Factory, "SELECT count(*) FROM identity.user_sessions WHERE user_id = @p AND revoked_at IS NULL", ct, id)).ShouldBe(0L);
        (await ScalarAsync(w.Factory, "SELECT count(*) FROM administration.audit_entries WHERE action = 'customer.account_deleted' AND entity_id = @p", ct, id.ToString()))
            .ShouldBe(1L);

        // The number can register again, as a brand-new account.
        var (again, newId) = await PhoneSignInAsync(w.Factory, phone, expectNew: true, ct);
        again.Dispose();
        newId.ShouldNotBe(id);
    }

    private static async Task<object?> ScalarAsync(TrimmeApiFactory factory, string sql, CancellationToken ct, object? parameter = null)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        if (parameter is not null)
        {
            command.Parameters.AddWithValue("p", parameter);
        }

        return await command.ExecuteScalarAsync(ct);
    }
}
