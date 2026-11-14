using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Administration.Domain;

namespace Trimme.Modules.Administration.Application.Admin;

// The terms of use and privacy policy, written by the platform admin in rich text per language (D-144). Without a
// saved document the site shows its built-in text, so "restore" simply deletes the row.

/// <summary>One saved legal page: <c>Kind</c> is <c>terms</c> or <c>privacy</c>; <c>Content</c> is the cleaned rich-text document.</summary>
public sealed record LegalDocumentResponse(string Kind, string Locale, JsonElement Content, DateTimeOffset UpdatedAt);

internal sealed record ListLegalDocumentsQuery : IQuery<IReadOnlyList<LegalDocumentResponse>>;

internal sealed record GetPublicLegalDocumentQuery(string Kind, string Locale) : IQuery<LegalDocumentResponse?>;

internal sealed record SaveLegalDocumentCommand(string Kind, string Locale, JsonElement Content) : ICommand<Result<LegalDocumentResponse>>;

internal sealed record ResetLegalDocumentCommand(string Kind, string Locale) : ICommand<Result>;

internal static class LegalDocuments
{
    public static readonly string[] Locales = ["ar", "en", "tr"];

    public static bool TryParse(string kind, string locale, out LegalDocumentKind parsed)
    {
        parsed = kind switch
        {
            "terms" => LegalDocumentKind.Terms,
            "privacy" => LegalDocumentKind.Privacy,
            _ => (LegalDocumentKind)(-1),
        };
        return Enum.IsDefined(parsed) && Locales.Contains(locale);
    }

    public static string Name(LegalDocumentKind kind) => kind == LegalDocumentKind.Terms ? "terms" : "privacy";

    public static LegalDocumentResponse ToResponse(LegalDocument document)
    {
        using var json = JsonDocument.Parse(document.Content);
        return new LegalDocumentResponse(Name(document.Kind), document.Locale, json.RootElement.Clone(), document.UpdatedAt);
    }

    public static Error NotFound() => Error.NotFound("legal.not_found", "This legal page has no saved text.");

    public static Error Invalid(string field, string code) =>
        Error.Validation("validation.failed", "The legal page is invalid.", new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [code] });
}

internal sealed class ListLegalDocumentsHandler(TrimmeDbContext db) : IQueryHandler<ListLegalDocumentsQuery, IReadOnlyList<LegalDocumentResponse>>
{
    public async Task<IReadOnlyList<LegalDocumentResponse>> Handle(ListLegalDocumentsQuery query, CancellationToken cancellationToken)
    {
        var documents = await db.Set<LegalDocument>().AsNoTracking().OrderBy(d => d.Kind).ThenBy(d => d.Locale).ToListAsync(cancellationToken);
        return [.. documents.Select(LegalDocuments.ToResponse)];
    }
}

internal sealed class GetPublicLegalDocumentHandler(TrimmeDbContext db) : IQueryHandler<GetPublicLegalDocumentQuery, LegalDocumentResponse?>
{
    public async Task<LegalDocumentResponse?> Handle(GetPublicLegalDocumentQuery query, CancellationToken cancellationToken)
    {
        if (!LegalDocuments.TryParse(query.Kind, query.Locale, out var kind))
        {
            return null;
        }

        var document = await db.Set<LegalDocument>().AsNoTracking()
            .SingleOrDefaultAsync(d => d.Kind == kind && d.Locale == query.Locale, cancellationToken);
        return document is null ? null : LegalDocuments.ToResponse(document);
    }
}

/// <summary>Saves one language of a legal page after the allowlist (D-144); audited with the page and language only.</summary>
internal sealed class SaveLegalDocumentHandler(TrimmeDbContext db, ICurrentUser user, IAuditLog audit, TimeProvider clock)
    : ICommandHandler<SaveLegalDocumentCommand, Result<LegalDocumentResponse>>
{
    public async Task<Result<LegalDocumentResponse>> Handle(SaveLegalDocumentCommand command, CancellationToken cancellationToken)
    {
        if (!LegalDocuments.TryParse(command.Kind, command.Locale, out var kind))
        {
            return LegalDocuments.NotFound();
        }

        if (LegalContent.Normalize(command.Content) is not { } content)
        {
            return LegalDocuments.Invalid("content", "legal.content_invalid");
        }

        var now = clock.GetUtcNow();
        var document = await db.Set<LegalDocument>().SingleOrDefaultAsync(d => d.Kind == kind && d.Locale == command.Locale, cancellationToken);
        if (document is null)
        {
            document = LegalDocument.Create(kind, command.Locale, content, now, user.UserId);
            db.Add(document);
        }
        else
        {
            document.Replace(content, now, user.UserId);
        }

        audit.Record(new AuditRecord("legal_document.saved", "LegalDocument", $"{command.Kind}:{command.Locale}", Summary: $"{command.Kind} ({command.Locale})"));
        await db.SaveChangesAsync(cancellationToken);
        return LegalDocuments.ToResponse(document);
    }
}

/// <summary>Goes back to the built-in text by deleting the saved one (D-144); audited.</summary>
internal sealed class ResetLegalDocumentHandler(TrimmeDbContext db, IAuditLog audit) : ICommandHandler<ResetLegalDocumentCommand, Result>
{
    public async Task<Result> Handle(ResetLegalDocumentCommand command, CancellationToken cancellationToken)
    {
        if (!LegalDocuments.TryParse(command.Kind, command.Locale, out var kind))
        {
            return LegalDocuments.NotFound();
        }

        var document = await db.Set<LegalDocument>().SingleOrDefaultAsync(d => d.Kind == kind && d.Locale == command.Locale, cancellationToken);
        if (document is not null)
        {
            db.Remove(document);
            audit.Record(new AuditRecord("legal_document.reset", "LegalDocument", $"{command.Kind}:{command.Locale}", Summary: $"{command.Kind} ({command.Locale})"));
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}
