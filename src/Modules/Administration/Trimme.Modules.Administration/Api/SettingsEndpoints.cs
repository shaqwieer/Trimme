using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Platform;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Administration.Application.Admin;

namespace Trimme.Modules.Administration.Api;

/// <summary>The editable platform settings; a stale <c>Version</c> answers 409.</summary>
public sealed record UpdatePlatformSettingsRequest(
    int MinLeadTimeMinutes,
    int BookingHorizonDays,
    int SlotStepMinutes,
    int CancellationCutoffMinutes,
    int ReviewWindowDays,
    int ReminderOffsetMinutes,
    int ExpiringSoonThresholdDays,
    SubscriptionEnforcement ExpiredSubscriptionEnforcement,
    bool HidePausedShopsFromDiscovery,
    double MapDefaultLatitude,
    double MapDefaultLongitude,
    int MapDefaultZoom,
    uint Version);

internal static class SettingsEndpoints
{
    // Identity owns the permission catalogue; the endpoint matrix test fails if a code is not in it.
    private const string SettingsView = "Admin.Settings.View";
    private const string SettingsEdit = "Admin.Settings.Edit";

    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/admin/settings").WithTags("Admin: settings");
        group.MapGet("/", async (IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.Send(new GetPlatformSettingsQuery(), ct)))
            .RequirePermission(SettingsView)
            .WithName("GetPlatformSettings").WithSummary("The platform settings: booking policy, reminders, subscription enforcement, map defaults.")
            .Produces<PlatformSettingsResponse>();
        group.MapPut("/", async (UpdatePlatformSettingsRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new UpdatePlatformSettingsCommand(
                    r.MinLeadTimeMinutes, r.BookingHorizonDays, r.SlotStepMinutes, r.CancellationCutoffMinutes, r.ReviewWindowDays, r.ReminderOffsetMinutes,
                    r.ExpiringSoonThresholdDays, r.ExpiredSubscriptionEnforcement, r.HidePausedShopsFromDiscovery, r.MapDefaultLatitude, r.MapDefaultLongitude,
                    r.MapDefaultZoom, r.Version), ct)).ToHttpResult())
            .RequirePermission(SettingsEdit)
            .WithName("UpdatePlatformSettings").WithSummary("Edits the platform settings (audited, optimistic concurrency).")
            .Produces<PlatformSettingsResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);
    }
}
