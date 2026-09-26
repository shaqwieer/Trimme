using Shouldly;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.Modules.Identity.Application.Sessions;
using Trimme.Modules.Identity.Domain;

namespace Trimme.UnitTests.Identity;

public sealed class IdentityDomainTests
{
    [Theory]
    [InlineData("+966 50 214 8830", "+966502148830")]
    [InlineData("0502148830", "+966502148830")]
    [InlineData("502148830", "+966502148830")]
    [InlineData("966502148830", "+966502148830")]
    [InlineData("00966502148830", "+966502148830")]
    [InlineData("٠٥٠٢١٤٨٨٣٠", "+966502148830")]
    [InlineData("+966-50-214-8830", "+966502148830")]
    public void MobileNumber_NormalizesSaudiMobilesToE164(string input, string expected)
    {
        MobileNumber.TryNormalize(input, out var e164).ShouldBeTrue();
        e164.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("0412345678")]
    [InlineData("+14155550100")]
    [InlineData("+9665021488301")]
    [InlineData("05021488a0")]
    [InlineData("+966 5 0+2148830")]
    public void MobileNumber_RejectsAnythingElse(string? input)
    {
        MobileNumber.TryNormalize(input, out var e164).ShouldBeFalse();
        e164.ShouldBeNull();
    }

    [Fact]
    public void MobileNumber_MasksAllButFirstAndLastTwoDigits()
    {
        MobileNumber.Mask("+966502148830").ShouldBe("+966 5•• ••• •30");
        MobileNumber.Mask("not-a-number").ShouldBe("••••");
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36", "Chrome · Windows")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", "Safari · iOS")]
    [InlineData("Mozilla/5.0 (Linux; Android 15) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Mobile Safari/537.36 EdgA/140.0", "Chrome · Android")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 14.5; rv:130.0) Gecko/20100101 Firefox/130.0", "Firefox · macOS")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36 Edg/140.0", "Edge · Windows")]
    [InlineData("curl/8.9.1", "Unknown device")]
    [InlineData(null, "Unknown device")]
    public void DeviceLabel_IsCoarse(string? userAgent, string expected) =>
        DeviceLabels.From(userAgent).ShouldBe(expected);

    [Fact]
    public void OtpPolicy_Defaults_MatchDecisionD037()
    {
        var policy = new OtpPolicy();
        OtpPolicy.CodeLength.ShouldBe(6);
        policy.CodeLifetime.ShouldBe(TimeSpan.FromMinutes(5));
        policy.MaxAttemptsPerCode.ShouldBe(3);
    }

    [Fact]
    public void Error_details_are_copied_not_shared()
    {
        var original = IdentityErrors.OtpIncorrect(2);
        var extended = original.WithDetail("extra", 1);

        original.Details!.Keys.ShouldBe(["attemptsRemaining"]);
        extended.Details!.Keys.ShouldBe(["attemptsRemaining", "extra"], ignoreOrder: true);
        extended.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public void Session_revocation_is_idempotent_and_keeps_the_first_reason()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new UserSession(new UserSessionId(Guid.NewGuid()), Guid.NewGuid(), UserType.Customer, now, now.AddDays(30), "Chrome · Windows", null);

        session.IsActive(now).ShouldBeTrue();
        session.Revoke(SessionRevocationReason.RefreshTokenReuse, now);
        session.Revoke(SessionRevocationReason.SignedOut, now.AddMinutes(1));

        session.IsActive(now).ShouldBeFalse();
        session.RevocationReason.ShouldBe(SessionRevocationReason.RefreshTokenReuse);
        session.RevokedAt.ShouldBe(now);
    }
}
