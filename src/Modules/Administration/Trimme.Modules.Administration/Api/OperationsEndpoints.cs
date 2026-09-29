using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Administration.Application.Admin;

namespace Trimme.Modules.Administration.Api;

/// <summary>The admin overview (R-AD-01) and the audit log (R-AD-12).</summary>
internal static class OperationsEndpoints
{
    // Identity owns the permission catalogue; the endpoint matrix test fails if a code is not in it.
    private const string DashboardView = "Admin.Dashboard.View";
    private const string AuditView = "Admin.Audit.View";

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/admin/dashboard/overview", async (int? days, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetAdminOverviewQuery(days ?? 1), ct)).ToHttpResult())
            .RequirePermission(DashboardView).WithTags("Admin: overview")
            .WithName("GetAdminOverview")
            .WithSummary("Platform KPIs for today or the last 7 or 30 days, the 14-day trend, popular categories and top shops (SQL aggregates, platform calendar).")
            .Produces<AdminOverviewResponse>().ProducesProblem(StatusCodes.Status400BadRequest);

        var audit = api.MapGroup("/admin/audit").WithTags("Admin: audit");
        audit.MapGet("/", async (Guid? actorUserId, string? action, string? entityType, string? entityId, Guid? shopId, DateTimeOffset? from,
                    DateTimeOffset? to, string? cursor, int? pageSize, IDispatcher d, CancellationToken ct) =>
                TypedResults.Ok(await d.Send(new ListAuditQuery(actorUserId, action, entityType, entityId, shopId, from, to, cursor, pageSize), ct)))
            .RequirePermission(AuditView)
            .WithName("ListAuditEntries")
            .WithSummary("The audit trail, newest first (keyset cursor; at most 100 per page). Entries hold no personal data and are never edited.")
            .Produces<AuditPageResponse>();
        audit.MapGet("/facets", async (IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.Send(new GetAuditFacetsQuery(), ct)))
            .RequirePermission(AuditView)
            .WithName("GetAuditFacets").WithSummary("Every recorded action code and entity type, for the filters.")
            .Produces<AuditFacetsResponse>();
    }
}
