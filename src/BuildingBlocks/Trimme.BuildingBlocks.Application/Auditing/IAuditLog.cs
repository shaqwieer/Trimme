using System.Globalization;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Application.Auditing;

/// <summary>
/// Append-only audit trail of admin and sensitive actions (spec §7, §14; R-TEN-08). <see cref="Record"/> adds the entry
/// to the current unit of work, so it is committed atomically with the change it describes (or not at all).
/// Entries never contain personal data: no phone numbers, emails or names — identifiers only.
/// </summary>
public interface IAuditLog
{
    void Record(AuditRecord record);
}

/// <param name="Action">Stable code, e.g. <c>shop.suspended</c>.</param>
/// <param name="EntityType">e.g. <c>Shop</c>.</param>
/// <param name="EntityId">The affected entity's identifier.</param>
/// <param name="ShopId">The shop concerned, when there is one.</param>
/// <param name="Summary">Short, PII-free description of what changed (e.g. <c>Draft → Active</c>), in English.</param>
/// <param name="Reason">Optional operator-supplied reason (for example for a suspension).</param>
/// <param name="SummaryCode">
/// D-155 (the client's 8-6, «سجل النشاط كله بالعربي»): the summary as a stable code the web words in the reader's language,
/// with <paramref name="SummaryArgs"/>. Null means the action itself is the code.
/// </param>
/// <param name="SummaryArgs">The values the code's message uses (statuses, counts, field names, dates as ISO 8601). PII-free.</param>
public sealed record AuditRecord(
    string Action,
    string EntityType,
    string EntityId,
    ShopId? ShopId = null,
    string? Summary = null,
    string? Reason = null,
    string? SummaryCode = null,
    IReadOnlyDictionary<string, string>? SummaryArgs = null);

/// <summary>Builds an audit summary's arguments: invariant culture, nulls left out (D-155).</summary>
public static class AuditArgs
{
    public static IReadOnlyDictionary<string, string> Of(params (string Key, object? Value)[] pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var args = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            if (Format(value) is { } text)
            {
                args[key] = text;
            }
        }

        return args;
    }

    /// <summary>A list as one argument, joined by commas (the web splits it to word each item).</summary>
    public static string List<T>(IEnumerable<T> items) => string.Join(",", items.Select(item => Format(item)).Where(s => s is not null));

    private static string? Format(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        DateTimeOffset at => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        DateOnly day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal d => d.ToString("0.##", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
