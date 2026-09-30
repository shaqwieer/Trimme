using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Notifications.Application;
using Trimme.Modules.Notifications.Infrastructure;

namespace Trimme.Modules.Notifications.Api;

/// <summary>
/// The Meta WhatsApp status webhook (R-NTF-01, D-110). Anonymous and exempt from the CSRF check, because Meta signs every
/// call instead: the <c>X-Hub-Signature-256</c> HMAC-SHA256 of the raw body with the app secret is verified in constant
/// time before anything is parsed. Without an app secret configured the webhook does not exist (404).
/// </summary>
internal static class WhatsAppWebhook
{
    public const string Path = "/webhooks/whatsapp";
    private const int MaxBodyBytes = 256 * 1024;

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(Path, Verify).AllowAnonymous().WithTags("Webhooks")
            .WithName("VerifyWhatsAppWebhook")
            .WithSummary("Meta's subscription check: echoes hub.challenge when hub.verify_token matches the configured token.")
            .Produces<string>(contentType: "text/plain").ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        api.MapPost(Path, Receive).AllowAnonymous().SkipCsrf().WithTags("Webhooks")
            .WithName("ReceiveWhatsAppWebhook")
            .WithSummary("Message status updates from Meta (sent, delivered, read, failed); the signature is verified before the body is read.")
            .Produces(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static IResult Verify(HttpRequest request, IOptions<WhatsAppOptions> options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var meta = options.Value.Meta;
        if (string.IsNullOrWhiteSpace(meta.AppSecret) || string.IsNullOrWhiteSpace(meta.VerifyToken))
        {
            return TypedResults.NotFound();
        }

        var mode = request.Query["hub.mode"].ToString();
        var token = request.Query["hub.verify_token"].ToString();
        var challenge = request.Query["hub.challenge"].ToString();
        return mode == "subscribe" && FixedEquals(token, meta.VerifyToken) && challenge.Length is > 0 and <= 200
            ? TypedResults.Text(challenge, "text/plain")
            : TypedResults.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> Receive(HttpRequest request, IOptions<WhatsAppOptions> options, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var secret = options.Value.Meta.AppSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            return TypedResults.NotFound();
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            buffer.Write(chunk, 0, read);
        }

        var body = buffer.ToArray();
        if (!IsSigned(body, request.Headers["X-Hub-Signature-256"].ToString(), secret))
        {
            return TypedResults.Unauthorized();
        }

        var statuses = Parse(body);
        if (statuses.Count > 0)
        {
            await dispatcher.Send(new ApplyProviderStatusesCommand(statuses), cancellationToken);
        }

        return TypedResults.Ok();
    }

    /// <summary>Checks <c>sha256=&lt;hex&gt;</c> against the HMAC-SHA256 of the raw body, in constant time.</summary>
    internal static bool IsSigned(byte[] body, string header, string secret)
    {
        const string prefix = "sha256=";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(header[prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>The statuses in a webhook body: <c>entry[].changes[].value.statuses[]</c> (message id, status, first error).</summary>
    internal static IReadOnlyList<ProviderStatusUpdate> Parse(byte[] body)
    {
        var updates = new List<ProviderStatusUpdate>();
        try
        {
            var root = JsonNode.Parse(body);
            foreach (var entry in root?["entry"]?.AsArray() ?? [])
            {
                foreach (var change in entry?["changes"]?.AsArray() ?? [])
                {
                    foreach (var status in change?["value"]?["statuses"]?.AsArray() ?? [])
                    {
                        var id = status?["id"]?.GetValue<string>();
                        var state = status?["status"]?.GetValue<string>();
                        if (id is null || state is null)
                        {
                            continue;
                        }

                        var error = status?["errors"]?[0];
                        updates.Add(new ProviderStatusUpdate(id, state, error is null ? null : $"meta.{error["code"]}: {error["title"]}"));
                    }
                }
            }
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return [];
        }

        return updates;
    }

    private static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
