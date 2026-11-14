using System.Text;
using System.Text.Json;

namespace Trimme.Modules.Administration.Application;

/// <summary>
/// The allowlist for the legal pages' rich text (D-144). The editor sends a ProseMirror document; this rebuilds it from
/// the allowed nodes, marks and attributes only, so nothing else ever reaches the public page (which renders it with
/// React elements, never as HTML). Unknown nodes or marks, unsafe links, too much nesting or size refuse the save.
/// </summary>
public static class LegalContent
{
    public const int MaxBytes = 200_000;
    public const int MaxTextLength = 20_000;
    private const int MaxDepth = 12;

    private static readonly string[] TopLevel = ["paragraph", "heading", "bulletList", "orderedList", "blockquote", "horizontalRule"];
    private static readonly string[] InlineNodes = ["text", "hardBreak"];
    private static readonly string[] ListItems = ["listItem"];
    private static readonly string[] InListItem = ["paragraph", "bulletList", "orderedList"];
    private static readonly string[] InQuote = ["paragraph", "heading", "bulletList", "orderedList"];
    private static readonly string[] SimpleMarks = ["bold", "italic", "underline", "strike"];

    /// <summary>The cleaned document as JSON, or <see langword="null"/> when it breaks the allowlist or holds no text.</summary>
    public static string? Normalize(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object || Type(document) != "doc")
        {
            return null;
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            var text = new StringBuilder();
            if (!WriteNode(document, writer, 0, text) || string.IsNullOrWhiteSpace(text.ToString()))
            {
                return null;
            }
        }

        return buffer.Length > MaxBytes ? null : Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string? Type(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;

    private static bool WriteNode(JsonElement node, Utf8JsonWriter writer, int depth, StringBuilder text)
    {
        var type = Type(node);
        if (depth > MaxDepth || type is null)
        {
            return false;
        }

        writer.WriteStartObject();
        writer.WriteString("type", type);
        var ok = type switch
        {
            "doc" => WriteChildren(node, writer, depth, TopLevel, text),
            "paragraph" => WriteChildren(node, writer, depth, InlineNodes, text, optional: true),
            "heading" => WriteHeading(node, writer) && WriteChildren(node, writer, depth, InlineNodes, text, optional: true),
            "bulletList" => WriteChildren(node, writer, depth, ListItems, text),
            "orderedList" => WriteStart(node, writer) && WriteChildren(node, writer, depth, ListItems, text),
            "listItem" => WriteChildren(node, writer, depth, InListItem, text),
            "blockquote" => WriteChildren(node, writer, depth, InQuote, text),
            "horizontalRule" or "hardBreak" => true,
            "text" => WriteText(node, writer, text),
            _ => false,
        };

        // A refused document is thrown away half-written, so nothing is closed after a failure.
        if (!ok)
        {
            return false;
        }

        writer.WriteEndObject();
        return true;
    }

    private static bool WriteChildren(JsonElement node, Utf8JsonWriter writer, int depth, string[] allowed, StringBuilder text, bool optional = false)
    {
        if (!node.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array || content.GetArrayLength() == 0)
        {
            return optional;
        }

        writer.WriteStartArray("content");
        foreach (var child in content.EnumerateArray())
        {
            if (!allowed.Contains(Type(child)) || !WriteNode(child, writer, depth + 1, text))
            {
                return false;
            }
        }

        writer.WriteEndArray();
        return true;
    }

    private static bool WriteHeading(JsonElement node, Utf8JsonWriter writer)
    {
        // Sections and subsections only: the page itself is the h1.
        var level = Attribute(node, "level") is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : 2;
        writer.WriteStartObject("attrs");
        writer.WriteNumber("level", Math.Clamp(level, 2, 3));
        writer.WriteEndObject();
        return true;
    }

    private static bool WriteStart(JsonElement node, Utf8JsonWriter writer)
    {
        if (Attribute(node, "start") is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var start) && start is > 1 and < 10_000)
        {
            writer.WriteStartObject("attrs");
            writer.WriteNumber("start", start);
            writer.WriteEndObject();
        }

        return true;
    }

    private static bool WriteText(JsonElement node, Utf8JsonWriter writer, StringBuilder text)
    {
        if (!node.TryGetProperty("text", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var content = value.GetString()!;
        if (content.Length is 0 or > MaxTextLength)
        {
            return false;
        }

        text.Append(content);
        writer.WriteString("text", content);
        if (!node.TryGetProperty("marks", out var marks) || marks.ValueKind != JsonValueKind.Array || marks.GetArrayLength() == 0)
        {
            return true;
        }

        writer.WriteStartArray("marks");
        foreach (var mark in marks.EnumerateArray())
        {
            var type = Type(mark);
            if (type == "link")
            {
                if (SafeHref(Attribute(mark, "href")) is not { } href)
                {
                    return false;
                }

                writer.WriteStartObject();
                writer.WriteString("type", "link");
                writer.WriteStartObject("attrs");
                writer.WriteString("href", href);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            else if (SimpleMarks.Contains(type))
            {
                writer.WriteStartObject();
                writer.WriteString("type", type);
                writer.WriteEndObject();
            }
            else
            {
                return false;
            }
        }

        writer.WriteEndArray();
        return true;
    }

    private static JsonElement? Attribute(JsonElement node, string name) =>
        node.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object && attrs.TryGetProperty(name, out var value)
            ? value
            : null;

    /// <summary>https, http, mailto and tel links, or a path on this site; nothing that could run script.</summary>
    public static string? SafeHref(JsonElement? value)
    {
        var href = value is { ValueKind: JsonValueKind.String } text ? text.GetString()?.Trim() : null;
        if (string.IsNullOrEmpty(href) || href.Length > 2000)
        {
            return null;
        }

        if (href.StartsWith('/') && !href.StartsWith("//", StringComparison.Ordinal) && !href.Contains('\\', StringComparison.Ordinal))
        {
            return href;
        }

        return Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto" or "tel" ? uri.OriginalString : null;
    }
}
