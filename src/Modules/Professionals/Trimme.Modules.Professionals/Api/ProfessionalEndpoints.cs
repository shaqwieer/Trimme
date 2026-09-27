using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Media;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Professionals.Application;
using Trimme.Modules.Professionals.Application.Admin;
using Trimme.Modules.Professionals.Application.Public;
using Trimme.Modules.Professionals.Domain;

namespace Trimme.Modules.Professionals.Api;

/// <summary>The one shop is chosen here, at creation, and can never be changed afterwards (D-011).</summary>
public sealed record CreateProfessionalRequest(
    Guid ShopId,
    string NameAr,
    string NameEn,
    string? Slug,
    string? SpecialtyAr,
    string? SpecialtyEn,
    string? BioAr,
    string? BioEn,
    string? WhatsAppNumber,
    bool NotificationsEnabled);

/// <summary>Profile edit. There is deliberately no shop field: a professional cannot be moved to another shop.</summary>
public sealed record UpdateProfessionalRequest(
    string NameAr,
    string NameEn,
    string? Slug,
    string? SpecialtyAr,
    string? SpecialtyEn,
    string? BioAr,
    string? BioEn,
    uint Version);

public sealed record ChangeProfessionalStatusRequest(string? Reason);

/// <summary>
/// Sets, corrects or removes (null) the WhatsApp number (E.164 or a Saudi national format), and the notification
/// toggle. With <c>KeepCurrentNumber</c> only the toggle changes, so an admin never needs to see the number to do that.
/// </summary>
public sealed record SetProfessionalWhatsAppRequest(string? WhatsAppNumber, bool NotificationsEnabled, bool KeepCurrentNumber = false);

public sealed record RevealProfessionalWhatsAppRequest(string Reason);

internal static class ProfessionalEndpoints
{
    // Identity owns the permission catalogue; the endpoint matrix test fails if a code is not in it.
    private const string View = "Admin.Professionals.View";
    private const string Create = "Admin.Professionals.Create";
    private const string Edit = "Admin.Professionals.Edit";
    private const string Disable = "Admin.Professionals.Disable";
    private const string ManageWhatsApp = "Admin.Professionals.ManageWhatsApp";
    private const string RevealWhatsApp = "Admin.Professionals.RevealWhatsApp";

    public static void Map(IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/admin/professionals").WithTags("Admin: professionals");

        admin.MapPost("/", CreateProfessional).RequirePermission(Create)
            .WithName("AdminCreateProfessional").WithSummary("Creates a professional in exactly one shop (audited).")
            .Produces<AdminProfessionalResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);
        admin.MapGet("/", List).RequirePermission(View)
            .WithName("AdminListProfessionals").WithSummary("Professionals with shop, status and search filters (paged). Numbers are masked.");
        admin.MapGet("/{professionalId:guid}", Get).RequirePermission(View)
            .WithName("AdminGetProfessional").WithSummary("One professional. The WhatsApp number is masked.")
            .Produces<AdminProfessionalResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPut("/{professionalId:guid}", Update).RequirePermission(Edit)
            .WithName("AdminUpdateProfessional").WithSummary("Edits the profile (audited, optimistic concurrency). The shop cannot change.")
            .Produces<AdminProfessionalResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        admin.MapPost("/{professionalId:guid}/disable", (Guid professionalId, ChangeProfessionalStatusRequest? request, IDispatcher dispatcher, CancellationToken ct) =>
                Send(new ChangeProfessionalStatusCommand(professionalId, Enable: false, request?.Reason), dispatcher, ct))
            .RequirePermission(Disable)
            .WithName("AdminDisableProfessional").WithSummary("Hides the professional from booking (audited).")
            .Produces<AdminProfessionalResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        admin.MapPost("/{professionalId:guid}/enable", (Guid professionalId, ChangeProfessionalStatusRequest? request, IDispatcher dispatcher, CancellationToken ct) =>
                Send(new ChangeProfessionalStatusCommand(professionalId, Enable: true, request?.Reason), dispatcher, ct))
            .RequirePermission(Disable)
            .WithName("AdminEnableProfessional").WithSummary("Makes a disabled professional active again (audited).")
            .Produces<AdminProfessionalResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        admin.MapPut("/{professionalId:guid}/whatsapp", SetWhatsApp).RequirePermission(ManageWhatsApp)
            .WithName("AdminSetProfessionalWhatsApp").WithSummary("Sets or corrects the WhatsApp number and the notification toggle (audited, number never logged).")
            .Produces<AdminProfessionalResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPost("/{professionalId:guid}/whatsapp/reveal", Reveal).RequirePermission(RevealWhatsApp)
            .WithName("AdminRevealProfessionalWhatsApp").WithSummary("Returns the full number once, for a stated reason (audited; never cached).")
            .Produces<RevealedWhatsAppResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPut("/{professionalId:guid}/avatar", UploadAvatar).RequirePermission(Edit).AcceptsImageUpload()
            .WithName("AdminUploadProfessionalAvatar").WithSummary("Replaces the photo (JPEG, PNG or WebP, at most 5 MB; audited).")
            .Produces<AdminProfessionalResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapDelete("/{professionalId:guid}/avatar", (Guid professionalId, IDispatcher dispatcher, CancellationToken ct) =>
                Send(new ReplaceProfessionalAvatarCommand(professionalId, null), dispatcher, ct))
            .RequirePermission(Edit)
            .WithName("AdminRemoveProfessionalAvatar").WithSummary("Removes the photo (audited).")
            .Produces<AdminProfessionalResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/shop/professionals", ShopList).RequireUserType(UserTypes.ShopUser).WithTags("Shop")
            .WithName("ListShopProfessionals").WithSummary("The shop's own professionals, read-only and without contact data.")
            .Produces<IReadOnlyList<ShopProfessionalResponse>>();

        api.MapGet("/public/shops/{slug}/professionals", PublicList).AllowAnonymous().WithTags("Public")
            .WithName("ListPublicShopProfessionals").WithSummary("Active professionals of an active shop. No contact data.")
            .Produces<IReadOnlyList<PublicProfessionalResponse>>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> CreateProfessional(CreateProfessionalRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new CreateProfessionalCommand(
                request.ShopId, request.NameAr ?? string.Empty, request.NameEn ?? string.Empty, request.Slug,
                request.SpecialtyAr, request.SpecialtyEn, request.BioAr, request.BioEn, request.WhatsAppNumber, request.NotificationsEnabled),
            cancellationToken);
        return result.ToHttpResult(professional => TypedResults.Created($"/api/v1/admin/professionals/{professional.Id}", professional));
    }

    private static async Task<Ok<PagedResponse<AdminProfessionalListItem>>> List(
        int? page, int? pageSize, Guid? shopId, ProfessionalStatus? status, string? search, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dispatcher.Send(new ListProfessionalsQuery(new PageRequest(page, pageSize), shopId, status, search), cancellationToken));

    private static async Task<IResult> Get(Guid professionalId, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        await dispatcher.Send(new GetProfessionalQuery(professionalId), cancellationToken) is { } professional
            ? TypedResults.Ok(professional)
            : ProfessionalErrors.NotFound().ToProblem();

    private static Task<IResult> Update(Guid professionalId, UpdateProfessionalRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        Send(
            new UpdateProfessionalCommand(
                professionalId, request.NameAr ?? string.Empty, request.NameEn ?? string.Empty, request.Slug,
                request.SpecialtyAr, request.SpecialtyEn, request.BioAr, request.BioEn, request.Version),
            dispatcher,
            cancellationToken);

    private static Task<IResult> SetWhatsApp(Guid professionalId, SetProfessionalWhatsAppRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        Send(new SetProfessionalWhatsAppCommand(professionalId, request.WhatsAppNumber, request.NotificationsEnabled, request.KeepCurrentNumber), dispatcher, cancellationToken);

    private static async Task<IResult> Reveal(Guid professionalId, RevealProfessionalWhatsAppRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.Send(new RevealProfessionalWhatsAppCommand(professionalId, request.Reason ?? string.Empty), cancellationToken)).ToHttpResult();

    private static async Task<IResult> UploadAvatar(Guid professionalId, IFormFile? file, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var content = await ImageUpload.ReadAsync(file, cancellationToken);
        return content.IsFailure
            ? content.Error.ToProblem()
            : await Send(new ReplaceProfessionalAvatarCommand(professionalId, content.Value), dispatcher, cancellationToken);
    }

    private static async Task<IResult> Send(ICommand<Result<AdminProfessionalResponse>> command, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.Send(command, cancellationToken)).ToHttpResult();

    private static async Task<Ok<IReadOnlyList<ShopProfessionalResponse>>> ShopList(IDispatcher dispatcher, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dispatcher.Send(new ListShopProfessionalsQuery(), cancellationToken));

    private static async Task<IResult> PublicList(string slug, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        await dispatcher.Send(new ListPublicProfessionalsQuery(slug), cancellationToken) is { } professionals
            ? TypedResults.Ok(professionals)
            : Error.NotFound("shop.not_found", "The shop was not found.").ToProblem();
}
