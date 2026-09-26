using System.Diagnostics.CodeAnalysis;
using System.Text;
using PhoneNumbers;

namespace Trimme.BuildingBlocks.Domain.Privacy;

/// <summary>
/// A validated phone number in E.164 (spec §8, D-026, R-TEN-07), parsed with libphonenumber. Latin, Arabic-Indic and
/// Extended Arabic-Indic digits are accepted. <see cref="ToString"/> returns the masked form, so a phone never leaks
/// into logs or messages by accident; read <see cref="E164"/> explicitly where the full number is required.
/// Used for professional WhatsApp numbers (Phase 06). Customer sign-in keeps its Saudi-mobile rule (D-060), whose
/// normalised output is identical for every number both accept.
/// </summary>
public sealed class PhoneNumber : IEquatable<PhoneNumber>
{
    public const string DefaultRegion = "SA";

    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    private PhoneNumber(string e164, int countryCode)
    {
        E164 = e164;
        CountryCode = countryCode;
    }

    /// <summary>The normalised number, e.g. <c>+966502148830</c>. Personal data: store encrypted, never log.</summary>
    public string E164 { get; }

    public int CountryCode { get; }

    /// <summary>All digits hidden except the first national digit and the last two: <c>+966 5•• ••• •30</c>.</summary>
    public string Masked
    {
        get
        {
            var prefix = $"+{CountryCode}";
            var national = E164[prefix.Length..];
            if (national.Length < 4)
            {
                return $"{prefix} ••••";
            }

            var hidden = new string('•', national.Length - 3);
            var body = $"{national[0]}{hidden}{national[^2..]}";
            // Group like the design (3-3-4 for Saudi mobiles: 5•• ••• •30).
            return national.Length == 9
                ? $"{prefix} {body[..3]} {body[3..6]} {body[6..]}"
                : $"{prefix} {body}";
        }
    }

    /// <summary>Parses a number; national formats are read in <paramref name="defaultRegion"/>.</summary>
    public static bool TryParse(string? input, [NotNullWhen(true)] out PhoneNumber? phone, string defaultRegion = DefaultRegion)
    {
        phone = null;
        if (string.IsNullOrWhiteSpace(input) || input.Length > 40)
        {
            return false;
        }

        try
        {
            var parsed = Util.Parse(LatinDigits(input), defaultRegion);
            if (!Util.IsValidNumber(parsed))
            {
                return false;
            }

            phone = new PhoneNumber(Util.Format(parsed, PhoneNumberFormat.E164), parsed.CountryCode);
            return true;
        }
        catch (NumberParseException)
        {
            return false;
        }
    }

    /// <summary>Like <see cref="TryParse"/>, but only numbers that can receive WhatsApp/SMS (mobile or mobile-capable).</summary>
    public static bool TryParseMobile(string? input, [NotNullWhen(true)] out PhoneNumber? phone, string defaultRegion = DefaultRegion)
    {
        if (TryParse(input, out phone, defaultRegion))
        {
            var type = Util.GetNumberType(Util.Parse(phone.E164, defaultRegion));
            if (type is PhoneNumberType.MOBILE or PhoneNumberType.FIXED_LINE_OR_MOBILE)
            {
                return true;
            }
        }

        phone = null;
        return false;
    }

    public override string ToString() => Masked;

    public bool Equals(PhoneNumber? other) => other is not null && string.Equals(E164, other.E164, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as PhoneNumber);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(E164);

    private static string LatinDigits(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            builder.Append(ch switch
            {
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                _ => ch,
            });
        }

        return builder.ToString();
    }
}
