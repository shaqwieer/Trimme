using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Subscriptions.Application;
using Trimme.Modules.Subscriptions.Application.Admin;
using Trimme.Modules.Subscriptions.Domain;

namespace Trimme.Modules.Subscriptions.Api;

/// <summary>A new plan (Draft). <c>InitialPrice</c>, when given, becomes price version 1 from today.</summary>
public sealed record CreatePlanRequest(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    IReadOnlyList<PlanFeatureDto>? Features,
    int? MaxProfessionals,
    int? MaxServices,
    BillingIntervalUnit IntervalUnit,
    int IntervalCount,
    int? TrialDays,
    int? GraceDays,
    bool AvailableToNewShops,
    decimal? InitialPrice);

/// <summary>Edits a plan's details; prices change only through a new price version.</summary>
public sealed record UpdatePlanRequest(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    IReadOnlyList<PlanFeatureDto>? Features,
    int? MaxProfessionals,
    int? MaxServices,
    BillingIntervalUnit IntervalUnit,
    int IntervalCount,
    int? TrialDays,
    int? GraceDays,
    bool AvailableToNewShops,
    uint Version);

/// <summary>Every non-archived plan, in the new order.</summary>
public sealed record PlanOrderRequest(IReadOnlyList<Guid> OrderedIds);

/// <summary>A new price version in the platform currency, effective from today or a later date.</summary>
public sealed record AddPlanPriceRequest(decimal Amount, DateOnly EffectiveFrom, uint Version);

/// <summary>
/// Starts a shop's subscription. <c>StartDate</c> defaults to today (it cannot be in the future); the end follows the
/// plan's interval unless <c>DurationDays</c> is given.
/// </summary>
public sealed record AssignSubscriptionRequest(Guid PlanId, DateOnly? StartDate, int? DurationDays, string? Notes);

/// <summary>Records a renewal; the plan defaults to the current one and the start to the day after the current end.</summary>
public sealed record RenewSubscriptionRequest(Guid? PlanId, DateOnly? StartDate, int? DurationDays, string? Notes, uint Version);

/// <summary>SuperAdmin override of the period in force: a shop-specific price and/or end date, with a reason.</summary>
public sealed record OverrideSubscriptionRequest(decimal? Price, DateOnly? EndDate, string Reason, uint Version);

public sealed record SuspendSubscriptionRequest(string Reason, uint Version);

public sealed record ReinstateSubscriptionRequest(uint Version);

internal static class SubscriptionEndpoints
{
    // Identity owns the permission catalogue; the endpoint matrix test fails if a code is not in it.
    private const string View = "Admin.Subscriptions.View";
    private const string Assign = "Admin.Subscriptions.Assign";
    private const string Renew = "Admin.Subscriptions.Renew";
    private const string Suspend = "Admin.Subscriptions.Suspend";
    private const string PlansManage = "SuperAdmin.SubscriptionPlans.Manage";
    private const string Override = "SuperAdmin.Subscriptions.Override";
    private const string ShopRead = "Shop.Subscription.Read";

    public static void Map(IEndpointRouteBuilder api)
    {
        MapPlans(api.MapGroup("/admin/subscription-plans").WithTags("Admin: subscriptions"));
        MapShopSubscriptions(api.MapGroup("/admin/shops/{shopId:guid}/subscription").WithTags("Admin: subscriptions"));

        api.MapGet("/admin/subscriptions", async (SubscriptionStatus? status, string? search, int? page, int? pageSize, IDispatcher d, CancellationToken ct) =>
                TypedResults.Ok(await d.Send(new ListSubscriptionsQuery(new PageRequest(page, pageSize), status, search), ct)))
            .RequirePermission(View).WithTags("Admin: subscriptions")
            .WithName("ListSubscriptions").WithSummary("Shop subscriptions by status (most urgent end date first), with counts over every shop.")
            .Produces<AdminSubscriptionListResponse>();

        api.MapGet("/shop/subscription", async (IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetMySubscriptionQuery(), ct) is { } mine ? TypedResults.Ok(mine) : SubscriptionErrors.NoSubscription().ToProblem())
            .RequirePermission(ShopRead).WithTags("Shop: subscription")
            .WithName("GetShopSubscription").WithSummary("The signed-in shop's own subscription and renewal history (read-only).")
            .Produces<ShopSubscriptionResponse>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapPlans(RouteGroupBuilder group)
    {
        group.MapGet("/", async (IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.Send(new ListPlansQuery(), ct)))
            .RequirePermission(View)
            .WithName("ListSubscriptionPlans").WithSummary("Every plan in display order, with its current price and full price history.")
            .Produces<IReadOnlyList<PlanResponse>>();
        group.MapGet("/{planId:guid}", async (Guid planId, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetPlanQuery(planId), ct) is { } plan ? TypedResults.Ok(plan) : PlanErrors.NotFound().ToProblem())
            .RequirePermission(View)
            .WithName("GetSubscriptionPlan").WithSummary("One plan with its price history.")
            .Produces<PlanResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/", async (CreatePlanRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new CreatePlanCommand(
                    r.NameAr ?? string.Empty, r.NameEn ?? string.Empty, r.DescriptionAr, r.DescriptionEn, r.Features, r.MaxProfessionals, r.MaxServices,
                    r.IntervalUnit, r.IntervalCount, r.TrialDays, r.GraceDays, r.AvailableToNewShops, r.InitialPrice), ct))
                    .ToHttpResult(p => TypedResults.Created($"/api/v1/admin/subscription-plans/{p.Id}", p)))
            .RequirePermission(PlansManage)
            .WithName("CreateSubscriptionPlan").WithSummary("Creates a draft plan (SuperAdmin).")
            .Produces<PlanResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapPut("/{planId:guid}", async (Guid planId, UpdatePlanRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new UpdatePlanCommand(
                    planId, r.NameAr ?? string.Empty, r.NameEn ?? string.Empty, r.DescriptionAr, r.DescriptionEn, r.Features, r.MaxProfessionals, r.MaxServices,
                    r.IntervalUnit, r.IntervalCount, r.TrialDays, r.GraceDays, r.AvailableToNewShops, r.Version), ct)).ToHttpResult())
            .RequirePermission(PlansManage)
            .WithName("UpdateSubscriptionPlan").WithSummary("Edits a plan's details (SuperAdmin; optimistic concurrency). Existing subscriptions keep their snapshots.")
            .Produces<PlanResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        foreach (var (action, change) in new[] { ("publish", PlanStateChange.Publish), ("deactivate", PlanStateChange.Deactivate), ("archive", PlanStateChange.Archive) })
        {
            group.MapPost($"/{{planId:guid}}/{action}", async (Guid planId, IDispatcher d, CancellationToken ct) =>
                    (await d.Send(new ChangePlanStateCommand(planId, change), ct)).ToHttpResult())
                .RequirePermission(PlansManage)
                .WithName($"{char.ToUpperInvariant(action[0])}{action[1..]}SubscriptionPlan")
                .WithSummary(change switch
                {
                    PlanStateChange.Publish => "Offers the plan (it needs a price).",
                    PlanStateChange.Deactivate => "Stops offering the plan; existing subscriptions may still renew on it.",
                    _ => "Retires the plan for good; history keeps it.",
                })
                .Produces<PlanResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        }

        group.MapPut("/order", async (PlanOrderRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new ReorderPlansCommand(r.OrderedIds ?? []), ct)).ToHttpResult())
            .RequirePermission(PlansManage)
            .WithName("ReorderSubscriptionPlans").WithSummary("Sets the display order of every non-archived plan (SuperAdmin).")
            .Produces<IReadOnlyList<PlanResponse>>().ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapGet("/{planId:guid}/prices", async (Guid planId, IDispatcher d, CancellationToken ct) =>
                await d.Send(new GetPlanQuery(planId), ct) is { } plan ? TypedResults.Ok(plan.Prices) : PlanErrors.NotFound().ToProblem())
            .RequirePermission(View)
            .WithName("ListSubscriptionPlanPrices").WithSummary("The plan's price versions, newest first.")
            .Produces<IReadOnlyList<PlanPriceResponse>>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{planId:guid}/prices", async (Guid planId, AddPlanPriceRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new AddPlanPriceCommand(planId, r.Amount, r.EffectiveFrom, r.Version), ct)).ToHttpResult())
            .RequirePermission(PlansManage)
            .WithName("AddSubscriptionPlanPrice")
            .WithSummary("Adds a price version from a date (today or later). It applies only to periods starting on or after that date.")
            .Produces<PlanResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapShopSubscriptions(RouteGroupBuilder group)
    {
        group.MapGet("/", async (Guid shopId, IDispatcher d, CancellationToken ct) => (await d.Send(new GetShopSubscriptionQuery(shopId), ct)).ToHttpResult())
            .RequirePermission(View)
            .WithName("GetAdminShopSubscription").WithSummary("A shop's subscription: status, current period, every renewal and override.")
            .Produces<AdminShopSubscriptionResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/assign", async (Guid shopId, AssignSubscriptionRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new AssignSubscriptionCommand(shopId, r.PlanId, r.StartDate, r.DurationDays, r.Notes), ct)).ToHttpResult())
            .RequirePermission(Assign)
            .WithName("AssignShopSubscription").WithSummary("Starts the shop's subscription on a published plan; the price in force on the start date is recorded.")
            .Produces<AdminShopSubscriptionResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/renew", async (Guid shopId, RenewSubscriptionRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new RenewSubscriptionCommand(shopId, r.PlanId, r.StartDate, r.DurationDays, r.Notes, r.Version), ct)).ToHttpResult())
            .RequirePermission(Renew)
            .WithName("RenewShopSubscription").WithSummary("Records a renewal period (no payment in v1); earlier periods never change.")
            .Produces<AdminShopSubscriptionResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/override", async (Guid shopId, OverrideSubscriptionRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new OverrideSubscriptionCommand(shopId, r.Price, r.EndDate, r.Reason ?? string.Empty, r.Version), ct)).ToHttpResult())
            .RequirePermission(Override)
            .WithName("OverrideShopSubscription").WithSummary("SuperAdmin shop-specific price and/or end date for the period in force, with a reason (audited).")
            .Produces<AdminShopSubscriptionResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/suspend", async (Guid shopId, SuspendSubscriptionRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SuspendSubscriptionCommand(shopId, r.Reason ?? string.Empty, r.Version), ct)).ToHttpResult())
            .RequirePermission(Suspend)
            .WithName("SuspendShopSubscription").WithSummary("Suspends the subscription (reason required). Existing bookings are not touched.")
            .Produces<AdminShopSubscriptionResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/reinstate", async (Guid shopId, ReinstateSubscriptionRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new ReinstateSubscriptionCommand(shopId, r.Version), ct)).ToHttpResult())
            .RequirePermission(Suspend)
            .WithName("ReinstateShopSubscription").WithSummary("Lifts a suspension.")
            .Produces<AdminShopSubscriptionResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }
}
