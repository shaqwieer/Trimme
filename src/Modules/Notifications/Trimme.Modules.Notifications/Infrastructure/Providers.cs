using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Domain.Privacy;
using Trimme.BuildingBlocks.Infrastructure.Privacy;
using Trimme.Modules.Notifications.Application;

namespace Trimme.Modules.Notifications.Infrastructure;

internal enum WhatsAppProviderKind
{
    /// <summary>No channel: every send fails permanently with <c>whatsapp.not_configured</c> (production until Meta is set up).</summary>
    None,

    /// <summary>Development and Testing only: records messages in memory and reports them delivered.</summary>
    Fake,

    /// <summary>The Meta WhatsApp Business Cloud API (credentials from environment variables only).</summary>
    Meta,
}

/// <summary>WhatsApp settings (spec §16, D-110). Bound from <c>WhatsApp</c>; secrets only from the environment.</summary>
internal sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    /// <summary>When unset: <see cref="WhatsAppProviderKind.Fake"/> in Development and Testing, otherwise <see cref="WhatsAppProviderKind.None"/>.</summary>
    public WhatsAppProviderKind? Provider { get; set; }

    public MetaCloudOptions Meta { get; set; } = new();

    public WhatsAppProviderKind Resolve(IHostEnvironment environment) =>
        Provider ?? (IsLocal(environment) ? WhatsAppProviderKind.Fake : WhatsAppProviderKind.None);

    public static bool IsLocal(IHostEnvironment environment) => environment.IsDevelopment() || environment.IsEnvironment("Testing");
}

/// <summary>Meta Cloud API settings (<c>WhatsApp__Meta__*</c> environment variables; never committed).</summary>
internal sealed class MetaCloudOptions
{
    public string GraphApiBaseUrl { get; set; } = "https://graph.facebook.com";

    public string GraphApiVersion { get; set; } = "v21.0";

    public string? PhoneNumberId { get; set; }

    public string? AccessToken { get; set; }

    /// <summary>Signs the status webhook (<c>X-Hub-Signature-256</c>); the webhook answers 404 without it.</summary>
    public string? AppSecret { get; set; }

    /// <summary>The token Meta echoes when it verifies the webhook subscription.</summary>
    public string? VerifyToken { get; set; }

    /// <summary>The approved authentication template used for sign-in codes (copy-code button).</summary>
    public string? AuthenticationTemplateName { get; set; }

    /// <summary>
    /// The base URL registered for the templates' URL buttons; the platform sends the rest of the link as the button's
    /// dynamic suffix. Usually the public web address followed by a slash.
    /// </summary>
    public string? ButtonBaseUrl { get; set; }
}

/// <summary>What the fake provider recorded (development and tests): masked recipient and text, never the number or a code.</summary>
internal sealed record FakeWhatsAppMessage(string ProviderMessageId, string RecipientMasked, string? TemplateName, string Body, bool Authentication, DateTimeOffset At);

internal sealed class FakeWhatsAppInbox
{
    private const int Capacity = 500;
    private readonly ConcurrentQueue<FakeWhatsAppMessage> _messages = new();

    public void Add(FakeWhatsAppMessage message)
    {
        _messages.Enqueue(message);
        while (_messages.Count > Capacity && _messages.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<FakeWhatsAppMessage> All() => [.. _messages];
}

/// <summary>
/// Development and Testing provider (R-NTF-01): records each message (masked recipient) in memory and reports it
/// delivered. Numbers ending in <c>0000</c> fail transiently and numbers ending in <c>9999</c> fail permanently, so retries
/// and failures can be exercised without a real provider.
/// </summary>
internal sealed partial class FakeWhatsAppProvider(FakeWhatsAppInbox inbox, TimeProvider clock, ILogger<FakeWhatsAppProvider> logger) : IWhatsAppProvider
{
    public const string TransientFailureSuffix = "0000";
    public const string PermanentFailureSuffix = "9999";

    public string Name => "Fake";

    public Task<WhatsAppSendResult> SendAsync(WhatsAppOutgoing message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.ToE164.EndsWith(TransientFailureSuffix, StringComparison.Ordinal))
        {
            return Task.FromResult(WhatsAppSendResult.Transient("fake.transient: simulated temporary failure"));
        }

        if (message.ToE164.EndsWith(PermanentFailureSuffix, StringComparison.Ordinal))
        {
            return Task.FromResult(WhatsAppSendResult.Refused("fake.rejected: simulated invalid recipient"));
        }

        var id = $"fake-{Guid.CreateVersion7():N}";
        inbox.Add(new FakeWhatsAppMessage(id, Mask(message.ToE164), message.TemplateName, message.Body, Authentication: false, clock.GetUtcNow()));
        LogRecorded(logger);
        return Task.FromResult(WhatsAppSendResult.Ok(id, delivered: true));
    }

    public Task<WhatsAppSendResult> SendAuthenticationCodeAsync(string toE164, string code, string locale, CancellationToken cancellationToken)
    {
        var id = $"fake-{Guid.CreateVersion7():N}";

        // The code itself is never recorded (R-NTF: sign-in codes are not stored or logged).
        inbox.Add(new FakeWhatsAppMessage(id, Mask(toE164), "authentication", string.Empty, Authentication: true, clock.GetUtcNow()));
        return Task.FromResult(WhatsAppSendResult.Ok(id, delivered: true));
    }

    private static string Mask(string e164) => PhoneNumber.TryParse(e164, out var phone) ? phone.Masked : "•••";

    [LoggerMessage(Level = LogLevel.Information, Message = "WhatsApp message recorded by the fake provider")]
    private static partial void LogRecorded(ILogger logger);
}

/// <summary>Used when no provider is configured: every message fails permanently, so admins see it in the log.</summary>
internal sealed class UnconfiguredWhatsAppProvider : IWhatsAppProvider
{
    public string Name => "None";

    public Task<WhatsAppSendResult> SendAsync(WhatsAppOutgoing message, CancellationToken cancellationToken) =>
        Task.FromResult(WhatsAppSendResult.Refused("whatsapp.not_configured: no WhatsApp provider is configured"));

    public Task<WhatsAppSendResult> SendAuthenticationCodeAsync(string toE164, string code, string locale, CancellationToken cancellationToken) =>
        Task.FromResult(WhatsAppSendResult.Refused("whatsapp.not_configured: no WhatsApp provider is configured"));
}

/// <summary>
/// The Meta WhatsApp Business Cloud API (R-NTF-01, D-110): sends an approved template by name with the placeholder values
/// in body order, and URL buttons as dynamic suffixes of the registered button base URL. 429 and 5xx answers and network
/// errors are transient; other 4xx answers are permanent. It never logs the token, the number or the text; errors keep
/// Meta's code and a redacted title only.
/// </summary>
internal sealed partial class MetaCloudApiProvider(HttpClient http, IOptions<WhatsAppOptions> options, ILogger<MetaCloudApiProvider> logger) : IWhatsAppProvider
{
    public string Name => "Meta";

    private MetaCloudOptions Meta => options.Value.Meta;

    public async Task<WhatsAppSendResult> SendAsync(WhatsAppOutgoing message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(message.TemplateName))
        {
            return WhatsAppSendResult.Refused("whatsapp.template_name_missing: the template version has no approved Meta template name");
        }

        var components = new JsonArray();
        if (message.Parameters.Count > 0)
        {
            components.Add(new JsonObject
            {
                ["type"] = "body",
                ["parameters"] = new JsonArray([.. message.Parameters.Select(p => (JsonNode)new JsonObject { ["type"] = "text", ["text"] = p })]),
            });
        }

        for (var index = 0; index < message.Buttons.Count; index++)
        {
            components.Add(new JsonObject
            {
                ["type"] = "button",
                ["sub_type"] = "url",
                ["index"] = index.ToString(CultureInfo.InvariantCulture),
                ["parameters"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = ButtonSuffix(message.Buttons[index].Url) }),
            });
        }

        return await PostAsync(Template(message.ToE164, message.TemplateName, message.Locale, components), cancellationToken);
    }

    public async Task<WhatsAppSendResult> SendAuthenticationCodeAsync(string toE164, string code, string locale, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Meta.AuthenticationTemplateName))
        {
            return WhatsAppSendResult.Refused("whatsapp.template_name_missing: no authentication template is configured");
        }

        var components = new JsonArray(
            new JsonObject { ["type"] = "body", ["parameters"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = code }) },
            new JsonObject
            {
                ["type"] = "button",
                ["sub_type"] = "url",
                ["index"] = "0",
                ["parameters"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = code }),
            });
        return await PostAsync(Template(toE164, Meta.AuthenticationTemplateName, locale, components), cancellationToken);
    }

    private static JsonObject Template(string toE164, string name, string locale, JsonArray components) => new()
    {
        ["messaging_product"] = "whatsapp",
        ["recipient_type"] = "individual",
        ["to"] = toE164.TrimStart('+'),
        ["type"] = "template",
        ["template"] = new JsonObject
        {
            ["name"] = name,
            ["language"] = new JsonObject { ["code"] = locale },
            ["components"] = components,
        },
    };

    private async Task<WhatsAppSendResult> PostAsync(JsonObject body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Meta.PhoneNumberId) || string.IsNullOrWhiteSpace(Meta.AccessToken))
        {
            return WhatsAppSendResult.Refused("whatsapp.not_configured: WhatsApp__Meta__PhoneNumberId and WhatsApp__Meta__AccessToken are required");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri($"{Meta.GraphApiBaseUrl.TrimEnd('/')}/{Meta.GraphApiVersion}/{Meta.PhoneNumberId}/messages"))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Meta.AccessToken);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            LogUnreachable(logger, exception.GetType().Name);
            return WhatsAppSendResult.Transient($"meta.unreachable: {exception.GetType().Name}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return WhatsAppSendResult.Transient("meta.timeout");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var id = TryRead(text, root => root["messages"]?[0]?["id"]?.GetValue<string>());
                return id is null ? WhatsAppSendResult.Transient("meta.unexpected_response") : WhatsAppSendResult.Ok(id);
            }

            var code = TryRead(text, root => root["error"]?["code"]?.ToString()) ?? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
            var title = TryRead(text, root => root["error"]?["message"]?.GetValue<string>()) ?? response.ReasonPhrase ?? "error";
            var error = SensitiveDataRedactor.Redact($"meta.{code}: {title}");
            LogRefused(logger, (int)response.StatusCode, code);
            return response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500
                ? WhatsAppSendResult.Transient(error)
                : WhatsAppSendResult.Refused(error);
        }
    }

    private string ButtonSuffix(string url)
    {
        var baseUrl = Meta.ButtonBaseUrl;
        return !string.IsNullOrEmpty(baseUrl) && url.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase) ? url[baseUrl.Length..] : url;
    }

    private static string? TryRead(string json, Func<JsonNode, string?> read)
    {
        try
        {
            return JsonNode.Parse(json) is { } root ? read(root) : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Meta Cloud API was unreachable ({ErrorType})")]
    private static partial void LogUnreachable(ILogger logger, string errorType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Meta Cloud API refused a message: HTTP {Status}, error {Code}")]
    private static partial void LogRefused(ILogger logger, int status, string code);
}

/// <summary><see cref="IWhatsAppAuthenticationSender"/> over the configured provider (the Identity module's WhatsApp OTP sender).</summary>
internal sealed class WhatsAppAuthenticationSender(IWhatsAppProvider provider) : IWhatsAppAuthenticationSender
{
    public async Task<bool> TrySendCodeAsync(string phoneE164, string code, string locale, CancellationToken cancellationToken) =>
        (await provider.SendAuthenticationCodeAsync(phoneE164, code, locale, cancellationToken)).Accepted;
}
