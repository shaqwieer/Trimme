using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Qr;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.QrAnalytics.Application;
using Trimme.Modules.QrAnalytics.Application.Admin;
using Trimme.Modules.QrAnalytics.Application.Public;
using Trimme.Modules.QrAnalytics.Application.Shop;
using Trimme.Modules.QrAnalytics.Domain;

namespace Trimme.Modules.QrAnalytics.Api;

/// <summary>The landing page's language, so the scan log can say which language visitors read.</summary>
public sealed record RecordQrVisitRequest(string? Locale);

/// <summary>A shop code (no professional) or a code for one of the shop's active professionals, with an optional label.</summary>
public sealed record CreateQrCodeRequest(Guid ShopId, Guid? ProfessionalId, string? Label);

/// <summary>Send the version read.</summary>
public sealed record SetQrCodeActiveRequest(uint Version);

internal static class QrEndpoints
{
    // Identity owns the permission catalogue; the endpoint matrix test fails if a code is not in it.
    private const string AdminView = "Admin.Qr.View";
    private const string AdminManage = "Admin.Qr.Manage";
    private const string ShopView = "Shop.Qr.View";

    public static void Map(IEndpointRouteBuilder api)
    {
        var @public = api.MapGroup("/public/qr").WithTags("Public");
        @public.MapGet("/{code}", async (string code, IDispatcher d, CancellationToken ct) =>
                await d.Send(new ResolveQrCodeQuery(code), ct) is { } target ? TypedResults.Ok(target) : QrErrors.NotFound().ToProblem())
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Qr)
            .WithName("ResolveQrCode")
            .WithSummary("What a scanned code opens: an active shop, or one of its active professionals. Unknown or switched-off codes are 404.")
            .Produces<QrTargetResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        @public.MapPost("/{code}/visits", RecordVisit)
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Qr)
            .WithName("RecordQrVisit")
            .WithSummary("Counts a scan (no IP address, no tracking; a reload reuses the visit) and sets the first-party attribution cookie for later bookings.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        MapAdmin(api.MapGroup("/admin/qr").WithTags("Admin: QR"));
        MapShop(api.MapGroup("/shop/qr").WithTags("Shop: QR"));
    }

    private static void MapAdmin(RouteGroupBuilder group)
    {
        group.MapGet("/codes", async (Guid? shopId, QrTargetType? targetType, bool? active, string? search, DateOnly? from, DateOnly? to, int? days, int? page,
                    int? pageSize, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new ListAdminQrCodesQuery(shopId, targetType, active, search, from, to, days, new PageRequest(page, pageSize)), ct)).ToHttpResult())
            .RequirePermission(AdminView)
            .WithName("AdminListQrCodes")
            .WithSummary("Every shop's QR codes (active first, newest first) with each code's scans and credited bookings over the period: from–to, or the last `days` days (default 30).")
            .Produces<QrCodeListResponse>().ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapPost("/codes", async (CreateQrCodeRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new CreateQrCodeCommand(r.ShopId, r.ProfessionalId, r.Label), ct))
                    .ToHttpResult(code => TypedResults.Created($"/api/v1/admin/qr/codes/{code.Id}", code)))
            .RequirePermission(AdminManage)
            .WithName("AdminCreateQrCode")
            .WithSummary("Creates a unique code for a shop, or for one of its active professionals (audited).")
            .Produces<QrCodeResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/codes/{codeId:guid}", async (Guid codeId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetAdminQrCodeQuery(codeId), ct)).ToHttpResult())
            .RequirePermission(AdminView)
            .WithName("AdminGetQrCode").WithSummary("One code with its target, printed URL and the last 30 days' figures.")
            .Produces<QrCodeResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/codes/{codeId:guid}/deactivate", async (Guid codeId, SetQrCodeActiveRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SetQrCodeActiveCommand(codeId, false, r.Version), ct)).ToHttpResult())
            .RequirePermission(AdminManage)
            .WithName("AdminDeactivateQrCode").WithSummary("Switches a code off: scanning it shows «not found»; its scans and bookings are kept (audited).")
            .Produces<QrCodeResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/codes/{codeId:guid}/activate", async (Guid codeId, SetQrCodeActiveRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SetQrCodeActiveCommand(codeId, true, r.Version), ct)).ToHttpResult())
            .RequirePermission(AdminManage)
            .WithName("AdminActivateQrCode").WithSummary("Switches a code back on (audited).")
            .Produces<QrCodeResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("/codes/{codeId:guid}/image", async (Guid codeId, string? format, int? size, IDispatcher d, CancellationToken ct) =>
                File(await d.Send(new GetAdminQrImageQuery(codeId, format, size), ct)))
            .RequirePermission(AdminView)
            .WithName("AdminDownloadQrCode").WithSummary("The code as PNG (size = pixels per module, 4–40), SVG or PDF (70 mm, vector).")
            .Produces<byte[]>(StatusCodes.Status200OK, "image/png", "image/svg+xml", "application/pdf").ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/analytics", async (DateOnly? from, DateOnly? to, int? days, Guid? shopId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetQrAnalyticsQuery(from, to, days, shopId), ct)).ToHttpResult())
            .RequirePermission(AdminView)
            .WithName("AdminQrAnalytics")
            .WithSummary("Scans, credited bookings and scan-to-booking conversion over a period of days: from–to, or the last `days` days (default 30; at most 366), per shop.")
            .Produces<QrAnalyticsResponse>().ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapShop(RouteGroupBuilder group)
    {
        group.MapGet("/codes", async (DateOnly? from, DateOnly? to, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new ListShopQrCodesQuery(from, to), ct)).ToHttpResult())
            .RequirePermission(ShopView)
            .WithName("ShopListQrCodes").WithSummary("The shop's own QR codes with their scans and bookings over the period (default: the last 30 days).")
            .Produces<ShopQrCodesResponse>().ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapGet("/codes/{codeId:guid}", async (Guid codeId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetShopQrCodeQuery(codeId), ct)).ToHttpResult())
            .RequirePermission(ShopView)
            .WithName("ShopGetQrCode").WithSummary("One of the shop's own codes; another shop's id is 404.")
            .Produces<QrCodeResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/codes/{codeId:guid}/image", async (Guid codeId, string? format, int? size, IDispatcher d, CancellationToken ct) =>
                File(await d.Send(new GetShopQrImageQuery(codeId, format, size), ct)))
            .RequirePermission(ShopView)
            .WithName("ShopDownloadQrCode").WithSummary("One of the shop's own codes as PNG, SVG or PDF.")
            .Produces<byte[]>(StatusCodes.Status200OK, "image/png", "image/svg+xml", "application/pdf").ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> RecordVisit(
        string code, RecordQrVisitRequest? request, HttpContext http, IDispatcher dispatcher, IOptions<QrOptions> options, CancellationToken cancellationToken)
    {
        Guid? current = Guid.TryParse(http.Request.Cookies[QrAttributionCookie.Name], out var visit) ? visit : null;
        var result = await dispatcher.Send(
            new RecordQrVisitCommand(code, current, http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString(), request?.Locale),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        http.Response.Cookies.Append(QrAttributionCookie.Name, result.Value.ToString("N"), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = QrAttributionCookie.Path,
            MaxAge = TimeSpan.FromDays(options.Value.AttributionDays),
            IsEssential = true,
        });
        return TypedResults.NoContent();
    }

    private static IResult File(Result<QrImageFile> result) =>
        result.IsFailure ? result.Error.ToProblem() : TypedResults.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
}
