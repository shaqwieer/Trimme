using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.Modules.Notifications.Application;
using Trimme.Modules.Notifications.Application.Admin;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.Modules.Notifications.Api;

/// <summary>The draft's text, buttons and optional Meta template name; send the template version read.</summary>
public sealed record SaveTemplateDraftRequest(string? Body, IReadOnlyList<TemplateButton>? Buttons, string? ProviderTemplateName, uint Version);

/// <summary>Send the template version read (optimistic concurrency).</summary>
public sealed record TemplateVersionActionRequest(uint Version);

/// <summary>Text and buttons to validate and render with sample data (nothing is saved).</summary>
public sealed record PreviewTemplateRequest(string? Body, IReadOnlyList<TemplateButton>? Buttons);

/// <summary>
/// A test send: the version (default the draft, else the active one), a mobile number typed by the admin (never prefilled)
/// and the explicit confirmation that it is a test recipient.
/// </summary>
public sealed record TestSendRequest(Guid? VersionId, string? Recipient, bool ConfirmTestRecipient);

internal static class NotificationEndpoints
{
    // Identity owns the permission catalogue; the endpoint matrix test fails if a code is not in it.
    private const string WhatsAppView = "Admin.WhatsApp.View";
    private const string TemplatesEdit = "Admin.WhatsApp.Templates.Edit";
    private const string TemplatesActivate = "Admin.WhatsApp.Templates.Activate";
    private const string TestSend = "Admin.WhatsApp.TestSend";
    private const string DispatchesRetry = "Admin.WhatsApp.Dispatches.Retry";
    private const string ShopRead = "Shop.Bookings.Read";

    // The account's own inbox is mapped with full paths (no group), so its routes carry no trailing slash.
    private const string MyInbox = "/me/notifications";
    private const string MyInboxTag = "Me: notifications";

    public static void Map(IEndpointRouteBuilder api)
    {
        MapTemplates(api.MapGroup("/admin/whatsapp/templates").WithTags("Admin: WhatsApp"));
        MapDispatches(api.MapGroup("/admin/whatsapp").WithTags("Admin: WhatsApp"));
        MapShopInbox(api.MapGroup("/shop/notifications").WithTags("Shop: notifications"));
        MapMyInbox(api);
        WhatsAppWebhook.Map(api);
    }

    private static void MapTemplates(RouteGroupBuilder group)
    {
        group.MapGet("/", async (IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.Send(new ListTemplatesQuery(), ct)))
            .RequirePermission(WhatsAppView)
            .WithName("AdminListWhatsAppTemplates")
            .WithSummary("Every template slot (event × audience × locale) with its active version number and text, and whether it has a draft.")
            .Produces<TemplateListResponse>();
        group.MapGet("/{templateId:guid}", async (Guid templateId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetTemplateQuery(templateId), ct)).ToHttpResult())
            .RequirePermission(WhatsAppView)
            .WithName("AdminGetWhatsAppTemplate")
            .WithSummary("A template slot with its version history (newest first) and the placeholders its audience may use.")
            .Produces<TemplateDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPut("/{templateId:guid}/draft", async (Guid templateId, SaveTemplateDraftRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new SaveTemplateDraftCommand(templateId, r.Body, r.Buttons, r.ProviderTemplateName, r.Version), ct)).ToHttpResult())
            .RequirePermission(TemplatesEdit)
            .WithName("AdminSaveWhatsAppTemplateDraft")
            .WithSummary("Creates the draft or replaces its text; validated against the placeholder whitelist. Active and archived versions never change.")
            .Produces<TemplateDetailResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/{templateId:guid}/versions/{versionId:guid}/restore", async (Guid templateId, Guid versionId, TemplateVersionActionRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new RestoreTemplateVersionCommand(templateId, versionId, r.Version), ct)).ToHttpResult())
            .RequirePermission(TemplatesEdit)
            .WithName("AdminRestoreWhatsAppTemplateVersion")
            .WithSummary("Copies an earlier version's text into the draft (it still needs activation).")
            .Produces<TemplateDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/{templateId:guid}/versions/{versionId:guid}/activate", async (Guid templateId, Guid versionId, TemplateVersionActionRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new ActivateTemplateVersionCommand(templateId, versionId, r.Version), ct)).ToHttpResult())
            .RequirePermission(TemplatesActivate)
            .WithName("AdminActivateWhatsAppTemplateVersion")
            .WithSummary("Makes the draft the active version (the previous one is archived). Only future messages use it; audited.")
            .Produces<TemplateDetailResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/{templateId:guid}/preview", async (Guid templateId, PreviewTemplateRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new PreviewTemplateCommand(templateId, r.Body, r.Buttons), ct)).ToHttpResult())
            .RequirePermission(WhatsAppView)
            .WithName("AdminPreviewWhatsAppTemplate")
            .WithSummary("Validates text and renders it with fixed sample data (no real booking or customer). Nothing is saved.")
            .Produces<TemplatePreviewResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{templateId:guid}/test-send", async (Guid templateId, TestSendRequest r, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new TestSendTemplateCommand(templateId, r.VersionId, r.Recipient, r.ConfirmTestRecipient), ct)).ToHttpResult())
            .RequirePermission(TestSend)
            .WithName("AdminTestSendWhatsAppTemplate")
            .WithSummary("Sends a version rendered with sample data to a number the admin typed and confirmed; a registered customer's number is refused. Audited without the number.")
            .Produces<DispatchResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapDispatches(RouteGroupBuilder group)
    {
        group.MapGet("/dispatches", async (
                    DispatchStatus? status, MessageAudience? audience, MessageEvent? @event, DispatchKind? kind, Guid? shopId, Guid? bookingId, int? page, int? pageSize,
                    IDispatcher d, CancellationToken ct) =>
                TypedResults.Ok(await d.Send(new ListDispatchesQuery(status, audience, @event, kind, shopId, bookingId, new PageRequest(page, pageSize)), ct)))
            .RequirePermission(WhatsAppView)
            .WithName("AdminListWhatsAppDispatches")
            .WithSummary("The WhatsApp dispatch log, newest first: masked recipient, template version, status, attempts and error; counts per status and the last 24 hours' delivery rate.")
            .Produces<DispatchListResponse>();
        group.MapGet("/dispatches/{dispatchId:guid}", async (Guid dispatchId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new GetDispatchQuery(dispatchId), ct)).ToHttpResult())
            .RequirePermission(WhatsAppView)
            .WithName("AdminGetWhatsAppDispatch")
            .WithSummary("A dispatch with its rendered text (until the retention period ends), its content hash and the template version that rendered it.")
            .Produces<DispatchDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/dispatches/{dispatchId:guid}/retry", async (Guid dispatchId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new RetryDispatchCommand(dispatchId), ct)).ToHttpResult())
            .RequirePermission(DispatchesRetry)
            .WithName("AdminRetryWhatsAppDispatch")
            .WithSummary("Queues a failed dispatch for one more attempt (audited).")
            .Produces<DispatchResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("/bookings/{bookingId:guid}", async (Guid bookingId, IDispatcher d, CancellationToken ct) =>
                TypedResults.Ok(await d.Send(new GetBookingNotificationsQuery(bookingId), ct)))
            .RequirePermission(WhatsAppView)
            .WithName("AdminGetBookingNotifications")
            .WithSummary("A booking's WhatsApp dispatches and its reminder jobs (audience, due time, status).")
            .Produces<BookingNotificationsResponse>();
    }

    private static void MapShopInbox(RouteGroupBuilder group)
    {
        group.MapGet("/", async (bool? unreadOnly, int? page, int? pageSize, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new ListShopNotificationsQuery(unreadOnly ?? false, new PageRequest(page, pageSize)), ct)).ToHttpResult())
            .RequirePermission(ShopRead)
            .WithName("ListShopNotifications")
            .WithSummary("The shop's in-app notifications, newest first (names, items and times; never a customer phone number).")
            .Produces<NotificationListResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/unread-count", async (IDispatcher d, CancellationToken ct) => (await d.Send(new ShopUnreadCountQuery(), ct)).ToHttpResult())
            .RequirePermission(ShopRead)
            .WithName("CountShopUnreadNotifications")
            .WithSummary("How many of the shop's notifications are unread.")
            .Produces<UnreadCountResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{notificationId:guid}/read", async (Guid notificationId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new MarkShopNotificationsReadCommand(notificationId), ct)).ToHttpResult())
            .RequirePermission(ShopRead)
            .WithName("MarkShopNotificationRead")
            .WithSummary("Marks one of the shop's notifications read.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/read-all", async (IDispatcher d, CancellationToken ct) =>
                (await d.Send(new MarkShopNotificationsReadCommand(null), ct)).ToHttpResult())
            .RequirePermission(ShopRead)
            .WithName("MarkAllShopNotificationsRead")
            .WithSummary("Marks every notification of the shop read.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapMyInbox(IEndpointRouteBuilder api)
    {
        api.MapGet(MyInbox, async (bool? unreadOnly, int? page, int? pageSize, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new ListMyNotificationsQuery(unreadOnly ?? false, new PageRequest(page, pageSize)), ct)).ToHttpResult())
            .WithTags(MyInboxTag).WithName("ListMyNotifications")
            .WithSummary("The signed-in account's own in-app notifications (customers: their bookings; admins: operational alerts), newest first.")
            .Produces<NotificationListResponse>().ProducesProblem(StatusCodes.Status403Forbidden);
        api.MapGet($"{MyInbox}/unread-count", async (IDispatcher d, CancellationToken ct) => (await d.Send(new MyUnreadCountQuery(), ct)).ToHttpResult())
            .WithTags(MyInboxTag).WithName("CountMyUnreadNotifications")
            .WithSummary("How many of the account's notifications are unread.")
            .Produces<UnreadCountResponse>().ProducesProblem(StatusCodes.Status403Forbidden);
        api.MapPost(MyInbox + "/{notificationId:guid}/read", async (Guid notificationId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new MarkMyNotificationsReadCommand(notificationId), ct)).ToHttpResult())
            .WithTags(MyInboxTag).WithName("MarkMyNotificationRead")
            .WithSummary("Marks one of the account's notifications read.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
        api.MapPost($"{MyInbox}/read-all", async (IDispatcher d, CancellationToken ct) =>
                (await d.Send(new MarkMyNotificationsReadCommand(null), ct)).ToHttpResult())
            .WithTags(MyInboxTag).WithName("MarkAllMyNotificationsRead")
            .WithSummary("Marks every notification of the account read.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status403Forbidden);
    }
}
