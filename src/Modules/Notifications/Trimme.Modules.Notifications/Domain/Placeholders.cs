using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Trimme.Modules.Notifications.Domain;

/// <summary>
/// The safe placeholder whitelist (spec §16, D-109). A template may use only these names, written <c>{{name}}</c>. There is
/// deliberately no placeholder for any phone number, and professionals' templates cannot use the customer's
/// manage-booking link.
/// </summary>
public static partial class Placeholders
{
    public const string CustomerName = "customer_name";
    public const string ProfessionalName = "professional_name";
    public const string ShopName = "shop_name";
    public const string ServiceName = "service_name";
    public const string BookingDate = "booking_date";
    public const string BookingTime = "booking_time";
    public const string TimeRemaining = "time_remaining";
    public const string Duration = "duration";
    public const string Amount = "amount";
    public const string Address = "address";
    public const string BookingReference = "booking_reference";
    public const string ManageUrl = "manage_url";

    public static IReadOnlyList<string> All { get; } =
    [
        CustomerName, ProfessionalName, ShopName, ServiceName, BookingDate, BookingTime, TimeRemaining, Duration, Amount, Address,
        BookingReference, ManageUrl,
    ];

    public static IReadOnlyList<string> For(MessageAudience audience) =>
        audience == MessageAudience.Customer ? All : [.. All.Where(p => p != ManageUrl)];

    /// <summary>The placeholders a body uses, in order of first appearance (the Meta parameter order).</summary>
    public static IReadOnlyList<string> Used(string body) =>
        [.. PlaceholderPattern().Matches(body).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal)];

    [GeneratedRegex(@"\{\{\s*([a-z_]+)\s*\}\}", RegexOptions.CultureInvariant)]
    internal static partial Regex PlaceholderPattern();
}

/// <summary>A validation problem: the field (<c>body</c> or <c>buttons</c>) and a stable code the editor translates.</summary>
public sealed record TemplateIssue(string Field, string Code, string? Placeholder = null);

/// <summary>Template validation (R-NTF-02): the body, the placeholder whitelist per audience, the buttons.</summary>
public static partial class TemplateValidator
{
    /// <summary>WhatsApp's limit for a template body.</summary>
    public const int MaxBodyLength = 1024;
    public const int MaxButtons = 2;
    public const int MaxButtonLabelLength = 25;
    public const int MaxProviderNameLength = 120;

    public static IReadOnlyList<TemplateIssue> Validate(MessageAudience audience, TemplateContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var issues = new List<TemplateIssue>();
        var body = content.Body;
        if (string.IsNullOrWhiteSpace(body))
        {
            issues.Add(new("body", "template.body_required"));
        }
        else if (body.Length > MaxBodyLength)
        {
            issues.Add(new("body", "template.body_too_long"));
        }

        var allowed = Placeholders.For(audience);
        foreach (var name in Placeholders.Used(body))
        {
            if (!Placeholders.All.Contains(name))
            {
                issues.Add(new("body", "template.unknown_placeholder", name));
            }
            else if (!allowed.Contains(name))
            {
                issues.Add(new("body", "template.placeholder_not_allowed", name));
            }
        }

        // Braces left after removing well-formed placeholders mean a typo such as "{{ name }" or "{customer_name}}".
        var remainder = Placeholders.PlaceholderPattern().Replace(body, string.Empty);
        if (remainder.Contains("{{", StringComparison.Ordinal) || remainder.Contains("}}", StringComparison.Ordinal))
        {
            issues.Add(new("body", "template.malformed_placeholder"));
        }

        if (content.Buttons.Count > MaxButtons)
        {
            issues.Add(new("buttons", "template.too_many_buttons"));
        }

        foreach (var button in content.Buttons)
        {
            if (string.IsNullOrWhiteSpace(button.Label) || button.Label.Length > MaxButtonLabelLength)
            {
                issues.Add(new("buttons", "template.button_label_invalid"));
            }

            if (button.Target == TemplateButtonTarget.ManageBooking && audience == MessageAudience.Professional)
            {
                issues.Add(new("buttons", "template.button_not_allowed"));
            }
        }

        if (content.Buttons.Select(b => b.Target).Distinct().Count() != content.Buttons.Count)
        {
            issues.Add(new("buttons", "template.duplicate_button"));
        }

        if (content.ProviderTemplateName is { } provider && (provider.Length > MaxProviderNameLength || !ProviderName().IsMatch(provider)))
        {
            issues.Add(new("providerTemplateName", "template.provider_name_invalid"));
        }

        return issues;
    }

    /// <summary>Meta template names are lowercase letters, digits and underscores.</summary>
    [GeneratedRegex("^[a-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderName();
}

/// <summary>Fills a body's placeholders with already formatted values (unknown names render empty).</summary>
public static class TemplateRenderer
{
    public static string Render(string body, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(values);
        return Placeholders.PlaceholderPattern().Replace(body, m => values.GetValueOrDefault(m.Groups[1].Value, string.Empty));
    }
}

/// <summary>
/// Message formatting in the reader's language, identical to the web app's rules (D-040, <c>format.ts</c>): clock times
/// and date text in Arabic-Indic digits on a 12-hour clock and the Gregorian calendar; amounts in Latin digits. The tables
/// are explicit so the output does not depend on the server's ICU data.
/// </summary>
public static class MessageFormat
{
    private static readonly string[] ArabicWeekdays = ["الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"];

    private static readonly string[] ArabicMonths =
        ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"];

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");

    public static bool IsArabic(string locale) => string.Equals(locale, "ar", StringComparison.OrdinalIgnoreCase);

    /// <summary>"الجمعة، ١٨ سبتمبر" · "Friday 18 September".</summary>
    public static string Date(DateTimeOffset instant, string timeZone, string locale)
    {
        var local = ToLocal(instant, timeZone);
        return IsArabic(locale)
            ? $"{ArabicWeekdays[(int)local.DayOfWeek]}، {ArabicDigits(local.Day.ToString(CultureInfo.InvariantCulture))} {ArabicMonths[local.Month - 1]}"
            : $"{English.DateTimeFormat.GetDayName(local.DayOfWeek)} {local.Day.ToString(CultureInfo.InvariantCulture)} {English.DateTimeFormat.GetMonthName(local.Month)}";
    }

    /// <summary>"٥:٣٠ م" · "5:30 pm".</summary>
    public static string Time(DateTimeOffset instant, string timeZone, string locale)
    {
        var local = ToLocal(instant, timeZone);
        var hour = local.Hour % 12 == 0 ? 12 : local.Hour % 12;
        var clock = $"{hour.ToString(CultureInfo.InvariantCulture)}:{local.Minute.ToString("00", CultureInfo.InvariantCulture)}";
        var pm = local.Hour >= 12;
        return IsArabic(locale) ? $"{ArabicDigits(clock)} {(pm ? "م" : "ص")}" : $"{clock} {(pm ? "pm" : "am")}";
    }

    /// <summary>"٣٠ دقيقة" · "30 min" (durations follow the clock digit rule).</summary>
    public static string Minutes(int minutes, string locale) =>
        IsArabic(locale)
            ? $"{ArabicDigits(minutes.ToString(CultureInfo.InvariantCulture))} دقيقة"
            : $"{minutes.ToString(CultureInfo.InvariantCulture)} min";

    /// <summary>"85 ر.س" · "SAR 85" (Latin digits, at most two decimals, grouped).</summary>
    public static string Amount(decimal amount, string currency, string locale)
    {
        var number = amount.ToString("#,0.##", CultureInfo.InvariantCulture);
        return IsArabic(locale)
            ? $"{number} {(currency == "SAR" ? "ر.س" : currency)}"
            : $"{currency} {number}";
    }

    public static string ArabicDigits(string latin)
    {
        var builder = new StringBuilder(latin.Length);
        foreach (var c in latin)
        {
            builder.Append(c is >= '0' and <= '9' ? (char)('٠' + (c - '0')) : c);
        }

        return builder.ToString();
    }

    private static DateTime ToLocal(DateTimeOffset instant, string timeZone) =>
        TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).DateTime;
}
