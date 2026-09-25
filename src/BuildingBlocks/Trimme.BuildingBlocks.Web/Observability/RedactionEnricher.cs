using Serilog.Core;
using Serilog.Events;
using Trimme.BuildingBlocks.Infrastructure.Privacy;

namespace Trimme.BuildingBlocks.Web.Observability;

/// <summary>
/// Serilog enricher that scrubs structured-log properties before they are written:
/// properties with sensitive names are replaced entirely, and string values are pattern-redacted
/// (phones, tokens, e-mails). Message templates themselves must never embed personal data.
/// </summary>
public sealed class RedactionEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        foreach (var (name, value) in logEvent.Properties.ToArray())
        {
            var redacted = RedactValue(name, value);
            if (!ReferenceEquals(redacted, value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, redacted));
            }
        }
    }

    internal static LogEventPropertyValue RedactValue(string name, LogEventPropertyValue value)
    {
        if (SensitiveDataRedactor.IsSensitiveName(name))
        {
            return new ScalarValue(SensitiveDataRedactor.Redacted);
        }

        switch (value)
        {
            case ScalarValue { Value: string text }:
                var clean = SensitiveDataRedactor.Redact(text);
                return string.Equals(clean, text, StringComparison.Ordinal) ? value : new ScalarValue(clean);

            case StructureValue structure:
                var properties = structure.Properties
                    .Select(p => new LogEventProperty(p.Name, RedactValue(p.Name, p.Value)))
                    .ToArray();
                return new StructureValue(properties, structure.TypeTag);

            case SequenceValue sequence:
                return new SequenceValue(sequence.Elements.Select(e => RedactValue(string.Empty, e)));

            case DictionaryValue dictionary:
                return new DictionaryValue(dictionary.Elements.Select(kv =>
                    new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                        kv.Key,
                        RedactValue(kv.Key.Value?.ToString() ?? string.Empty, kv.Value))));

            default:
                return value;
        }
    }
}
