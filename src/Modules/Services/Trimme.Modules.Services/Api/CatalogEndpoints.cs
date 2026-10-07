using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Web.Caching;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Services.Application;
using Trimme.Modules.Services.Application.Admin;
using Trimme.Modules.Services.Application.Public;
using Trimme.Modules.Services.Domain;

namespace Trimme.Modules.Services.Api;

/// <summary>A new service of the signed-in shop. Price in SAR (≤ 2 decimals); duration a multiple of 5 minutes.</summary>
public sealed record CreateShopServiceRequest(
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    Guid? CategoryId,
    decimal Price,
    int DurationMinutes,
    bool OnlineBookable);

/// <summary>Edit of the shop's own service; a stale <c>Version</c> answers 409.</summary>
public sealed record UpdateShopServiceRequest(
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    Guid? CategoryId,
    decimal Price,
    int DurationMinutes,
    bool OnlineBookable,
    uint Version);

/// <summary>A package of 2–10 of the shop's own services, with its own price and total duration.</summary>
public sealed record CreateShopPackageRequest(
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    decimal Price,
    int DurationMinutes,
    IReadOnlyList<Guid> ServiceIds);

public sealed record UpdateShopPackageRequest(
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    decimal Price,
    int DurationMinutes,
    IReadOnlyList<Guid> ServiceIds,
    uint Version);

/// <summary>Every non-archived item of the shop, in the new order.</summary>
public sealed record ReorderRequest(IReadOnlyList<Guid> OrderedIds);

public sealed record CategoryRequest(string NameAr, string NameEn, string Icon, int DisplayOrder);

/// <summary>Hide needs a reason (audited); unhide does not.</summary>
public sealed record ModerationRequest(ModerationAction Action, string? Reason);

/// <summary>Support correction of one shop service. A reason is required and the change is audited.</summary>
public sealed record OverrideServiceRequest(
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    Guid? CategoryId,
    decimal Price,
    int DurationMinutes,
    bool OnlineBookable,
    string Reason,
    uint Version);

public sealed record ProfessionalServicesRequest(IReadOnlyList<Guid> ServiceIds);

/// <summary>An admin's package for a shop (D-130): the shop's own price and duration, of that shop's services.</summary>
public sealed record AdminPackageRequest(
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    decimal Price,
    int DurationMinutes,
    IReadOnlyList<Guid> ServiceIds,
    uint Version = 0);

/// <summary>
/// An admin's service for a shop (D-127): the shop's own name, price and duration, and the shop's barbers who do it.
/// On an edit, <c>ProfessionalIds</c> null keeps them and <c>Version</c> is the one read.
/// </summary>
public sealed record AdminServiceRequest(
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    Guid? CategoryId,
    decimal Price,
    int DurationMinutes,
    bool OnlineBookable,
    IReadOnlyList<Guid>? ProfessionalIds,
    uint Version = 0);

internal static class CatalogEndpoints
{
    // Identity owns the permission catalogue; the endpoint matrix test fails if a code is not in it.
    private const string ShopManage = "Shop.Services.Manage";
    private const string CategoriesManage = "Admin.ServiceCategories.Manage";
    private const string ServicesView = "Admin.ShopServices.View";
    private const string ServicesModerate = "Admin.ShopServices.Moderate";
    private const string ServicesOverride = "Admin.ShopServices.SupportOverride";
    private const string ServicesManage = "Admin.ShopServices.Manage";
    private const string ProfessionalsView = "Admin.Professionals.View";
    private const string AssignServices = "Admin.Professionals.AssignServices";

    public static void Map(IEndpointRouteBuilder api)
    {
        MapShopServices(api.MapGroup("/shop/services").WithTags("Shop: services"));
        MapShopPackages(api.MapGroup("/shop/packages").WithTags("Shop: services"));
        MapAdmin(api);

        api.MapGet("/public/service-categories", PublicCategories).AllowAnonymous().CachePublicly().WithTags("Public")
            .WithName("ListPublicServiceCategories").WithSummary("Active service categories, in display order.")
            .Produces<IReadOnlyList<ServiceCategoryResponse>>();
        api.MapGet("/public/shops/{slug}/services", PublicServices).AllowAnonymous().CachePublicly().WithTags("Public")
            .WithName("ListPublicShopServices").WithSummary("An active shop's published services (active, not archived, not hidden).")
            .Produces<IReadOnlyList<PublicServiceResponse>>().ProducesProblem(StatusCodes.Status404NotFound);
        api.MapGet("/public/shops/{slug}/packages", PublicPackages).AllowAnonymous().CachePublicly().WithTags("Public")
            .WithName("ListPublicShopPackages").WithSummary("An active shop's published packages whose every service is available.")
            .Produces<IReadOnlyList<PublicPackageResponse>>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapShopServices(RouteGroupBuilder group)
    {
        group.MapGet("/", async (bool? includeArchived, IDispatcher d, CancellationToken ct) =>
                await d.Send(new ListShopServicesQuery(includeArchived ?? false), ct) is { } list ? TypedResults.Ok(list) : CatalogErrors.ServiceNotFound().ToProblem())
            .RequireUserType(UserTypes.ShopUser)
            .WithName("ListShopServices").WithSummary("The shop's own services in display order (archived ones on request).")
            .Produces<IReadOnlyList<ShopServiceResponse>>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{serviceId:guid}", async (Guid serviceId, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetShopServiceQuery(serviceId), ct) is { } service ? TypedResults.Ok(service) : CatalogErrors.ServiceNotFound().ToProblem())
            .RequireUserType(UserTypes.ShopUser)
            .WithName("GetShopService").WithSummary("One of the shop's own services; another shop's id is 404.")
            .Produces<ShopServiceResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/", async (CreateShopServiceRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new CreateShopServiceCommand(r.NameAr ?? string.Empty, r.NameEn, r.DescriptionAr, r.DescriptionEn, r.CategoryId, r.Price, r.DurationMinutes, r.OnlineBookable), ct))
                    .ToHttpResult(s => TypedResults.Created($"/api/v1/shop/services/{s.Id}", s)))
            .RequirePermission(ShopManage)
            .WithName("CreateShopService").WithSummary("Creates a service with the shop's own name, price and duration.")
            .Produces<ShopServiceResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapPut("/{serviceId:guid}", async (Guid serviceId, UpdateShopServiceRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new UpdateShopServiceCommand(serviceId, r.NameAr ?? string.Empty, r.NameEn, r.DescriptionAr, r.DescriptionEn, r.CategoryId, r.Price, r.DurationMinutes, r.OnlineBookable, r.Version), ct)).ToHttpResult())
            .RequirePermission(ShopManage)
            .WithName("UpdateShopService").WithSummary("Edits the shop's own service (optimistic concurrency).")
            .Produces<ShopServiceResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        foreach (var (action, change) in new[] { ("activate", CatalogStateChange.Activate), ("deactivate", CatalogStateChange.Deactivate), ("archive", CatalogStateChange.Archive) })
        {
            group.MapPost($"/{{serviceId:guid}}/{action}", async (Guid serviceId, IDispatcher d, CancellationToken ct) =>
                    (await d.Send(new ChangeShopServiceStateCommand(serviceId, change), ct)).ToHttpResult())
                .RequirePermission(ShopManage)
                .WithName($"{char.ToUpperInvariant(action[0])}{action[1..]}ShopService")
                .WithSummary(change == CatalogStateChange.Archive ? "Retires the service for good; history keeps it." : $"Turns the service {(change == CatalogStateChange.Activate ? "on" : "off")}.")
                .Produces<ShopServiceResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        }

        group.MapDelete("/{serviceId:guid}", async (Guid serviceId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new DeleteShopServiceCommand(serviceId), ct)).ToHttpResult())
            .RequirePermission(ShopManage)
            .WithName("DeleteShopService").WithSummary("Deletes a service nothing uses yet; otherwise 409 service.in_use (archive instead).")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPut("/order", async (ReorderRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new ReorderShopServicesCommand(r.OrderedIds ?? []), ct)).ToHttpResult())
            .RequirePermission(ShopManage)
            .WithName("ReorderShopServices").WithSummary("Sets the display order from the full list of non-archived services.")
            .Produces<IReadOnlyList<ShopServiceResponse>>().ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapShopPackages(RouteGroupBuilder group)
    {
        group.MapGet("/", async (bool? includeArchived, IDispatcher d, CancellationToken ct) =>
                await d.Send(new ListShopPackagesQuery(includeArchived ?? false), ct) is { } list ? TypedResults.Ok(list) : CatalogErrors.PackageNotFound().ToProblem())
            .RequireUserType(UserTypes.ShopUser)
            .WithName("ListShopPackages").WithSummary("The shop's own packages, with whether each is currently bookable.")
            .Produces<IReadOnlyList<ShopPackageResponse>>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{packageId:guid}", async (Guid packageId, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetShopPackageQuery(packageId), ct) is { } package ? TypedResults.Ok(package) : CatalogErrors.PackageNotFound().ToProblem())
            .RequireUserType(UserTypes.ShopUser)
            .WithName("GetShopPackage").WithSummary("One of the shop's own packages.")
            .Produces<ShopPackageResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/", async (CreateShopPackageRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new CreateShopPackageCommand(r.NameAr ?? string.Empty, r.NameEn, r.DescriptionAr, r.DescriptionEn, r.Price, r.DurationMinutes, r.ServiceIds ?? []), ct))
                    .ToHttpResult(p => TypedResults.Created($"/api/v1/shop/packages/{p.Id}", p)))
            .RequirePermission(ShopManage)
            .WithName("CreateShopPackage").WithSummary("Creates a package of the shop's own services.")
            .Produces<ShopPackageResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapPut("/{packageId:guid}", async (Guid packageId, UpdateShopPackageRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new UpdateShopPackageCommand(packageId, r.NameAr ?? string.Empty, r.NameEn, r.DescriptionAr, r.DescriptionEn, r.Price, r.DurationMinutes, r.ServiceIds ?? [], r.Version), ct)).ToHttpResult())
            .RequirePermission(ShopManage)
            .WithName("UpdateShopPackage").WithSummary("Edits the package and its items (optimistic concurrency).")
            .Produces<ShopPackageResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        foreach (var (action, change) in new[] { ("activate", CatalogStateChange.Activate), ("deactivate", CatalogStateChange.Deactivate), ("archive", CatalogStateChange.Archive) })
        {
            group.MapPost($"/{{packageId:guid}}/{action}", async (Guid packageId, IDispatcher d, CancellationToken ct) =>
                    (await d.Send(new ChangeShopPackageStateCommand(packageId, change), ct)).ToHttpResult())
                .RequirePermission(ShopManage)
                .WithName($"{char.ToUpperInvariant(action[0])}{action[1..]}ShopPackage")
                .WithSummary(change == CatalogStateChange.Archive ? "Retires the package for good." : $"Turns the package {(change == CatalogStateChange.Activate ? "on" : "off")}.")
                .Produces<ShopPackageResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        }

        group.MapPut("/order", async (ReorderRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new ReorderShopPackagesCommand(r.OrderedIds ?? []), ct)).ToHttpResult())
            .RequirePermission(ShopManage)
            .WithName("ReorderShopPackages").WithSummary("Sets the display order from the full list of non-archived packages.")
            .Produces<IReadOnlyList<ShopPackageResponse>>().ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapAdmin(IEndpointRouteBuilder api)
    {
        var categories = api.MapGroup("/admin/service-categories").WithTags("Admin: services");
        categories.MapGet("/", async (IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.Send(new ListCategoriesQuery(), ct)))
            .RequirePermission(ServicesView)
            .WithName("AdminListServiceCategories").WithSummary("All categories with how many active shop services use each.");
        categories.MapPost("/", async (CategoryRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new CreateCategoryCommand(r.NameAr ?? string.Empty, r.NameEn ?? string.Empty, r.Icon ?? string.Empty, r.DisplayOrder), ct))
                    .ToHttpResult(c => TypedResults.Created($"/api/v1/admin/service-categories/{c.Id}", c)))
            .RequirePermission(CategoriesManage)
            .WithName("AdminCreateServiceCategory").WithSummary("Creates a category (no price or duration: those are the shops').")
            .Produces<AdminCategoryResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);
        categories.MapPut("/{categoryId:guid}", async (Guid categoryId, CategoryRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new UpdateCategoryCommand(categoryId, r.NameAr ?? string.Empty, r.NameEn ?? string.Empty, r.Icon ?? string.Empty, r.DisplayOrder), ct)).ToHttpResult())
            .RequirePermission(CategoriesManage)
            .WithName("AdminUpdateServiceCategory").WithSummary("Edits a category (audited).")
            .Produces<AdminCategoryResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        foreach (var (action, active) in new[] { ("activate", true), ("deactivate", false) })
        {
            categories.MapPost($"/{{categoryId:guid}}/{action}", async (Guid categoryId, IDispatcher d, CancellationToken ct) =>
                    (await d.Send(new SetCategoryActiveCommand(categoryId, active), ct)).ToHttpResult())
                .RequirePermission(CategoriesManage)
                .WithName(active ? "AdminActivateServiceCategory" : "AdminDeactivateServiceCategory")
                .WithSummary(active ? "Offers the category again." : "Stops offering the category for new services; existing ones keep it.")
                .Produces<AdminCategoryResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        }

        var services = api.MapGroup("/admin/services").WithTags("Admin: services");
        services.MapGet("/", async (int? page, int? pageSize, Guid? shopId, Guid? categoryId, CatalogStateFilter? state, string? search, IDispatcher d, CancellationToken ct) =>
                TypedResults.Ok(await d.Send(new ListAdminServicesQuery(new PageRequest(page, pageSize), shopId, categoryId, state, search), ct)))
            .RequirePermission(ServicesView)
            .WithName("AdminListServices").WithSummary("Shop services across the platform, each with its shop's own price and duration (paged).");
        services.MapGet("/{serviceId:guid}", async (Guid serviceId, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetAdminServiceQuery(serviceId), ct) is { } service ? TypedResults.Ok(service) : CatalogErrors.ServiceNotFound().ToProblem())
            .RequirePermission(ServicesView)
            .WithName("AdminGetService").WithSummary("One shop service.")
            .Produces<AdminServiceResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        services.MapPost("/{serviceId:guid}/moderation", async (Guid serviceId, ModerationRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new ModerateServiceCommand(serviceId, r.Action, r.Reason), ct)).ToHttpResult())
            .RequirePermission(ServicesModerate)
            .WithName("AdminModerateService").WithSummary("Hides a service from customers (reason required) or shows it again (audited).")
            .Produces<AdminServiceResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        services.MapPut("/{serviceId:guid}/override", async (Guid serviceId, OverrideServiceRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new OverrideServiceCommand(serviceId, r.NameAr ?? string.Empty, r.NameEn, r.DescriptionAr, r.DescriptionEn, r.CategoryId, r.Price, r.DurationMinutes, r.OnlineBookable, r.Reason ?? string.Empty, r.Version), ct)).ToHttpResult())
            .RequirePermission(ServicesOverride)
            .WithName("AdminOverrideService").WithSummary("Support correction of one shop service, with a reason; audited before → after.")
            .Produces<AdminServiceResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        services.MapPut("/{serviceId:guid}", async (Guid serviceId, AdminServiceRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new AdminUpdateServiceCommand(serviceId, r.NameAr ?? string.Empty, r.NameEn, r.DescriptionAr, r.DescriptionEn, r.CategoryId, r.Price, r.DurationMinutes, r.OnlineBookable, r.ProfessionalIds, r.Version), ct)).ToHttpResult())
            .RequirePermission(ServicesManage)
            .WithName("AdminUpdateService").WithSummary("Edits a shop's service and who does it, with the shop's own price and duration (audited).")
            .Produces<AdminServiceResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        api.MapPost("/admin/shops/{shopId:guid}/services", async (Guid shopId, AdminServiceRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new AdminCreateServiceCommand(shopId, r.NameAr ?? string.Empty, r.NameEn, r.DescriptionAr, r.DescriptionEn, r.CategoryId, r.Price, r.DurationMinutes, r.OnlineBookable, r.ProfessionalIds), ct))
                    .ToHttpResult(s => TypedResults.Created($"/api/v1/admin/services/{s.Id}", s)))
            .RequirePermission(ServicesManage).WithTags("Admin: services")
            .WithName("AdminCreateService").WithSummary("Adds a service to a shop with the shop's own price and duration, and the shop's barbers who do it (audited).")
            .Produces<AdminServiceResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/admin/shops/{shopId:guid}/packages", async (Guid shopId, AdminPackageRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new AdminCreatePackageCommand(shopId, r.NameAr ?? string.Empty, r.NameEn, r.DescriptionAr, r.DescriptionEn, r.Price, r.DurationMinutes, r.ServiceIds ?? []), ct))
                    .ToHttpResult(p => TypedResults.Created($"/api/v1/admin/packages/{p.Package.Id}", p)))
            .RequirePermission(ServicesManage).WithTags("Admin: services")
            .WithName("AdminCreatePackage").WithSummary("Adds a package of a shop's own services to that shop, with its own price and duration (audited).")
            .Produces<AdminPackageResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        var packages = api.MapGroup("/admin/packages").WithTags("Admin: services");
        packages.MapGet("/{packageId:guid}", async (Guid packageId, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetAdminPackageQuery(packageId), ct) is { } package ? TypedResults.Ok(package) : CatalogErrors.PackageNotFound().ToProblem())
            .RequirePermission(ServicesView)
            .WithName("AdminGetPackage").WithSummary("One shop package with its services.")
            .Produces<AdminPackageResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        packages.MapPut("/{packageId:guid}", async (Guid packageId, AdminPackageRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new AdminUpdatePackageCommand(packageId, r.NameAr ?? string.Empty, r.NameEn, r.DescriptionAr, r.DescriptionEn, r.Price, r.DurationMinutes, r.ServiceIds ?? [], r.Version), ct)).ToHttpResult())
            .RequirePermission(ServicesManage)
            .WithName("AdminUpdatePackage").WithSummary("Edits a shop's package (optimistic concurrency, audited).")
            .Produces<AdminPackageResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        packages.MapGet("/", async (int? page, int? pageSize, Guid? shopId, CatalogStateFilter? state, string? search, IDispatcher d, CancellationToken ct) =>
                TypedResults.Ok(await d.Send(new ListAdminPackagesQuery(new PageRequest(page, pageSize), shopId, state, search), ct)))
            .RequirePermission(ServicesView)
            .WithName("AdminListPackages").WithSummary("Shop packages across the platform (paged).");
        packages.MapPost("/{packageId:guid}/moderation", async (Guid packageId, ModerationRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new ModeratePackageCommand(packageId, r.Action, r.Reason), ct)).ToHttpResult())
            .RequirePermission(ServicesModerate)
            .WithName("AdminModeratePackage").WithSummary("Hides a package from customers (reason required) or shows it again (audited).")
            .Produces<AdminPackageListItem>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        var assignments = api.MapGroup("/admin/professionals/{professionalId:guid}/services").WithTags("Admin: professionals");
        assignments.MapGet("/", async (Guid professionalId, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetProfessionalServicesQuery(professionalId), ct) is { } result
                    ? TypedResults.Ok(result)
                    : Error.NotFound("professional.not_found", "The professional was not found.").ToProblem())
            .RequirePermission(ProfessionalsView)
            .WithName("AdminGetProfessionalServices").WithSummary("The professional's own shop's services, with that shop's prices, and which are assigned.")
            .Produces<ProfessionalServicesResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        assignments.MapPut("/", async (Guid professionalId, ProfessionalServicesRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SetProfessionalServicesCommand(professionalId, r.ServiceIds ?? []), ct)).ToHttpResult())
            .RequirePermission(AssignServices)
            .WithName("AdminSetProfessionalServices").WithSummary("Replaces the assigned services; only the professional's own shop's services (audited).")
            .Produces<ProfessionalServicesResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Ok<IReadOnlyList<ServiceCategoryResponse>>> PublicCategories(IDispatcher dispatcher, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dispatcher.Send(new ListPublicCategoriesQuery(), cancellationToken));

    private static async Task<IResult> PublicServices(string slug, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        await dispatcher.Send(new ListPublicServicesQuery(slug), cancellationToken) is { } services
            ? TypedResults.Ok(services)
            : Error.NotFound("shop.not_found", "The shop was not found.").ToProblem();

    private static async Task<IResult> PublicPackages(string slug, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        await dispatcher.Send(new ListPublicPackagesQuery(slug), cancellationToken) is { } packages
            ? TypedResults.Ok(packages)
            : Error.NotFound("shop.not_found", "The shop was not found.").ToProblem();
}
