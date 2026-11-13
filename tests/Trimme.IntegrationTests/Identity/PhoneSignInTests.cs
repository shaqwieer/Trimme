using System.Net;
using Npgsql;
using Shouldly;
using Trimme.IntegrationTests.Infrastructure;

namespace Trimme.IntegrationTests.Identity;

/// <summary>
/// D-143: customer sign-up and sign-in by mobile number alone while codes are off (the default), and the code flow
/// only once <c>Identity:Otp:Enabled</c> is true. The test host turns codes on by default; these tests set it.
/// </summary>
public sealed class PhoneSignInTests(PostgresFixture postgres)
{
    private static readonly Dictionary<string, string?> CodesOff = new() { ["Identity:Otp:Enabled"] = "false" };

    [Fact]
    public async Task CodesOn_ANumberAlone_SignsNobodyIn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "phone_codes_on", ct);
        var phone = IdentityTestData.NewPhone();
        using var session = ApiSession.Create(factory);

        using (var options = await session.GetAsync("/api/v1/auth/sign-in-options", ct))
        {
            options.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await options.JsonAsync(ct)).GetProperty("otpRequired").GetBoolean().ShouldBeTrue();
        }

        using (var refused = await session.PostAsync("/api/v1/auth/phone/sign-in", new { phone, termsAccepted = true, locale = "ar" }, ct))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await refused.ErrorCodeAsync(ct)).ShouldBe("auth.otp_required");
        }

        session.Cookie("trimme-access").ShouldBeNull();
        using (var me = await session.GetAsync("/api/v1/me", ct))
        {
            me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await ScalarAsync(factory, "SELECT count(*) FROM identity.users WHERE user_type = 'Customer'", ct)).ShouldBe(0L);
    }

    [Fact]
    public async Task CodesOff_ByDefault_SignsUpAndSignsInByNumber_AndCodesAreRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "phone_codes_off", ct, settings: CodesOff);
        var phone = IdentityTestData.NewPhone();
        using var session = ApiSession.Create(factory);

        using (var options = await session.GetAsync("/api/v1/auth/sign-in-options", ct))
        {
            (await options.JsonAsync(ct)).GetProperty("otpRequired").GetBoolean().ShouldBeFalse();
        }

        // The code endpoints are switched off.
        using (var request = await session.PostAsync("/api/v1/auth/otp/request", new { phone, termsAccepted = true, locale = "ar" }, ct))
        {
            request.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await request.ErrorCodeAsync(ct)).ShouldBe("otp.disabled");
        }

        using (var verify = await session.PostAsync("/api/v1/auth/otp/verify", new { challengeId = Guid.CreateVersion7(), code = "123456" }, ct))
        {
            verify.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await verify.ErrorCodeAsync(ct)).ShouldBe("otp.disabled");
        }

        // Sign-up: a new account, signed in, whose profile still needs a name.
        using var signUp = await session.PostAsync("/api/v1/auth/phone/sign-in", new { phone, termsAccepted = true, locale = "ar" }, ct);
        signUp.StatusCode.ShouldBe(HttpStatusCode.OK, await signUp.Content.ReadAsStringAsync(ct));
        var created = await signUp.JsonAsync(ct);
        created.GetProperty("isNewUser").GetBoolean().ShouldBeTrue();
        var user = created.GetProperty("user");
        user.GetProperty("userType").GetString().ShouldBe("Customer");
        user.GetProperty("profileComplete").GetBoolean().ShouldBeFalse();
        user.GetProperty("phoneMasked").GetString().ShouldBe($"+966 5•• ••• •{phone[^2..]}");
        session.Cookie("trimme-access").ShouldNotBeNull();

        using (var complete = await session.PostAsync(
            "/api/v1/auth/profile/complete", new { displayName = "نورة القحطاني", preferredLocale = "ar", termsAccepted = true }, ct))
        {
            complete.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await complete.JsonAsync(ct)).GetProperty("profileComplete").GetBoolean().ShouldBeTrue();
        }

        // Sign-in on another device: the same account, any number format libphonenumber reads.
        using var other = ApiSession.Create(factory);
        using var signIn = await other.PostAsync("/api/v1/auth/phone/sign-in", new { phone = "0" + phone[4..], termsAccepted = true, locale = "ar" }, ct);
        signIn.StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = await signIn.JsonAsync(ct);
        again.GetProperty("isNewUser").GetBoolean().ShouldBeFalse();
        again.GetProperty("user").GetProperty("id").GetGuid().ShouldBe(user.GetProperty("id").GetGuid());
        again.GetProperty("user").GetProperty("displayName").GetString().ShouldBe("نورة القحطاني");

        // The number is stored protected, and marked as never confirmed by a code.
        (await ScalarAsync(factory, "SELECT phone_number_confirmed FROM identity.users WHERE id = @p", ct, user.GetProperty("id").GetGuid()))
            .ShouldBe(false);
        (await ScalarAsync(factory, "SELECT count(*) FROM identity.users WHERE protected_phone LIKE '%' || @p || '%'", ct, phone[1..]))
            .ShouldBe(0L);

        // An invalid number is a validation error and opens nothing.
        using var invalid = ApiSession.Create(factory);
        using (var bad = await invalid.PostAsync("/api/v1/auth/phone/sign-in", new { phone = "+966123", termsAccepted = true, locale = "ar" }, ct))
        {
            bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await bad.JsonAsync(ct)).GetProperty("errors").GetProperty("phone")[0].GetString().ShouldBe("validation.phone_invalid");
        }

        invalid.Cookie("trimme-access").ShouldBeNull();

        // Without the CSRF header the call is refused like every other unsafe call.
        using var noCsrf = await ApiSession.Create(factory).SendAsync(
            HttpMethod.Post, "/api/v1/auth/phone/sign-in", new { phone, termsAccepted = true, locale = "ar" }, ct, withCsrf: false);
        noCsrf.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CodesOff_ADisabledCustomer_CannotSignIn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "phone_disabled", ct, settings: CodesOff);
        var phone = IdentityTestData.NewPhone();

        using (var first = await ApiSession.Create(factory).PostAsync("/api/v1/auth/phone/sign-in", new { phone, termsAccepted = true, locale = "ar" }, ct))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await ScalarAsync(factory, "UPDATE identity.users SET disabled_at = now() WHERE user_type = 'Customer'", ct);

        using var session = ApiSession.Create(factory);
        using var refused = await session.PostAsync("/api/v1/auth/phone/sign-in", new { phone, termsAccepted = true, locale = "ar" }, ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.ErrorCodeAsync(ct)).ShouldBe("auth.account_disabled");
        session.Cookie("trimme-access").ShouldBeNull();
    }

    [Fact]
    public async Task CodesTurnedOnLater_TheSameAccountSignsInWithACode_AndItsNumberIsConfirmed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var off = await IdentityTestData.CreateFactoryAsync(postgres, "phone_switch_on", ct, settings: CodesOff);
        var phone = IdentityTestData.NewPhone();
        using var signUp = await ApiSession.Create(off).PostAsync("/api/v1/auth/phone/sign-in", new { phone, termsAccepted = true, locale = "ar" }, ct);
        var id = (await signUp.JsonAsync(ct)).GetProperty("user").GetProperty("id").GetGuid();

        // The same database, with codes switched on.
        await using var on = new TrimmeApiFactory(off.ConnectionString);
        using (var refused = await ApiSession.Create(on).PostAsync("/api/v1/auth/phone/sign-in", new { phone, termsAccepted = true, locale = "ar" }, ct))
        {
            (await refused.ErrorCodeAsync(ct)).ShouldBe("auth.otp_required");
        }

        using var session = ApiSession.Create(on);
        var challengeId = await IdentityTestData.RequestOtpAsync(session, phone, ct);
        using var verify = await session.PostAsync("/api/v1/auth/otp/verify", new { challengeId, code = IdentityTestData.LatestOtp(on, phone) }, ct);
        verify.StatusCode.ShouldBe(HttpStatusCode.OK);
        var signedIn = await verify.JsonAsync(ct);
        signedIn.GetProperty("isNewUser").GetBoolean().ShouldBeFalse();
        signedIn.GetProperty("user").GetProperty("id").GetGuid().ShouldBe(id);

        (await ScalarAsync(on, "SELECT phone_number_confirmed FROM identity.users WHERE id = @p", ct, id)).ShouldBe(true);
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
