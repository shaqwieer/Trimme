using Shouldly;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Notifications.Application;
using Trimme.Modules.Notifications.Domain;
using Trimme.Modules.Notifications.Infrastructure.Seeding;
using Trimme.Modules.Notifications.Jobs;
using Trimme.Modules.Subscriptions.Jobs;

namespace Trimme.UnitTests.Notifications;

/// <summary>Phase 15 domain rules: templates, placeholders, rendering, dispatch state, event plans, backoff, milestones.</summary>
public sealed class NotificationDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.CreateVersion7();

    [Fact]
    public void TemplatePlaceholders_Validated()
    {
        Codes(MessageAudience.Customer, "Hi {{customer_name}}, see {{manage_url}}").ShouldBeEmpty();
        Codes(MessageAudience.Customer, "").ShouldBe(["template.body_required"]);
        Codes(MessageAudience.Customer, new string('x', TemplateValidator.MaxBodyLength + 1)).ShouldBe(["template.body_too_long"]);
        Codes(MessageAudience.Customer, "Hi {{customer_phone}}").ShouldBe(["template.unknown_placeholder"], "there is no phone placeholder at all");
        Codes(MessageAudience.Professional, "Open {{manage_url}}").ShouldBe(["template.placeholder_not_allowed"], "the manage link is the customer's");
        Codes(MessageAudience.Customer, "Hi {{customer_name}").ShouldBe(["template.malformed_placeholder"]);
        Codes(MessageAudience.Customer, "Hi {customer_name}}").ShouldBe(["template.malformed_placeholder"]);

        var buttons = new TemplateButton[] { new("A", TemplateButtonTarget.ShopPage), new("B", TemplateButtonTarget.ManageBooking), new("C", TemplateButtonTarget.ShopPage) };
        TemplateValidator.Validate(MessageAudience.Customer, new TemplateContent("ok", buttons, null)).Select(i => i.Code)
            .ShouldBe(["template.too_many_buttons", "template.duplicate_button"], ignoreOrder: true);
        TemplateValidator.Validate(MessageAudience.Professional, new TemplateContent("ok", [new("Manage", TemplateButtonTarget.ManageBooking)], null))
            .Select(i => i.Code).ShouldBe(["template.button_not_allowed"]);
        TemplateValidator.Validate(MessageAudience.Customer, new TemplateContent("ok", [], "Bad Name")).Select(i => i.Code).ShouldBe(["template.provider_name_invalid"]);

        Placeholders.Used("{{shop_name}} {{booking_time}} {{shop_name}} {{ booking_date }}").ShouldBe(["shop_name", "booking_time", "booking_date"]);
    }

    [Fact]
    public void Formatting_MatchesTheWebRules_ArabicIndicClockDigits_LatinAmounts()
    {
        // The same strings the web app's format.ts produces (D-040), captured from Intl.
        var start = new DateTimeOffset(2026, 9, 18, 14, 30, 0, TimeSpan.Zero);
        MessageFormat.Date(start, "Asia/Riyadh", "ar").ShouldBe("الجمعة، ١٨ سبتمبر");
        MessageFormat.Date(start, "Asia/Riyadh", "en").ShouldBe("Friday 18 September");
        MessageFormat.Time(start, "Asia/Riyadh", "ar").ShouldBe("٥:٣٠ م");
        MessageFormat.Time(start, "Asia/Riyadh", "en").ShouldBe("5:30 pm");
        MessageFormat.Time(new DateTimeOffset(2026, 9, 18, 9, 5, 0, TimeSpan.Zero), "Asia/Riyadh", "en").ShouldBe("12:05 pm");
        MessageFormat.Time(new DateTimeOffset(2026, 9, 18, 6, 0, 0, TimeSpan.Zero), "Asia/Riyadh", "ar").ShouldBe("٩:٠٠ ص");
        MessageFormat.Minutes(30, "ar").ShouldBe("٣٠ دقيقة");
        MessageFormat.Minutes(45, "en").ShouldBe("45 min");
        MessageFormat.Amount(1234.5m, "SAR", "ar").ShouldBe("1,234.5 ر.س");
        MessageFormat.Amount(85m, "SAR", "en").ShouldBe("SAR 85");
    }

    [Fact]
    public void CustomerAndProfessionalTemplates_RenderSeparately()
    {
        var (booking, shop) = SampleMessage.For("ar", Now);
        var customerAr = Default(MessageEvent.BookingConfirmed, MessageAudience.Customer, "ar");
        var professionalAr = Default(MessageEvent.BookingConfirmed, MessageAudience.Professional, "ar");

        var forCustomer = RenderedMessage.From(customerAr, MessageComposer.Values(booking, shop, MessageAudience.Customer, "ar", 30, "https://trimme.test"),
            MessageComposer.Urls(booking, shop, MessageAudience.Customer, "ar", "https://trimme.test"));
        var forProfessional = RenderedMessage.From(professionalAr, MessageComposer.Values(booking, shop, MessageAudience.Professional, "ar", 30, "https://trimme.test"),
            MessageComposer.Urls(booking, shop, MessageAudience.Professional, "ar", "https://trimme.test"));

        forCustomer.Body.ShouldContain("سارة العتيبي");
        forCustomer.Body.ShouldContain("٥:٣٠ م");
        forCustomer.Body.ShouldContain("85 ر.س");
        forCustomer.Body.ShouldNotContain("{{");
        forCustomer.Buttons.Select(b => b.Url).ShouldContain(u => u.Contains("/ar/account/bookings/", StringComparison.Ordinal));
        forProfessional.Body.ShouldStartWith("لديك حجز جديد مع سارة العتيبي");
        forProfessional.Buttons.ShouldBeEmpty();
        forProfessional.Body.ShouldNotBe(forCustomer.Body);
        MessageComposer.Values(booking, shop, MessageAudience.Professional, "ar", 30, "https://trimme.test").Keys.ShouldNotContain(Placeholders.ManageUrl);

        var english = RenderedMessage.From(Default(MessageEvent.BookingReminder, MessageAudience.Customer, "en"),
            MessageComposer.Values(SampleMessage.For("en", Now).Booking, SampleMessage.For("en", Now).Shop, MessageAudience.Customer, "en", 30, "https://trimme.test"),
            new Dictionary<TemplateButtonTarget, string>());
        english.Body.ShouldStartWith("Reminder: your appointment at Al Asala Salon is in 30 min, at 5:30 pm.");
        english.Parameters.ShouldBe(["Al Asala Salon", "30 min", "5:30 pm", "Haircut and styling", "Majed Alharbi", "Prince Mohammed bin Abdulaziz Rd, Al Olaya, Riyadh"]);
    }

    [Fact]
    public void DefaultTemplates_CoverEverySlotOnce_AndAreValid()
    {
        DefaultTemplates.All.Count.ShouldBe(18, "customers five events and professionals four, in Arabic and English");
        DefaultTemplates.All.Select(t => t.Key).Distinct().Count().ShouldBe(18);
        DefaultTemplates.All.ShouldNotContain(t => t.Key.Audience == MessageAudience.Professional && t.Key.Event == MessageEvent.BookingPending);
        foreach (var (key, content) in DefaultTemplates.All)
        {
            TemplateValidator.Validate(key.Audience, content).ShouldBeEmpty($"{key}");
        }

        DefaultTemplates.StableId("template:x").ShouldBe(DefaultTemplates.StableId("template:x"));
    }

    [Fact]
    public void Template_Versions_DraftActivateAndHistory_NeverRewriteAnActiveVersion()
    {
        var template = NewTemplate();
        var v1 = template.ActiveVersion!;
        var original = v1.Body;

        var draft = template.SaveDraft(TemplateContent.Create("New {{customer_name}}", null, null), Admin, Now).Value;
        draft.Number.ShouldBe(2);
        draft.Status.ShouldBe(TemplateVersionStatus.Draft);
        template.SaveDraft(TemplateContent.Create("Newer {{customer_name}}", null, null), Admin, Now).Value.ShouldBeSameAs(draft, "one draft, edited in place");
        template.ActiveVersion!.Body.ShouldBe(original, "saving a draft never touches the active text");

        template.Activate(draft.Id, Admin, Now).IsSuccess.ShouldBeTrue();
        template.ActiveVersionId.ShouldBe(draft.Id);
        v1.Status.ShouldBe(TemplateVersionStatus.Archived);
        v1.Body.ShouldBe(original);

        template.Activate(draft.Id, Admin, Now).Error!.Code.ShouldBe("template.not_a_draft");
        var third = template.SaveDraft(TemplateContent.Create("Third {{shop_name}}", null, null), Admin, Now).Value;
        third.Number.ShouldBe(3);
        draft.Body.ShouldBe("Newer {{customer_name}}", "an active version is immutable: edits start a new draft");

        template.DraftFrom(v1.Id, Admin, Now).Value.Body.ShouldBe(original, "restoring copies an old wording into the draft");
        template.SaveDraft(TemplateContent.Create("{{manage_url}}", null, null), Admin, Now).IsFailure.ShouldBeFalse();
        NewTemplate(MessageAudience.Professional).SaveDraft(TemplateContent.Create("{{manage_url}}", null, null), Admin, Now).Error!.Code.ShouldBe("validation.failed");
    }

    [Fact]
    public void Dispatch_Attempts_Backoff_Failure_Retry_And_ProviderStatuses()
    {
        var dispatch = NewDispatch();
        dispatch.CanAttempt.ShouldBeTrue();
        for (var attempt = 1; attempt < WhatsAppDispatch.MaxAutomaticAttempts; attempt++)
        {
            dispatch.RecordFailure("fake.transient", permanent: false, Now).ShouldBeTrue();
            dispatch.Status.ShouldBe(DispatchStatus.Queued);
        }

        dispatch.RecordFailure("fake.transient", permanent: false, Now).ShouldBeFalse();
        dispatch.Status.ShouldBe(DispatchStatus.Failed);
        dispatch.Retry().IsSuccess.ShouldBeTrue();
        dispatch.Status.ShouldBe(DispatchStatus.Queued);
        dispatch.Retry().Error!.Code.ShouldBe("dispatch.not_retryable");

        var permanent = NewDispatch();
        permanent.RecordFailure("fake.rejected", permanent: true, Now).ShouldBeFalse();
        permanent.Status.ShouldBe(DispatchStatus.Failed);

        var sent = NewDispatch();
        sent.RecordAccepted("wamid.1", delivered: false, Now);
        sent.Status.ShouldBe(DispatchStatus.Sent);
        sent.RecordProviderStatus(DispatchStatus.Read, null, Now);
        sent.Status.ShouldBe(DispatchStatus.Read);
        sent.RecordProviderStatus(DispatchStatus.Delivered, null, Now);
        sent.Status.ShouldBe(DispatchStatus.Read, "statuses only move forward");

        var hash = sent.ContentHash;
        sent.PurgeContent(Now);
        sent.Body.ShouldBeNull();
        sent.Parameters.ShouldBeEmpty();
        sent.ContentHash.ShouldBe(hash, "the hash and the template version stay after the retention period");
        var purged = NewDispatch();
        purged.RecordFailure("fake.rejected", permanent: true, Now);
        purged.PurgeContent(Now);
        purged.Retry().Error!.Code.ShouldBe("dispatch.content_purged", "a purged message cannot be sent again");

        DispatchSender.Backoff(1).ShouldBe(TimeSpan.FromSeconds(30));
        DispatchSender.Backoff(4).ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Professional_NotificationEligibility()
    {
        WhatsAppTemplate.IsSent(MessageEvent.BookingPending, MessageAudience.Professional).ShouldBeFalse();
        WhatsAppTemplate.IsSent(MessageEvent.BookingPending, MessageAudience.Customer).ShouldBeTrue();

        Plan("booking.created", "Confirmed", "Confirmed").ShouldBe(["BookingConfirmed:Customer", "BookingConfirmed:Professional"]);
        Plan("booking.created", "Arrived", "Arrived").ShouldBe(["BookingConfirmed:Customer", "BookingConfirmed:Professional"], "a walk-in starting now");
        Plan("booking.created", "Pending", "Pending").ShouldBe(["BookingPending:Customer"], "the professional hears only once the shop confirms");
        Plan("booking.status_changed", "Confirmed", "Confirmed").ShouldBe(["BookingConfirmed:Customer", "BookingConfirmed:Professional"]);
        Plan("booking.status_changed", "Arrived", "Arrived").ShouldBeEmpty();
        Plan("booking.status_changed", "Completed", "Completed").ShouldBeEmpty();
        Plan("booking.rescheduled", "Pending", "Pending").ShouldBe(["BookingRescheduled:Customer"]);
        Plan("booking.rescheduled", "Confirmed", "Confirmed").ShouldBe(["BookingRescheduled:Customer", "BookingRescheduled:Professional"]);
        Plan("booking.cancelled", "CancelledByShop", "CancelledByShop").ShouldBe(["BookingCancelled:Customer", "BookingCancelled:Professional"]);
    }

    [Fact]
    public void Outbox_Backoff_ThenDeadLetter()
    {
        var message = OutboxMessage.Create("booking.created", new { id = 1 }, Now);
        message.RecordFailure("boom", Now).ShouldBeFalse();
        message.NextAttemptAt.ShouldBe(Now.AddSeconds(30));
        message.RecordFailure("boom", Now).ShouldBeFalse();
        message.NextAttemptAt.ShouldBe(Now.AddMinutes(1));
        OutboxMessage.Backoff(20).ShouldBe(TimeSpan.FromHours(1));
        for (var i = message.Attempts; i < OutboxMessage.MaxAttempts - 1; i++)
        {
            message.RecordFailure("boom", Now).ShouldBeFalse();
        }

        message.RecordFailure("boom", Now).ShouldBeTrue();
        message.DeadLetteredAt.ShouldBe(Now);
        message.NextAttemptAt.ShouldBeNull();
    }

    [Fact]
    public void SubscriptionExpiry_NoticeOncePerMilestone_WithinTheThreshold()
    {
        var end = new DateOnly(2026, 10, 31);
        SubscriptionExpiryJob.Notice(end, end.AddDays(-20), 14).ShouldBeNull();
        SubscriptionExpiryJob.Notice(end, end.AddDays(-13), 14).ShouldBe(("subscription.expiring", "14", 14));
        SubscriptionExpiryJob.Notice(end, end.AddDays(-10), 14)!.Value.Milestone.ShouldBe("14", "the same notice again: deduplicated");
        SubscriptionExpiryJob.Notice(end, end.AddDays(-6), 14)!.Value.Milestone.ShouldBe("7");
        SubscriptionExpiryJob.Notice(end, end.AddDays(-4), 14)!.Value.Milestone.ShouldBe("7", "a missed run catches up on the milestone it passed");
        SubscriptionExpiryJob.Notice(end, end.AddDays(-2), 14)!.Value.Milestone.ShouldBe("3");
        SubscriptionExpiryJob.Notice(end, end, 14).ShouldBe(("subscription.expiring", "1", 1));
        SubscriptionExpiryJob.Notice(end, end.AddDays(1), 14).ShouldBe(("subscription.expired", "expired", 0));
        SubscriptionExpiryJob.Notice(end, end.AddDays(9), 14).ShouldBeNull("an old lapse is not announced again");
        SubscriptionExpiryJob.Notice(end, end.AddDays(-4), 5)!.Value.Milestone.ShouldBe("5");
    }

    private static string[] Codes(MessageAudience audience, string body) =>
        [.. TemplateValidator.Validate(audience, TemplateContent.Create(body, null, null)).Select(i => i.Code)];

    private static TemplateContent Default(MessageEvent @event, MessageAudience audience, string locale) =>
        DefaultTemplates.All.Single(t => t.Key.Event == @event && t.Key.Audience == audience && t.Key.Locale == locale).Content;

    private static WhatsAppTemplate NewTemplate(MessageAudience audience = MessageAudience.Customer) =>
        WhatsAppTemplate.CreateWithActiveVersion(
            WhatsAppTemplateId.From(Guid.CreateVersion7()), TemplateVersionId.From(Guid.CreateVersion7()), MessageEvent.BookingConfirmed, audience, "ar",
            TemplateContent.Create("Hello {{customer_name}}", null, "trimme_test"), Now);

    private static WhatsAppDispatch NewDispatch()
    {
        var template = NewTemplate();
        return WhatsAppDispatch.Create(
            DispatchKind.Lifecycle, $"k:{Guid.CreateVersion7():N}", Guid.CreateVersion7(), new ShopId(Guid.CreateVersion7()), template, template.ActiveVersion!,
            "cipher", "+966 5•• ••• •12", Guid.CreateVersion7(), new RenderedMessage("Hello Sara", [], ["Sara"]), Now);
    }

    private static string[] Plan(string type, string eventStatus, string bookingStatus)
    {
        var change = new BookingOutboxEvent(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Now, Now, eventStatus, "Online", null, "Customer");
        var booking = SampleMessage.For("ar", Now).Booking with { Status = bookingStatus };
        return [.. BookingNotificationsConsumer.Plan(type, change, booking).Select(p => $"{p.Event}:{p.Audience}")];
    }
}
