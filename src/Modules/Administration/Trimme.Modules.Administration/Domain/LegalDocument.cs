using Trimme.BuildingBlocks.Domain.Primitives;

namespace Trimme.Modules.Administration.Domain;

public readonly record struct LegalDocumentId(Guid Value) : IEntityId<LegalDocumentId>
{
    public static LegalDocumentId From(Guid value) => new(value);
}

/// <summary>The two legal pages the platform admin writes (D-144).</summary>
public enum LegalDocumentKind
{
    Terms,
    Privacy,
}

/// <summary>
/// The terms of use or the privacy policy in one language, as the admin wrote it (D-144). <see cref="Content"/> is a
/// rich-text document (ProseMirror JSON) that passed the allowlist on save: headings, paragraphs, lists, quotes, bold,
/// italic, underline, strike and safe links; never HTML. Without a row the site shows its built-in text.
/// </summary>
public sealed class LegalDocument : Entity<LegalDocumentId>, IPublicContent
{
    private LegalDocument(LegalDocumentId id, LegalDocumentKind kind, string locale, string content, DateTimeOffset updatedAt, Guid? updatedBy)
        : base(id)
    {
        Kind = kind;
        Locale = locale;
        Content = content;
        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }

    public LegalDocumentKind Kind { get; private set; }

    public string Locale { get; private set; }

    public string Content { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static LegalDocument Create(LegalDocumentKind kind, string locale, string content, DateTimeOffset now, Guid? by) =>
        new(new LegalDocumentId(Guid.CreateVersion7()), kind, locale, content, now, by);

    public void Replace(string content, DateTimeOffset now, Guid? by)
    {
        Content = content;
        UpdatedAt = now;
        UpdatedBy = by;
    }
}
