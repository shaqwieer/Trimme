using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Trimme.Modules.Notifications.Api;
using Trimme.Modules.Notifications.Application;
using Trimme.Modules.Notifications.Domain;
using Trimme.Modules.Notifications.Infrastructure;

namespace Trimme.UnitTests.Notifications;

/// <summary>R-NTF-01: the Meta Cloud API adapter against a mocked HTTP handler, and the webhook signature check.</summary>
public sealed class WhatsAppProviderTests
{
    private static readonly WhatsAppOutgoing Message = new(
        "+966512345678", "ar", "trimme_customer_booking_confirmed", "rendered text", ["سارة", "٥:٣٠ م"],
        [new RenderedButton("إدارة الموعد", "https://trimme.sa/ar/account/bookings/0199")]);

    [Fact]
    public async Task Meta_SendsTheApprovedTemplate_WithParametersInOrder_AndReadsTheMessageId()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"messaging_product":"whatsapp","messages":[{"id":"wamid.HBgM"}]}""");
        var result = await Provider(handler).SendAsync(Message, CancellationToken.None);

        result.ShouldBe(WhatsAppSendResult.Ok("wamid.HBgM"));
        handler.Request!.RequestUri!.ToString().ShouldBe("https://graph.test/v21.0/1234567890/messages");
        handler.Request.Headers.Authorization!.ToString().ShouldBe("Bearer test-token");
        var body = JsonNode.Parse(handler.Body!)!;
        body["to"]!.GetValue<string>().ShouldBe("966512345678");
        body["type"]!.GetValue<string>().ShouldBe("template");
        body["template"]!["name"]!.GetValue<string>().ShouldBe("trimme_customer_booking_confirmed");
        body["template"]!["language"]!["code"]!.GetValue<string>().ShouldBe("ar");
        var components = body["template"]!["components"]!.AsArray();
        components[0]!["parameters"]!.AsArray().Select(p => p!["text"]!.GetValue<string>()).ShouldBe(["سارة", "٥:٣٠ م"]);
        components[1]!["sub_type"]!.GetValue<string>().ShouldBe("url");
        components[1]!["parameters"]![0]!["text"]!.GetValue<string>().ShouldBe("ar/account/bookings/0199", "the dynamic suffix after the registered base URL");
        handler.Body!.ShouldNotContain("rendered text", Case.Sensitive, "Meta renders the approved template; the local text is not sent");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task Meta_ClientErrorsArePermanent_RateLimitsAndServerErrorsTransient(HttpStatusCode status, bool permanent)
    {
        var handler = new RecordingHandler(status, """{"error":{"message":"(#131026) Message undeliverable to +966512345678","code":131026}}""");
        var result = await Provider(handler).SendAsync(Message, CancellationToken.None);

        result.Accepted.ShouldBeFalse();
        result.Permanent.ShouldBe(permanent);
        result.Error!.ShouldStartWith("meta.131026");
        result.Error.ShouldNotContain("512345678", Case.Sensitive, "a number in the provider's text is redacted");
    }

    [Fact]
    public async Task Meta_WithoutATemplateName_OrCredentials_RefusesWithoutCallingOut()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        (await Provider(handler).SendAsync(Message with { TemplateName = null }, CancellationToken.None)).Error!.ShouldStartWith("whatsapp.template_name_missing");
        (await Provider(handler, token: null).SendAsync(Message, CancellationToken.None)).Error!.ShouldStartWith("whatsapp.not_configured");
        handler.Request.ShouldBeNull();
    }

    [Fact]
    public async Task Meta_AuthenticationCode_GoesInTheBodyAndTheCopyButton()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"messages":[{"id":"wamid.otp"}]}""");
        (await Provider(handler).SendAuthenticationCodeAsync("+966512345678", "482913", "ar", CancellationToken.None)).Accepted.ShouldBeTrue();
        var template = JsonNode.Parse(handler.Body!)!["template"]!;
        template["name"]!.GetValue<string>().ShouldBe("trimme_sign_in_code");
        template["components"]![0]!["parameters"]![0]!["text"]!.GetValue<string>().ShouldBe("482913");
        template["components"]![1]!["parameters"]![0]!["text"]!.GetValue<string>().ShouldBe("482913");
    }

    [Fact]
    public async Task Fake_RecordsMaskedRecipients_SimulatesFailures_AndNeverStoresACode()
    {
        var inbox = new FakeWhatsAppInbox();
        var fake = new FakeWhatsAppProvider(inbox, TimeProvider.System, NullLogger<FakeWhatsAppProvider>.Instance);
        (await fake.SendAsync(Message, CancellationToken.None)).ShouldSatisfyAllConditions(r => r.Accepted.ShouldBeTrue(), r => r.Delivered.ShouldBeTrue());
        (await fake.SendAsync(Message with { ToE164 = "+966512340000" }, CancellationToken.None)).Permanent.ShouldBeFalse();
        (await fake.SendAsync(Message with { ToE164 = "+966512349999" }, CancellationToken.None)).Permanent.ShouldBeTrue();
        await fake.SendAuthenticationCodeAsync("+966512345678", "482913", "ar", CancellationToken.None);

        inbox.All().Count.ShouldBe(2);
        inbox.All().ShouldAllBe(m => !m.RecipientMasked.Contains("12345678"));
        inbox.All().ShouldNotContain(m => m.Body.Contains("482913"));
    }

    [Fact]
    public void Webhook_SignatureIsTheHmacOfTheRawBody_AndStatusesAreParsed()
    {
        var body = Encoding.UTF8.GetBytes("""
            {"entry":[{"changes":[{"value":{"statuses":[
              {"id":"wamid.1","status":"delivered"},
              {"id":"wamid.2","status":"failed","errors":[{"code":131026,"title":"Message undeliverable"}]}]}}]}]}
            """);
        var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("app-secret"), body)).ToLowerInvariant();

        WhatsAppWebhook.IsSigned(body, signature, "app-secret").ShouldBeTrue();
        WhatsAppWebhook.IsSigned(body, signature, "another-secret").ShouldBeFalse();
        WhatsAppWebhook.IsSigned([.. body, (byte)' '], signature, "app-secret").ShouldBeFalse("any change to the body breaks the signature");
        WhatsAppWebhook.IsSigned(body, "sha1=abc", "app-secret").ShouldBeFalse();
        WhatsAppWebhook.IsSigned(body, "sha256=not-hex", "app-secret").ShouldBeFalse();

        WhatsAppWebhook.Parse(body).ShouldBe(
        [
            new ProviderStatusUpdate("wamid.1", "delivered", null),
            new ProviderStatusUpdate("wamid.2", "failed", "meta.131026: Message undeliverable"),
        ]);
        WhatsAppWebhook.Parse("not json"u8.ToArray()).ShouldBeEmpty();
    }

    private static MetaCloudApiProvider Provider(RecordingHandler handler, string? token = "test-token") =>
        new(
            new HttpClient(handler),
            Options.Create(new WhatsAppOptions
            {
                Provider = WhatsAppProviderKind.Meta,
                Meta = new MetaCloudOptions
                {
                    GraphApiBaseUrl = "https://graph.test",
                    GraphApiVersion = "v21.0",
                    PhoneNumberId = "1234567890",
                    AccessToken = token,
                    AuthenticationTemplateName = "trimme_sign_in_code",
                    ButtonBaseUrl = "https://trimme.sa/",
                },
            }),
            NullLogger<MetaCloudApiProvider>.Instance);

    private sealed class RecordingHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
