using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Trimme.Modules.Identity.Domain;

/// <summary>
/// Saudi mobile number normalised to E.164 (<c>+9665XXXXXXXX</c>). The design fixes the <c>+966</c> prefix (D-048);
/// Phase 05 replaces this with the platform-wide phone value object (libphonenumber) without changing stored values.
/// Accepts Latin or Arabic-Indic digits, spaces and dashes, and the forms <c>+9665…</c>, <c>009665…</c>,
/// <c>9665…</c>, <c>05…</c> and <c>5…</c>.
/// </summary>
public static class MobileNumber
{
    public static bool TryNormalize(string? input, [NotNullWhen(true)] out string? e164)
    {
        e164 = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var digits = new StringBuilder(input.Length);
        var leadingPlus = false;
        foreach (var ch in input.Trim())
        {
            if (ch is >= '0' and <= '9')
            {
                digits.Append(ch);
            }
            else if (ch is >= '٠' and <= '٩')
            {
                digits.Append((char)('0' + (ch - '٠')));
            }
            else if (ch is >= '۰' and <= '۹')
            {
                digits.Append((char)('0' + (ch - '۰')));
            }
            else if (ch == '+' && digits.Length == 0 && !leadingPlus)
            {
                leadingPlus = true;
            }
            else if (ch is not (' ' or '-' or '(' or ')' or ' '))
            {
                return false;
            }
        }

        var value = digits.ToString();
        if (!leadingPlus && value.StartsWith("00", StringComparison.Ordinal))
        {
            value = value[2..];
            leadingPlus = true;
        }

        string national;
        if (value.StartsWith("966", StringComparison.Ordinal) && (leadingPlus || value.Length == 12))
        {
            national = value[3..];
        }
        else if (leadingPlus)
        {
            return false;
        }
        else if (value.StartsWith('0'))
        {
            national = value[1..];
        }
        else
        {
            national = value;
        }

        if (national.Length != 9 || national[0] != '5')
        {
            return false;
        }

        e164 = "+966" + national;
        return true;
    }

    /// <summary>Masks all but the first national digit and the last two (D-026): <c>+966 5•• ••• •12</c>.</summary>
    public static string Mask(string e164)
    {
        ArgumentNullException.ThrowIfNull(e164);
        if (e164.Length != 13 || !e164.StartsWith("+966", StringComparison.Ordinal))
        {
            return "••••";
        }

        return $"+966 {e164[4]}•• ••• •{e164[^2..]}";
    }
}
