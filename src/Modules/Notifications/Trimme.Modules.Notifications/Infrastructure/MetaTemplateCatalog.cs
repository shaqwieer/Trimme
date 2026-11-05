using System.Globalization;
using System.Text.Json.Nodes;
using Trimme.Modules.Notifications.Application;
using Trimme.Modules.Notifications.Domain;
using Trimme.Modules.Notifications.Infrastructure.Seeding;

namespace Trimme.Modules.Notifications.Infrastructure;

/// <summary>
/// The templates to register with Meta (D-140). Each entry is the body of <c>POST /{waba-id}/message_templates</c>:
/// <list type="bullet">
/// <item>every default slot (<see cref="DefaultTemplates"/>) as a UTILITY template. Its placeholders are numbered
/// <c>{{1}}…{{n}}</c> in order of first appearance, the order <see cref="MetaCloudApiProvider"/> sends the values in. The
/// examples are the admin preview's sample values, and URL buttons are dynamic suffixes of the button base URL;</item>
/// <item>the AUTHENTICATION template for sign-in codes (copy-code button), in Arabic and English.</item>
/// </list>
/// <c>docs/whatsapp/meta-templates.json</c> is generated from it (a unit-test snapshot), and
/// <c>tools/whatsapp/register-meta-templates.mjs</c> submits that file.
/// </summary>
internal static class MetaTemplateCatalog
{
    /// <summary>The authentication template, set as <c>WhatsApp__Meta__AuthenticationTemplateName</c>.</summary>
    public const string AuthenticationTemplateName = "trimme_sign_in_code";

    /// <summary>Shown in the authentication template's footer; the Identity OTP policy's code lifetime (5 minutes).</summary>
    public const int CodeExpirationMinutes = 5;

    /// <summary>The reminder offset used for the examples (the platform default).</summary>
    private const int SampleReminderMinutes = 30;

    /// <param name="buttonBaseUrl">The registered button base URL, ending with a slash (<c>WhatsApp__Meta__ButtonBaseUrl</c>).</param>
    /// <param name="sampleAt">The instant the sample booking is built from (it starts the next day at 17:30 Riyadh time).</param>
    public static JsonArray Build(string buttonBaseUrl, DateTimeOffset sampleAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buttonBaseUrl);
        var templates = new JsonArray();
        foreach (var (key, content) in DefaultTemplates.All)
        {
            templates.Add(Utility(key, content, buttonBaseUrl, sampleAt));
        }

        foreach (var locale in WhatsAppTemplate.Locales)
        {
            templates.Add(Authentication(locale));
        }

        return templates;
    }

    /// <summary>The body with each placeholder replaced by its 1-based position in <see cref="Placeholders.Used"/>.</summary>
    public static string NumberedBody(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var numbers = Placeholders.Used(body)
            .Select((name, index) => (name, number: index + 1))
            .ToDictionary(p => p.name, p => p.number, StringComparer.Ordinal);
        return Placeholders.PlaceholderPattern()
            .Replace(body, m => "{{" + numbers[m.Groups[1].Value].ToString(CultureInfo.InvariantCulture) + "}}");
    }

    private static JsonObject Utility(TemplateKey key, TemplateContent content, string buttonBaseUrl, DateTimeOffset sampleAt)
    {
        var (booking, shop) = SampleMessage.For(key.Locale, sampleAt);
        var values = MessageComposer.Values(booking, shop, key.Audience, key.Locale, SampleReminderMinutes, buttonBaseUrl);
        var urls = MessageComposer.Urls(booking, shop, key.Audience, key.Locale, buttonBaseUrl);

        var used = Placeholders.Used(content.Body);
        var body = new JsonObject { ["type"] = "BODY", ["text"] = NumberedBody(content.Body) };
        if (used.Count > 0)
        {
            body["example"] = new JsonObject
            {
                ["body_text"] = new JsonArray(
                    new JsonArray([.. used.Select(p => (JsonNode?)JsonValue.Create(MetaCloudApiProvider.ParameterText(values.GetValueOrDefault(p))))])),
            };
        }

        var components = new JsonArray(body);

        // The same buttons, in the same order, as a rendered message (RenderedMessage.From), so the indexes match.
        var buttons = content.Buttons.Where(b => urls.ContainsKey(b.Target)).ToList();
        if (buttons.Count > 0)
        {
            components.Add(new JsonObject
            {
                ["type"] = "BUTTONS",
                ["buttons"] = new JsonArray([.. buttons.Select(b => (JsonNode)new JsonObject
                {
                    ["type"] = "URL",
                    ["text"] = b.Label,
                    ["url"] = buttonBaseUrl + "{{1}}",
                    ["example"] = new JsonArray(JsonValue.Create(urls[b.Target])),
                })]),
            });
        }

        return new JsonObject
        {
            ["name"] = content.ProviderTemplateName,
            ["language"] = key.Locale,
            ["category"] = "UTILITY",
            ["parameter_format"] = "positional",
            ["components"] = components,
        };
    }

    private static JsonObject Authentication(string locale) => new()
    {
        ["name"] = AuthenticationTemplateName,
        ["language"] = locale,
        ["category"] = "AUTHENTICATION",
        ["components"] = new JsonArray(
            new JsonObject { ["type"] = "BODY", ["add_security_recommendation"] = true },
            new JsonObject { ["type"] = "FOOTER", ["code_expiration_minutes"] = CodeExpirationMinutes },
            new JsonObject
            {
                ["type"] = "BUTTONS",
                ["buttons"] = new JsonArray(new JsonObject { ["type"] = "OTP", ["otp_type"] = "COPY_CODE" }),
            }),
    };
}
