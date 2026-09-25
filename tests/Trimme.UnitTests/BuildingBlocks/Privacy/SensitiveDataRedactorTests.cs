using Serilog.Events;
using Shouldly;
using Trimme.BuildingBlocks.Infrastructure.Privacy;
using Trimme.BuildingBlocks.Web.Observability;

namespace Trimme.UnitTests.BuildingBlocks.Privacy;

public sealed class SensitiveDataRedactorTests
{
    [Theory]
    [InlineData("customer +966512345678 booked", "customer [PHONE] booked")]
    [InlineData("call +966 51 234 5678 now", "call [PHONE] now")]
    [InlineData("local 0512345678.", "local [PHONE].")]
    [InlineData("intl 00966512345678", "intl [PHONE]")]
    [InlineData("arabic digits ٠٥١٢٣٤٥٦٧٨", "arabic digits [PHONE]")]
    public void Redact_masks_phone_numbers(string input, string expected)
    {
        SensitiveDataRedactor.Redact(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Bearer abcdefghijklmnop1234", "Bearer [TOKEN]")]
    [InlineData("jwt eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.c2lnbmF0dXJlX3ZhbHVl end", "jwt [TOKEN] end")]
    [InlineData("mail sara@example.com please", "mail [EMAIL] please")]
    public void Redact_masks_tokens_and_emails(string input, string expected)
    {
        SensitiveDataRedactor.Redact(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("GET /api/v1/shops/3f2a/services responded 200 in 12.5 ms")]
    [InlineData("Booking TRM-2026-000123 starts 2026-09-25T17:30:00Z, price 85.00 SAR")]
    [InlineData("Order 12345 processed")]
    public void Redact_leaves_ordinary_text_untouched(string input)
    {
        SensitiveDataRedactor.Redact(input).ShouldBe(input);
    }

    [Theory]
    [InlineData("PhoneNumber", true)]
    [InlineData("customerMobile", true)]
    [InlineData("WhatsAppNumber", true)]
    [InlineData("RefreshToken", true)]
    [InlineData("OtpCode", true)]
    [InlineData("Password", true)]
    [InlineData("StatusCode", false)]
    [InlineData("ErrorCode", false)]
    [InlineData("ShopId", false)]
    [InlineData(null, false)]
    public void IsSensitiveName_flags_personal_and_secret_fields(string? name, bool expected)
    {
        SensitiveDataRedactor.IsSensitiveName(name).ShouldBe(expected);
    }

    [Fact]
    public void Enricher_replaces_sensitive_properties_and_scrubs_text()
    {
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            MessageTemplate.Empty,
            [
                new LogEventProperty("PhoneNumber", new ScalarValue("+966512345678")),
                new LogEventProperty("Note", new ScalarValue("reach me at 0512345678")),
                new LogEventProperty("ShopId", new ScalarValue("shop-1")),
                new LogEventProperty("Payload", new StructureValue(
                [
                    new LogEventProperty("Mobile", new ScalarValue("0512345678")),
                    new LogEventProperty("Name", new ScalarValue("Sara")),
                ])),
            ]);

        new RedactionEnricher().Enrich(logEvent, propertyFactory: null!);

        Scalar(logEvent, "PhoneNumber").ShouldBe(SensitiveDataRedactor.Redacted);
        Scalar(logEvent, "Note").ShouldBe("reach me at [PHONE]");
        Scalar(logEvent, "ShopId").ShouldBe("shop-1");
        var payload = (StructureValue)logEvent.Properties["Payload"];
        ((ScalarValue)payload.Properties.Single(p => p.Name == "Mobile").Value).Value.ShouldBe(SensitiveDataRedactor.Redacted);
        ((ScalarValue)payload.Properties.Single(p => p.Name == "Name").Value).Value.ShouldBe("Sara");
    }

    private static object? Scalar(LogEvent logEvent, string name) => ((ScalarValue)logEvent.Properties[name]).Value;
}
