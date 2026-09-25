using System.Text.RegularExpressions;

namespace Trimme.BuildingBlocks.Infrastructure.Privacy;

/// <summary>
/// Removes personal data and secrets from text before it reaches logs (spec §7, §21).
/// This is a safety net: code must still avoid logging personal data in the first place.
/// </summary>
public static partial class SensitiveDataRedactor
{
    public const string Redacted = "[REDACTED]";
    public const string RedactedPhone = "[PHONE]";
    public const string RedactedToken = "[TOKEN]";
    public const string RedactedEmail = "[EMAIL]";

    private static readonly string[] SensitiveNameFragments =
    [
        "phone", "mobile", "whatsapp", "msisdn",
        "password", "passwd", "secret", "token", "otp", "verificationcode", "cookie", "authorization", "apikey", "api_key",
    ];

    /// <summary>True when a structured-log property or header name implies its value must never be logged.</summary>
    public static bool IsSensitiveName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var fragment in SensitiveNameFragments)
        {
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Masks phone numbers, bearer/JWT tokens and e-mail addresses found inside free text.</summary>
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var result = JwtPattern().Replace(value, RedactedToken);
        result = BearerPattern().Replace(result, "Bearer " + RedactedToken);
        result = EmailPattern().Replace(result, RedactedEmail);
        result = PhonePattern().Replace(result, RedactedPhone);
        return result;
    }

    // Three base64url segments starting with a JSON header ("eyJ").
    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}", RegexOptions.CultureInvariant)]
    private static partial Regex JwtPattern();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._~+/=-]{8,}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    // International (+ followed by 8-15 digits, optional spaces/dashes) or Saudi local mobile (05xxxxxxxx),
    // also matching Arabic-Indic digits. Not preceded/followed by other digits.
    [GeneratedRegex(
        @"(?<![\d٠-٩])(?:\+|00)[\d٠-٩](?:[\s-]?[\d٠-٩]){7,14}(?![\d٠-٩])|(?<![\d٠-٩])[0٠][5٥](?:[\s-]?[\d٠-٩]){8}(?![\d٠-٩])",
        RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
