using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Media;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Shops.Application;
using Trimme.Modules.Shops.Application.Admin;
using Trimme.Modules.Shops.Application.Public;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Api;

public sealed record CreateShopRequest(string Slug, string NameAr, string NameEn, string? TimeZone);

public sealed record ChangeShopStatusRequest(string? Reason);

/// <summary>
/// The admin's profile edit. <c>Version</c> is the value the client read; a stale value answers 409
/// <c>resource.concurrency_conflict</c>.
/// </summary>
public sealed record UpdateShopProfileRequest(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    ShopCategory Category,
    string? PublicPhone,
    IReadOnlyList<ShopAmenity>? Amenities,
    bool IsVerified,
    uint Version);

/// <summary>The shop's own edit. Send the whole form: fields locked by the admin policy must keep their current value.</summary>
public sealed record UpdateOwnShopProfileRequest(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    ShopCategory Category,
    string? PublicPhone,
    IReadOnlyList<ShopAmenity>? Amenities,
    uint Version);

public sealed record ShopLocationRequest(
    double Latitude,
    double Longitude,
    string? AddressLine,
    string? District,
    string? City,
    string? FormattedAddress,
    LocationSource Source);

public sealed record EditablePolicyRequest(IReadOnlyList<ShopProfileField> EditableFields);

internal static class ShopEndpoints
{
    // Identity owns the permission catalogue; the codes are duplicated as literals here to keep modules decoupled.
    // The endpoint matrix test fails if a code is not in the catalogue.
    private const string ShopsView = "Admin.Shops.View";
    private const string ShopsCreate = "Admin.Shops.Create";
    private const string ShopsEdit = "Admin.Shops.Edit";
    private const string ShopsSuspend = "Admin.Shops.Suspend";
    private const string ShopProfileEdit = "Shop.Profile.Edit";
    private const string ShopLocationEdit = "Shop.Location.Edit";

    public static void Map(IEndpointRouteBuilder api)
    {
        MapAdmin(api);
        MapShop(api);
        MapGeocoding(api.MapGroup("/admin/geo").WithTags("Admin: shops"), ShopsEdit, "Admin");
        MapGeocoding(api.MapGroup("/shop/geo").WithTags("Shop"), ShopLocationEdit, "Shop");

        api.MapGet("/public/shops/{slug}", GetPublic).AllowAnonymous().WithTags("Public")
            .WithName("GetPublicShop").WithSummary("An active shop's public profile. No professional or customer contact data.")
            .Produces<PublicShopResponse>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapAdmin(IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/admin/shops").WithTags("Admin: shops");

        admin.MapPost("/", Create).RequirePermission(ShopsCreate)
            .WithName("AdminCreateShop").WithSummary("Creates a shop in Draft status.")
            .Produces<AdminShopResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);
        admin.MapGet("/", List).RequirePermission(ShopsView)
            .WithName("AdminListShops").WithSummary("Shops, newest first, with search (name, slug, district) and status filter (paged).");
        admin.MapGet("/{shopId:guid}", Get).RequirePermission(ShopsView)
            .WithName("AdminGetShop").WithSummary("One shop with its profile, location and shop-edit policy.")
            .Produces<AdminShopDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPost("/{shopId:guid}/activate", Activate).RequirePermission(ShopsSuspend)
            .WithName("AdminActivateShop").WithSummary("Activates a draft or suspended shop (audited).")
            .Produces<AdminShopResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        admin.MapPost("/{shopId:guid}/suspend", Suspend).RequirePermission(ShopsSuspend)
            .WithName("AdminSuspendShop").WithSummary("Suspends a shop; its users lose access to shop data immediately (audited).")
            .Produces<AdminShopResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        admin.MapPut("/{shopId:guid}", UpdateProfile).RequirePermission(ShopsEdit)
            .WithName("AdminUpdateShopProfile").WithSummary("Edits the public profile and verification (audited, optimistic concurrency).")
            .Produces<AdminShopDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        admin.MapPut("/{shopId:guid}/location", SetLocation).RequirePermission(ShopsEdit)
            .WithName("AdminSetShopLocation").WithSummary("Sets the confirmed map point and address (audited).")
            .Produces<AdminShopDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPut("/{shopId:guid}/editable-policy", SetPolicy).RequirePermission(ShopsEdit)
            .WithName("AdminSetShopEditablePolicy").WithSummary("Sets which profile fields the shop may edit itself (audited).")
            .Produces<AdminShopDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPut("/{shopId:guid}/logo", (Guid shopId, IFormFile? file, IDispatcher dispatcher, CancellationToken ct) =>
                UploadAdmin(shopId, ShopImageSlot.Logo, file, dispatcher, ct))
            .RequirePermission(ShopsEdit).AcceptsImageUpload()
            .WithName("AdminUploadShopLogo").WithSummary("Replaces the logo (JPEG, PNG or WebP, at most 5 MB; audited).")
            .Produces<AdminShopDetailResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapDelete("/{shopId:guid}/logo", (Guid shopId, IDispatcher dispatcher, CancellationToken ct) =>
                SendAdmin(new ReplaceShopImageCommand(shopId, ShopImageSlot.Logo, null), dispatcher, ct))
            .RequirePermission(ShopsEdit)
            .WithName("AdminRemoveShopLogo").WithSummary("Removes the logo (audited).")
            .Produces<AdminShopDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPut("/{shopId:guid}/cover", (Guid shopId, IFormFile? file, IDispatcher dispatcher, CancellationToken ct) =>
                UploadAdmin(shopId, ShopImageSlot.Cover, file, dispatcher, ct))
            .RequirePermission(ShopsEdit).AcceptsImageUpload()
            .WithName("AdminUploadShopCover").WithSummary("Replaces the cover image (1600×900 recommended; audited).")
            .Produces<AdminShopDetailResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapDelete("/{shopId:guid}/cover", (Guid shopId, IDispatcher dispatcher, CancellationToken ct) =>
                SendAdmin(new ReplaceShopImageCommand(shopId, ShopImageSlot.Cover, null), dispatcher, ct))
            .RequirePermission(ShopsEdit)
            .WithName("AdminRemoveShopCover").WithSummary("Removes the cover image (audited).")
            .Produces<AdminShopDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPost("/{shopId:guid}/gallery", AddGalleryImage)
            .RequirePermission(ShopsEdit).AcceptsImageUpload()
            .WithName("AdminAddShopGalleryImage").WithSummary("Adds a gallery image (up to 12; audited).")
            .Produces<AdminShopDetailResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapDelete("/{shopId:guid}/gallery/{mediaId:guid}", (Guid shopId, Guid mediaId, IDispatcher dispatcher, CancellationToken ct) =>
                SendAdmin(new RemoveShopGalleryImageCommand(shopId, mediaId), dispatcher, ct))
            .RequirePermission(ShopsEdit)
            .WithName("AdminRemoveShopGalleryImage").WithSummary("Removes a gallery image (audited).")
            .Produces<AdminShopDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapShop(IEndpointRouteBuilder api)
    {
        api.MapGet("/shop/me", MyShop).RequireUserType(UserTypes.ShopUser).WithTags("Shop")
            .WithName("GetMyShop").WithSummary("The signed-in shop user's shop, from the session (never from the request).")
            .Produces<ShopProfileResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        var shop = api.MapGroup("/shop").WithTags("Shop");
        shop.MapGet("/profile", OwnProfile).RequireUserType(UserTypes.ShopUser)
            .WithName("GetOwnShopProfile").WithSummary("The shop's own profile and the admin's edit policy. Not available while suspended.")
            .Produces<ShopOwnProfileResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        shop.MapPut("/profile", UpdateOwnProfile).RequirePermission(ShopProfileEdit)
            .WithName("UpdateOwnShopProfile").WithSummary("Edits the fields the admin policy opens to the shop; a locked field that changes answers 403.")
            .Produces<ShopOwnProfileResponse>().ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict);
        shop.MapPut("/profile/logo", (IFormFile? file, IDispatcher dispatcher, CancellationToken ct) => UploadOwn(ShopImageSlot.Logo, file, dispatcher, ct))
            .RequirePermission(ShopProfileEdit).AcceptsImageUpload()
            .WithName("UploadOwnShopLogo").WithSummary("Replaces the logo, if the policy allows.")
            .Produces<ShopOwnProfileResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden);
        shop.MapDelete("/profile/logo", (IDispatcher dispatcher, CancellationToken ct) => SendOwn(new ReplaceOwnShopImageCommand(ShopImageSlot.Logo, null), dispatcher, ct))
            .RequirePermission(ShopProfileEdit)
            .WithName("RemoveOwnShopLogo").WithSummary("Removes the logo, if the policy allows.")
            .Produces<ShopOwnProfileResponse>().ProducesProblem(StatusCodes.Status403Forbidden);
        shop.MapPut("/profile/cover", (IFormFile? file, IDispatcher dispatcher, CancellationToken ct) => UploadOwn(ShopImageSlot.Cover, file, dispatcher, ct))
            .RequirePermission(ShopProfileEdit).AcceptsImageUpload()
            .WithName("UploadOwnShopCover").WithSummary("Replaces the cover image, if the policy allows.")
            .Produces<ShopOwnProfileResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden);
        shop.MapDelete("/profile/cover", (IDispatcher dispatcher, CancellationToken ct) => SendOwn(new ReplaceOwnShopImageCommand(ShopImageSlot.Cover, null), dispatcher, ct))
            .RequirePermission(ShopProfileEdit)
            .WithName("RemoveOwnShopCover").WithSummary("Removes the cover image, if the policy allows.")
            .Produces<ShopOwnProfileResponse>().ProducesProblem(StatusCodes.Status403Forbidden);
        shop.MapPost("/profile/gallery", AddOwnGalleryImage)
            .RequirePermission(ShopProfileEdit).AcceptsImageUpload()
            .WithName("AddOwnShopGalleryImage").WithSummary("Adds a gallery image, if the policy allows.")
            .Produces<ShopOwnProfileResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden);
        shop.MapDelete("/profile/gallery/{mediaId:guid}", (Guid mediaId, IDispatcher dispatcher, CancellationToken ct) =>
                SendOwn(new RemoveOwnShopGalleryImageCommand(mediaId), dispatcher, ct))
            .RequirePermission(ShopProfileEdit)
            .WithName("RemoveOwnShopGalleryImage").WithSummary("Removes one of the shop's gallery images, if the policy allows.")
            .Produces<ShopOwnProfileResponse>().ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        shop.MapPut("/location", SetOwnLocation).RequirePermission(ShopLocationEdit)
            .WithName("SetOwnShopLocation").WithSummary("Sets the shop's map point, if the admin policy opens the location to the shop.")
            .Produces<ShopOwnProfileResponse>().ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static void MapGeocoding(RouteGroupBuilder group, string permission, string namePrefix)
    {
        group.MapGet("/search", Search).RequirePermission(permission).RequireRateLimiting(RateLimitPolicies.Geocode)
            .WithName($"{namePrefix}GeocodeSearch").WithSummary("Address and district search for the location picker (server-side provider, cached).")
            .Produces<IReadOnlyList<GeocodedPlace>>().ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapGet("/reverse", Reverse).RequirePermission(permission).RequireRateLimiting(RateLimitPolicies.Geocode)
            .WithName($"{namePrefix}GeocodeReverse").WithSummary("The address at a map point, for the picker's confirmation card.")
            .Produces<GeocodedPlace>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Create(CreateShopRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new CreateShopCommand(request.Slug ?? string.Empty, request.NameAr ?? string.Empty, request.NameEn ?? string.Empty, request.TimeZone),
            cancellationToken);
        return result.ToHttpResult(shop => TypedResults.Created($"/api/v1/admin/shops/{shop.Id}", shop));
    }

    private static async Task<Ok<PagedResponse<AdminShopResponse>>> List(
        int? page, int? pageSize, string? search, ShopStatus? status, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dispatcher.Send(new ListShopsQuery(new PageRequest(page, pageSize), search, status), cancellationToken));

    private static async Task<IResult> Get(Guid shopId, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        await dispatcher.Send(new GetShopQuery(shopId), cancellationToken) is { } shop
            ? TypedResults.Ok(shop)
            : ShopErrors.NotFound().ToProblem();

    private static async Task<IResult> Activate(Guid shopId, ChangeShopStatusRequest? request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.Send(new ChangeShopStatusCommand(shopId, ShopStatusChange.Activate, request?.Reason), cancellationToken)).ToHttpResult();

    private static async Task<IResult> Suspend(Guid shopId, ChangeShopStatusRequest? request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.Send(new ChangeShopStatusCommand(shopId, ShopStatusChange.Suspend, request?.Reason), cancellationToken)).ToHttpResult();

    private static Task<IResult> UpdateProfile(Guid shopId, UpdateShopProfileRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        SendAdmin(
            new UpdateShopProfileCommand(
                shopId, request.NameAr ?? string.Empty, request.NameEn ?? string.Empty, request.DescriptionAr, request.DescriptionEn,
                request.Category, request.PublicPhone, request.Amenities, request.IsVerified, request.Version),
            dispatcher,
            cancellationToken);

    private static Task<IResult> SetLocation(Guid shopId, ShopLocationRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        SendAdmin(
            new SetShopLocationCommand(
                shopId, request.Latitude, request.Longitude, request.AddressLine, request.District, request.City, request.FormattedAddress, request.Source),
            dispatcher,
            cancellationToken);

    private static Task<IResult> SetPolicy(Guid shopId, EditablePolicyRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        SendAdmin(new SetEditablePolicyCommand(shopId, request.EditableFields ?? []), dispatcher, cancellationToken);

    private static async Task<IResult> AddGalleryImage(Guid shopId, IFormFile? file, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var content = await ImageUpload.ReadAsync(file, cancellationToken);
        return content.IsFailure
            ? content.Error.ToProblem()
            : await SendAdmin(new AddShopGalleryImageCommand(shopId, content.Value), dispatcher, cancellationToken);
    }

    private static async Task<IResult> UploadAdmin(Guid shopId, ShopImageSlot slot, IFormFile? file, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var content = await ImageUpload.ReadAsync(file, cancellationToken);
        return content.IsFailure
            ? content.Error.ToProblem()
            : await SendAdmin(new ReplaceShopImageCommand(shopId, slot, content.Value), dispatcher, cancellationToken);
    }

    private static async Task<IResult> SendAdmin(ICommand<Result<AdminShopDetailResponse>> command, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.Send(command, cancellationToken)).ToHttpResult();

    private static async Task<IResult> MyShop(ICurrentUser user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        user.ShopId is { } shopId && await dispatcher.Send(new GetMyShopQuery(new ShopId(shopId)), cancellationToken) is { } shop
            ? TypedResults.Ok(shop)
            : ShopErrors.NotFound().ToProblem();

    private static async Task<IResult> OwnProfile(IDispatcher dispatcher, CancellationToken cancellationToken) =>
        await dispatcher.Send(new GetOwnShopProfileQuery(), cancellationToken) is { } profile
            ? TypedResults.Ok(profile)
            : ShopErrors.NotFound().ToProblem();

    private static Task<IResult> UpdateOwnProfile(UpdateOwnShopProfileRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        SendOwn(
            new UpdateOwnShopProfileCommand(
                request.NameAr ?? string.Empty, request.NameEn ?? string.Empty, request.DescriptionAr, request.DescriptionEn,
                request.Category, request.PublicPhone, request.Amenities, request.Version),
            dispatcher,
            cancellationToken);

    private static Task<IResult> SetOwnLocation(ShopLocationRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        SendOwn(
            new SetOwnShopLocationCommand(
                request.Latitude, request.Longitude, request.AddressLine, request.District, request.City, request.FormattedAddress, request.Source),
            dispatcher,
            cancellationToken);

    private static async Task<IResult> AddOwnGalleryImage(IFormFile? file, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var content = await ImageUpload.ReadAsync(file, cancellationToken);
        return content.IsFailure
            ? content.Error.ToProblem()
            : await SendOwn(new AddOwnShopGalleryImageCommand(content.Value), dispatcher, cancellationToken);
    }

    private static async Task<IResult> UploadOwn(ShopImageSlot slot, IFormFile? file, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var content = await ImageUpload.ReadAsync(file, cancellationToken);
        return content.IsFailure
            ? content.Error.ToProblem()
            : await SendOwn(new ReplaceOwnShopImageCommand(slot, content.Value), dispatcher, cancellationToken);
    }

    private static async Task<IResult> SendOwn(ICommand<Result<ShopOwnProfileResponse>> command, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.Send(command, cancellationToken)).ToHttpResult();

    private static async Task<Ok<IReadOnlyList<GeocodedPlace>>> Search(string? q, string? lang, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dispatcher.Send(new SearchPlacesQuery(q ?? string.Empty, lang ?? "ar"), cancellationToken));

    private static async Task<IResult> Reverse(double lat, double lng, string? lang, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        await dispatcher.Send(new ReversePlaceQuery(lat, lng, lang ?? "ar"), cancellationToken) is { } place
            ? TypedResults.Ok(place)
            : Error.NotFound("geo.not_found", "No address was found at this point.").ToProblem();

    private static async Task<IResult> GetPublic(string slug, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        await dispatcher.Send(new GetPublicShopQuery(slug), cancellationToken) is { } shop
            ? TypedResults.Ok(shop)
            : ShopErrors.NotFound().ToProblem();
}
