using System.Net;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shouldly;
using Trimme.IntegrationTests.Infrastructure;

namespace Trimme.IntegrationTests.Identity;

/// <summary>Customer passwordless sign-in (D-005, D-037): R-AUTH-01 and R-AUTH-07.</summary>
public sealed class OtpSignInTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Customer_SignUp_VerifiesMobile()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "otp_signup", ct, clock);
        var phone = IdentityTestData.NewPhone();
        using var session = ApiSession.Create(factory);

        var challengeId = await IdentityTestData.RequestOtpAsync(session, phone, ct);
        var code = IdentityTestData.LatestOtp(factory, phone);
        code.Length.ShouldBe(6);

        using var verify = await session.PostAsync("/api/v1/auth/otp/verify", new { challengeId, code }, ct);
        verify.StatusCode.ShouldBe(HttpStatusCode.OK);
        var signIn = await verify.JsonAsync(ct);
        signIn.GetProperty("isNewUser").GetBoolean().ShouldBeTrue();
        var user = signIn.GetProperty("user");
        user.GetProperty("userType").GetString().ShouldBe("Customer");
        user.GetProperty("profileComplete").GetBoolean().ShouldBeFalse();
        user.GetProperty("phoneMasked").GetString().ShouldBe($"+966 5•• ••• •{phone[^2..]}");
        user.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldBe(["Customer"]);
        user.GetProperty("permissions").GetArrayLength().ShouldBe(0);

        using var complete = await session.PostAsync(
            "/api/v1/auth/profile/complete", new { displayName = "نورة القحطاني", preferredLocale = "ar", termsAccepted = true }, ct);
        complete.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await complete.JsonAsync(ct)).GetProperty("profileComplete").GetBoolean().ShouldBeTrue();

        // Signing in again with the same number (after the resend cooldown) finds the same account.
        clock.Advance(TimeSpan.FromSeconds(31));
        using var again = ApiSession.Create(factory);
        var secondChallenge = await IdentityTestData.RequestOtpAsync(again, phone, ct);
        using var secondVerify = await again.PostAsync(
            "/api/v1/auth/otp/verify", new { challengeId = secondChallenge, code = IdentityTestData.LatestOtp(factory, phone) }, ct);
        var second = await secondVerify.JsonAsync(ct);
        second.GetProperty("isNewUser").GetBoolean().ShouldBeFalse();
        second.GetProperty("user").GetProperty("id").GetGuid().ShouldBe(user.GetProperty("id").GetGuid());
        second.GetProperty("user").GetProperty("displayName").GetString().ShouldBe("نورة القحطاني");

        // Neither the number nor the code is stored in clear.
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT (SELECT count(*) FROM identity.users WHERE protected_phone LIKE '%' || @digits || '%' OR phone_lookup_hash = @phone)
                 + (SELECT count(*) FROM identity.otp_challenges WHERE code_hash = @code OR protected_phone LIKE '%' || @digits || '%')
            """,
            connection);
        command.Parameters.AddWithValue("digits", phone[1..]);
        command.Parameters.AddWithValue("phone", phone);
        command.Parameters.AddWithValue("code", code);
        ((long)(await command.ExecuteScalarAsync(ct))!).ShouldBe(0);
    }

    [Fact]
    public async Task Otp_WrongCode_CountsDownAttempts_ThenLocksTheCode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "otp_attempts", ct);
        var phone = IdentityTestData.NewPhone();
        using var session = ApiSession.Create(factory);
        var challengeId = await IdentityTestData.RequestOtpAsync(session, phone, ct);
        var code = IdentityTestData.LatestOtp(factory, phone);
        var wrong = code == "000000" ? "111111" : "000000";

        using (var first = await session.PostAsync("/api/v1/auth/otp/verify", new { challengeId, code = wrong }, ct))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = await first.JsonAsync(ct);
            problem.GetProperty("errorCode").GetString().ShouldBe("otp.incorrect");
            problem.GetProperty("attemptsRemaining").GetInt32().ShouldBe(2);
        }

        using (var second = await session.PostAsync("/api/v1/auth/otp/verify", new { challengeId, code = wrong }, ct))
        {
            (await second.JsonAsync(ct)).GetProperty("attemptsRemaining").GetInt32().ShouldBe(1);
        }

        using (var third = await session.PostAsync("/api/v1/auth/otp/verify", new { challengeId, code = wrong }, ct))
        {
            (await third.ErrorCodeAsync(ct)).ShouldBe("otp.attempts_exhausted");
        }

        // Even the right code no longer works for this challenge.
        using var correct = await session.PostAsync("/api/v1/auth/otp/verify", new { challengeId, code }, ct);
        correct.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await correct.ErrorCodeAsync(ct)).ShouldBe("otp.attempts_exhausted");
        session.Cookie("trimme-access").ShouldBeNull();
    }

    [Fact]
    public async Task Otp_ExpiresAfterFiveMinutes_AndIsSingleUse()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "otp_expiry", ct, clock);
        var phone = IdentityTestData.NewPhone();
        using var session = ApiSession.Create(factory);

        var expiring = await IdentityTestData.RequestOtpAsync(session, phone, ct);
        var expiringCode = IdentityTestData.LatestOtp(factory, phone);
        clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        using (var expired = await session.PostAsync("/api/v1/auth/otp/verify", new { challengeId = expiring, code = expiringCode }, ct))
        {
            (await expired.ErrorCodeAsync(ct)).ShouldBe("otp.expired");
        }

        var fresh = await IdentityTestData.RequestOtpAsync(session, phone, ct);
        var freshCode = IdentityTestData.LatestOtp(factory, phone);
        using (var ok = await session.PostAsync("/api/v1/auth/otp/verify", new { challengeId = fresh, code = freshCode }, ct))
        {
            ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var replay = await ApiSession.Create(factory).PostAsync("/api/v1/auth/otp/verify", new { challengeId = fresh, code = freshCode }, ct);
        (await replay.ErrorCodeAsync(ct)).ShouldBe("otp.challenge_invalid");
    }

    [Fact]
    public async Task Otp_RateLimited_PerNumber_WithResendCooldown()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "otp_limits", ct, clock);
        var phone = IdentityTestData.NewPhone();
        using var session = ApiSession.Create(factory);

        var first = await IdentityTestData.RequestOtpAsync(session, phone, ct);
        using (var tooSoon = await session.PostAsync("/api/v1/auth/otp/request", new { phone, termsAccepted = true }, ct))
        {
            tooSoon.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
            var problem = await tooSoon.JsonAsync(ct);
            problem.GetProperty("errorCode").GetString().ShouldBe("otp.resend_cooldown");
            problem.GetProperty("retryAfterSeconds").GetInt32().ShouldBeInRange(1, 30);
        }

        for (var i = 0; i < 4; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(31));
            await IdentityTestData.RequestOtpAsync(session, phone, ct);
        }

        clock.Advance(TimeSpan.FromSeconds(31));
        using (var sixth = await session.PostAsync("/api/v1/auth/otp/request", new { phone, termsAccepted = true }, ct))
        {
            sixth.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
            (await sixth.ErrorCodeAsync(ct)).ShouldBe("otp.too_many_requests");
        }

        // A newer code supersedes the older ones.
        using var superseded = await session.PostAsync(
            "/api/v1/auth/otp/verify", new { challengeId = first, code = "123456" }, ct);
        (await superseded.ErrorCodeAsync(ct)).ShouldBe("otp.challenge_invalid");

        clock.Advance(TimeSpan.FromHours(1));
        await IdentityTestData.RequestOtpAsync(session, phone, ct);
    }

    [Fact]
    public async Task Otp_RateLimited_PerClient()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(
            postgres, "otp_ip_limit", ct, settings: new Dictionary<string, string?> { ["RateLimiting:otp:PermitLimit"] = "2" });
        using var session = ApiSession.Create(factory);

        await IdentityTestData.RequestOtpAsync(session, IdentityTestData.NewPhone(), ct);
        await IdentityTestData.RequestOtpAsync(session, IdentityTestData.NewPhone(), ct);
        using var third = await session.PostAsync("/api/v1/auth/otp/request", new { phone = IdentityTestData.NewPhone(), termsAccepted = true }, ct);

        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await third.ErrorCodeAsync(ct)).ShouldBe("rate_limit.exceeded");
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("+14155550100")]
    [InlineData("0412345678")]
    public async Task Otp_Request_RejectsNonSaudiMobile(string phone)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "otp_phone", ct);
        using var session = ApiSession.Create(factory);

        using var response = await session.PostAsync("/api/v1/auth/otp/request", new { phone, termsAccepted = true }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.JsonAsync(ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("validation.failed");
        problem.GetProperty("errors").GetProperty("phone")[0].GetString().ShouldBe("validation.phone_invalid");
    }

    [Fact]
    public async Task Customer_WithoutTerms_MustAcceptThemToCompleteProfile()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "otp_terms", ct);
        var phone = IdentityTestData.NewPhone();
        using var session = ApiSession.Create(factory);
        var challengeId = await IdentityTestData.RequestOtpAsync(session, phone, ct, termsAccepted: false);
        using var verify = await session.PostAsync("/api/v1/auth/otp/verify", new { challengeId, code = IdentityTestData.LatestOtp(factory, phone) }, ct);
        verify.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var withoutTerms = await session.PostAsync(
            "/api/v1/auth/profile/complete", new { displayName = "Sara", preferredLocale = "en", termsAccepted = false }, ct);
        withoutTerms.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await withoutTerms.JsonAsync(ct)).GetProperty("errors").GetProperty("termsAccepted")[0].GetString().ShouldBe("validation.terms_required");

        using var withTerms = await session.PostAsync(
            "/api/v1/auth/profile/complete", new { displayName = "Sara", preferredLocale = "en", termsAccepted = true }, ct);
        var me = await withTerms.JsonAsync(ct);
        me.GetProperty("profileComplete").GetBoolean().ShouldBeTrue();
        me.GetProperty("preferredLocale").GetString().ShouldBe("en");
    }
}
