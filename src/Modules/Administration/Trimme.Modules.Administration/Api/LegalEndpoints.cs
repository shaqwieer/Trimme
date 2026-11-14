using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Web.Caching;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Administration.Application.Admin;

namespace Trimme.Modules.Administration.Api;

/// <summary>The rich-text document from the admin's editor (ProseMirror JSON); the API keeps only what its allowlist permits.</summary>
public sealed record SaveLegalDocumentRequest(JsonElement Content);

/// <summary>The terms of use and privacy policy the admin writes (D-144), and the public read the legal pages use.</summary>
internal static class LegalEndpoints
{
    // The same permissions as the platform settings: no new permission to grant to the existing admin roles.
    private const string SettingsView = "Admin.Settings.View";
    private const string SettingsEdit = "Admin.Settings.Edit";

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/public/legal/{kind}", GetPublic).AllowAnonymous().CachePublicly().WithTags("Public")
            .WithName("GetPublicLegalDocument")
            .WithSummary("The admin's text of the terms (kind=terms) or privacy policy (kind=privacy) in one language; 404 means the built-in text applies.")
            .Produces<LegalDocumentResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        var admin = api.MapGroup("/admin/legal").WithTags("Admin: settings");
        admin.MapGet("/", async (IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.Send(new ListLegalDocumentsQuery(), ct)))
            .RequirePermission(SettingsView)
            .WithName("ListLegalDocuments").WithSummary("Every saved language of the terms and the privacy policy.")
            .Produces<IReadOnlyList<LegalDocumentResponse>>();
        admin.MapPut("/{kind}/{locale}", async (string kind, string locale, SaveLegalDocumentRequest request, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SaveLegalDocumentCommand(kind, locale, request.Content), ct)).ToHttpResult())
            .RequirePermission(SettingsEdit)
            .WithName("SaveLegalDocument").WithSummary("Saves one language of the terms or the privacy policy (rich text through an allowlist; audited).")
            .Produces<LegalDocumentResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapDelete("/{kind}/{locale}", async (string kind, string locale, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new ResetLegalDocumentCommand(kind, locale), ct)).ToHttpResult())
            .RequirePermission(SettingsEdit)
            .WithName("ResetLegalDocument").WithSummary("Deletes the saved text of one language, so the page shows its built-in text again (audited).")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetPublic(string kind, string? locale, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        await dispatcher.Send(new GetPublicLegalDocumentQuery(kind, locale ?? "ar"), cancellationToken) is { } document
            ? TypedResults.Ok(document)
            : LegalDocuments.NotFound().ToProblem();
}
