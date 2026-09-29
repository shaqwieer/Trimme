using System.Globalization;
using System.Text;

namespace Trimme.BuildingBlocks.Domain.Text;

/// <summary>
/// Normalizes Arabic and English text for discovery search (D-091), so a customer finds "صالون الأصالة" by typing
/// "صالون الاصاله" and "Barber" by typing "barber":
/// <list type="bullet">
/// <item>case folding (invariant) and Unicode compatibility folding;</item>
/// <item>tashkeel (harakat, tanween, shadda, sukun, superscript alef) and tatweel removed;</item>
/// <item>alef forms (أ إ آ ٱ) become ا, ى becomes ي, ة becomes ه, ؤ becomes و and ئ becomes ي;</item>
/// <item>Arabic-Indic and Extended Arabic-Indic digits become Latin digits;</item>
/// <item>punctuation becomes a space and runs of white space collapse to one.</item>
/// </list>
/// </summary>
public static class SearchText
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var folded = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var builder = new StringBuilder(folded.Length);
        var pendingSpace = false;
        foreach (var raw in folded)
        {
            var c = Map(raw);
            if (c == '\0')
            {
                continue;
            }

            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>True when every word of <paramref name="normalizedQuery"/> occurs in <paramref name="normalizedText"/>.</summary>
    public static bool Matches(string normalizedText, string normalizedQuery)
    {
        ArgumentNullException.ThrowIfNull(normalizedText);
        if (string.IsNullOrEmpty(normalizedQuery))
        {
            return true;
        }

        return normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => normalizedText.Contains(word, StringComparison.Ordinal));
    }

    private static char Map(char c)
    {
        switch (c)
        {
            case 'أ' or 'إ' or 'آ' or 'ٱ':
                return 'ا';
            case 'ى':
                return 'ي';
            case 'ة':
                return 'ه';
            case 'ؤ':
                return 'و';
            case 'ئ':
                return 'ي';
            case 'ـ':
                return '\0';
            case >= 'ً' and <= 'ٟ':
            case 'ٰ':
                return '\0';
            case >= '٠' and <= '٩':
                return (char)('0' + (c - '٠'));
            case >= '۰' and <= '۹':
                return (char)('0' + (c - '۰'));
            case '،' or '؛' or '؟':
                return ' ';
            default:
                return CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark ? '\0' : c;
        }
    }
}
