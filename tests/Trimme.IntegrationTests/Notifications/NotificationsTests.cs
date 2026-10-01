using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Web.Jobs;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Administration.Domain;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Otp;
using Trimme.Modules.Notifications.Domain;
using Trimme.Modules.Notifications.Infrastructure;
using Trimme.Modules.Notifications.Jobs;
using Trimme.Modules.Professionals.Domain;
using Trimme.Modules.Subscriptions.Jobs;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Notifications;

/// <summary>
/// Phase 15 (R-NTF-01…10, R-NEG-09, R-SUB-04/05): booking events through the outbox become rendered WhatsApp dispatches
/// for customers and professionals and reminder jobs in Hangfire that follow reschedules and cancellations; template edits
/// affect only future messages; safe test sends; the signed status webhook; in-app notifications; subscription warnings;
/// OTP over WhatsApp; the secured jobs dashboard. Tests call the processor and the jobs themselves (jobs are off in Testing).
/// </summary>
public sealed class NotificationsTests(PostgresFixture postgres)
{
    private const string FaisalNumber = "+966512300001";

    private static DateOnly Target => TodayAt(DateTimeOffset.UtcNow).AddDays(2);

    [Fact]
    public async Task CustomerAndProfessionalDispatches_ForLifecycle_AndReschedule_ReplacesReminderJobs()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "ntf_lifecycle", ct);
        await SetWhatsAppAsync(w.Admin, w.Faisal, FaisalNumber, ct);
        var customerPhone = IdentityTestData.NewPhone();
        using var noura = await CustomerWithPhoneAsync(w.Factory, "نورة القحطاني", customerPhone, ct);

        // Created (Confirmed): one customer confirmation and one professional new-booking alert.
        var booking = await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created);
        var bookingId = booking.GetProperty("id").GetGuid();
        await ProcessOutboxAsync(w.Factory, ct);
        var created = await DispatchesAsync(w.Factory, bookingId, ct);
        created.Select(d => $"{d.Event}:{d.Audience}").ShouldBe(["BookingConfirmed:Customer", "BookingConfirmed:Professional"], ignoreOrder: true);
        created.ShouldAllBe(d => d.Status == DispatchStatus.Queued && d.JobId != null && d.TemplateVersionNumber == 1);
        var toCustomer = created.Single(d => d.Audience == MessageAudience.Customer);
        var toProfessional = created.Single(d => d.Audience == MessageAudience.Professional);
        toCustomer.Body.ShouldNotBeNull().ShouldContain("نورة القحطاني");
        var shopNameAr = (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}", ct), ct)).GetProperty("nameAr").GetString()!;
        toCustomer.Body.ShouldContain(shopNameAr);
        toCustomer.Buttons.ShouldContain(b => b.Url.EndsWith($"/ar/account/bookings/{bookingId}", StringComparison.Ordinal));
        toProfessional.Body.ShouldNotBeNull().ShouldStartWith("لديك حجز جديد مع نورة القحطاني");
        toProfessional.Body.ShouldNotContain(customerPhone[4..], Case.Sensitive, "the professional never receives the customer's number");
        toProfessional.RecipientMasked.ShouldBe("+966 5•• ••• •01");
        created.ShouldAllBe(d => !d.RecipientProtected.Contains("5123"));

        // Sending: the fake provider delivers; the professional's number is now verified by delivery.
        await RunSendsAsync(w.Factory, created, ct);
        (await DispatchesAsync(w.Factory, bookingId, ct)).ShouldAllBe(d => d.Status == DispatchStatus.Delivered && d.Attempts == 1 && d.ProviderMessageId!.StartsWith("fake-"));
        (await ContactAsync(w.Factory, w.Faisal, ct)).Verification.ShouldBe(WhatsAppVerification.Verified);

        // Reminders: one per audience at start − 30 minutes, scheduled in Hangfire.
        var reminders = await RemindersAsync(w.Factory, bookingId, ct);
        reminders.Select(r => r.Audience).ShouldBe([MessageAudience.Customer, MessageAudience.Professional], ignoreOrder: true);
        reminders.ShouldAllBe(r => r.Status == ReminderStatus.Scheduled && r.DueAt == At(Target, 10).AddMinutes(-30));
        reminders.ShouldAllBe(r => JobState(w.Factory, r.JobId!) == "Scheduled");

        // At-least-once is idempotent: processing again changes nothing.
        await ProcessOutboxAsync(w.Factory, ct);
        (await DispatchesAsync(w.Factory, bookingId, ct)).Count.ShouldBe(2);

        // Reschedule: both audiences are told; the old reminder jobs are deleted and replaced (R-NTF-06).
        var version = (await OkAsync(noura.GetAsync($"/api/v1/me/bookings/{bookingId}", ct), ct)).GetProperty("version").GetUInt32();
        await OkAsync(noura.SendAsync(HttpMethod.Post, $"/api/v1/me/bookings/{bookingId}/reschedule", new { startsAt = At(Target, 12), version }, ct, headers: Key()), ct);
        await ProcessOutboxAsync(w.Factory, ct);
        (await DispatchesAsync(w.Factory, bookingId, ct)).Where(d => d.Event == MessageEvent.BookingRescheduled).Select(d => d.Audience)
            .ShouldBe([MessageAudience.Customer, MessageAudience.Professional], ignoreOrder: true);
        var afterMove = await RemindersAsync(w.Factory, bookingId, ct);
        afterMove.Where(r => r.Status == ReminderStatus.Cancelled).ShouldAllBe(r => JobState(w.Factory, r.JobId!) == "Deleted");
        afterMove.Count(r => r.Status == ReminderStatus.Cancelled).ShouldBe(2);
        var replacements = afterMove.Where(r => r.Status == ReminderStatus.Scheduled).ToList();
        replacements.Count.ShouldBe(2);
        replacements.ShouldAllBe(r => r.StartsAt == At(Target, 12) && r.DueAt == At(Target, 12).AddMinutes(-30) && JobState(w.Factory, r.JobId!) == "Scheduled");

        // A professional without a number gets nothing (spec §16); the customer still does.
        using var khalid = await CustomerWithPhoneAsync(w.Factory, "خالد", IdentityTestData.NewPhone(), ct);
        var second = (await OkAsync(BookAsync(khalid, w.SlugA, w.Haircut, w.Omar, At(Target, 15), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await ProcessOutboxAsync(w.Factory, ct);
        (await DispatchesAsync(w.Factory, second, ct)).Select(d => d.Audience).ShouldBe([MessageAudience.Customer]);

        // The shop cancels: cancellations for both, reminders cancelled and their jobs deleted.
        version = (await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings/{bookingId}", ct), ct)).GetProperty("booking").GetProperty("version").GetUInt32();
        await OkAsync(w.OwnerA.PostAsync($"/api/v1/shop/bookings/{bookingId}/transitions", new { to = "CancelledByShop", reason = "إغلاق طارئ", version }, ct), ct);
        await ProcessOutboxAsync(w.Factory, ct);
        (await DispatchesAsync(w.Factory, bookingId, ct)).Where(d => d.Event == MessageEvent.BookingCancelled).Select(d => d.Audience)
            .ShouldBe([MessageAudience.Customer, MessageAudience.Professional], ignoreOrder: true);
        (await RemindersAsync(w.Factory, bookingId, ct)).ShouldAllBe(r => r.Status == ReminderStatus.Cancelled && JobState(w.Factory, r.JobId!) == "Deleted");

        // In-app: the shop hears what the customer did, the customer hears what the shop did; nothing crosses shops.
        var shopInbox = await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/notifications", ct), ct);
        shopInbox.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("kind").GetString())
            .ShouldBe(["booking.created", "booking.rescheduled", "booking.created"], ignoreOrder: true);
        shopInbox.GetRawText().ShouldNotContain(customerPhone[4..]);
        shopInbox.GetRawText().ShouldNotContain("+966");
        var customerInbox = await OkAsync(noura.GetAsync("/api/v1/me/notifications", ct), ct);
        customerInbox.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("kind").GetString()).ShouldBe(["booking.cancelled"]);
        var shopBInbox = await OkAsync(w.OwnerB.GetAsync("/api/v1/shop/notifications", ct), ct);
        shopBInbox.GetProperty("total").GetInt32().ShouldBe(0);

        // Mark read: shared by the shop's users; another shop cannot touch it.
        var noticeId = shopInbox.GetProperty("items")[0].GetProperty("id").GetGuid();
        await FailsAsync(w.OwnerB.PostAsync($"/api/v1/shop/notifications/{noticeId}/read", null, ct), HttpStatusCode.NotFound, "notification.not_found", ct);
        (await OkAsync(w.StaffA.GetAsync("/api/v1/shop/notifications/unread-count", ct), ct)).GetProperty("unread").GetInt32().ShouldBe(3);
        await OkAsync(w.OwnerA.PostAsync($"/api/v1/shop/notifications/{noticeId}/read", null, ct), ct, HttpStatusCode.NoContent);
        (await OkAsync(w.StaffA.GetAsync("/api/v1/shop/notifications/unread-count", ct), ct)).GetProperty("unread").GetInt32().ShouldBe(2);
        await OkAsync(w.StaffA.PostAsync("/api/v1/shop/notifications/read-all", null, ct), ct, HttpStatusCode.NoContent);
        (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/notifications/unread-count", ct), ct)).GetProperty("unread").GetInt32().ShouldBe(0);

        // The admin booking page lists the booking's messages and reminder jobs; masked recipients only.
        var forAdmin = await OkAsync(w.Admin.GetAsync($"/api/v1/admin/whatsapp/bookings/{bookingId}", ct), ct);
        forAdmin.GetProperty("dispatches").GetArrayLength().ShouldBe(6);
        forAdmin.GetProperty("reminders").GetArrayLength().ShouldBe(4);
        forAdmin.GetRawText().ShouldNotContain(FaisalNumber[4..]);
    }

    [Fact]
    public async Task Reminder_Idempotent_AndSkipsWhenTheBookingMoved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "ntf_reminder", ct);
        await SetWhatsAppAsync(w.Admin, w.Faisal, FaisalNumber, ct);
        using var noura = await CustomerWithPhoneAsync(w.Factory, "نورة", IdentityTestData.NewPhone(), ct);
        var bookingId = (await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await ProcessOutboxAsync(w.Factory, ct);
        var reminder = (await RemindersAsync(w.Factory, bookingId, ct)).Single(r => r.Audience == MessageAudience.Professional);

        await RunReminderAsync(w.Factory, reminder.Id.Value, ct);
        await RunReminderAsync(w.Factory, reminder.Id.Value, ct);
        var sent = (await DispatchesAsync(w.Factory, bookingId, ct)).Where(d => d.Kind == DispatchKind.Reminder).ToList();
        sent.Count.ShouldBe(1, "a reminder job run twice sends once");
        sent[0].Event.ShouldBe(MessageEvent.BookingReminder);
        sent[0].Body.ShouldNotBeNull().ShouldStartWith("لديك حجز مع نورة بعد ٣٠ دقيقة");
        (await RemindersAsync(w.Factory, bookingId, ct)).Single(r => r.Id == reminder.Id).Status.ShouldBe(ReminderStatus.Sent);

        // The customer's reminder job runs after the booking moved but before the outbox caught up: it skips.
        var customerReminder = (await RemindersAsync(w.Factory, bookingId, ct)).Single(r => r.Audience == MessageAudience.Customer);
        var version = (await OkAsync(noura.GetAsync($"/api/v1/me/bookings/{bookingId}", ct), ct)).GetProperty("version").GetUInt32();
        await OkAsync(noura.SendAsync(HttpMethod.Post, $"/api/v1/me/bookings/{bookingId}/reschedule", new { startsAt = At(Target, 13), version }, ct, headers: Key()), ct);
        await RunReminderAsync(w.Factory, customerReminder.Id.Value, ct);
        (await RemindersAsync(w.Factory, bookingId, ct)).Single(r => r.Id == customerReminder.Id).Status.ShouldBe(ReminderStatus.Skipped);
        (await DispatchesAsync(w.Factory, bookingId, ct)).Count(d => d.Kind == DispatchKind.Reminder && d.Audience == MessageAudience.Customer).ShouldBe(0);
    }

    [Fact]
    public async Task TemplateEdit_AffectsOnlyFutureDispatches_AndFailedDispatchesCanBeRetried()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "ntf_templates", ct);
        await SetWhatsAppAsync(w.Admin, w.Faisal, FaisalNumber, ct);
        using var noura = await CustomerWithPhoneAsync(w.Factory, "نورة", IdentityTestData.NewPhone(), ct);
        var first = (await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await ProcessOutboxAsync(w.Factory, ct);
        var before = (await DispatchesAsync(w.Factory, first, ct)).Single(d => d.Audience == MessageAudience.Professional);

        // The admin edits the Arabic professional confirmation: invalid text is refused, then a draft is activated.
        var templates = await OkAsync(w.Admin.GetAsync("/api/v1/admin/whatsapp/templates", ct), ct);
        var slot = templates.GetProperty("items").EnumerateArray().Single(t =>
            t.GetProperty("event").GetString() == "BookingConfirmed" && t.GetProperty("audience").GetString() == "Professional" && t.GetProperty("locale").GetString() == "ar");
        var templateId = slot.GetProperty("id").GetGuid();
        var detail = await OkAsync(w.Admin.GetAsync($"/api/v1/admin/whatsapp/templates/{templateId}", ct), ct);
        detail.GetProperty("allowedPlaceholders").EnumerateArray().Select(p => p.GetString()).ShouldNotContain("manage_url");
        var bad = await FailsAsync(
            w.Admin.PutAsync($"/api/v1/admin/whatsapp/templates/{templateId}/draft", new { body = "{{manage_url}} {{customer_phone}}", version = detail.GetProperty("version").GetUInt32() }, ct),
            HttpStatusCode.BadRequest, "template.placeholder_not_allowed", ct);
        bad.ShouldContain("template.unknown_placeholder");
        var preview = await OkAsync(w.Admin.PostAsync($"/api/v1/admin/whatsapp/templates/{templateId}/preview", new { body = "حجز {{customer_name}} الساعة {{booking_time}}" }, ct), ct);
        preview.GetProperty("valid").GetBoolean().ShouldBeTrue();
        preview.GetProperty("body").GetString().ShouldBe("حجز سارة العتيبي الساعة ٥:٣٠ م", "sample data only, never a real customer");

        var drafted = await OkAsync(
            w.Admin.PutAsync($"/api/v1/admin/whatsapp/templates/{templateId}/draft", new { body = "نسخة ٢: حجز {{customer_name}} يوم {{booking_date}}", version = detail.GetProperty("version").GetUInt32() }, ct), ct);
        var draftId = drafted.GetProperty("versions")[0].GetProperty("id").GetGuid();
        drafted.GetProperty("versions")[0].GetProperty("status").GetString().ShouldBe("Draft");
        var activated = await OkAsync(
            w.Admin.PostAsync($"/api/v1/admin/whatsapp/templates/{templateId}/versions/{draftId}/activate", new { version = drafted.GetProperty("version").GetUInt32() }, ct), ct);
        activated.GetProperty("versions").EnumerateArray().Select(v => $"{v.GetProperty("number").GetInt32()}:{v.GetProperty("status").GetString()}").ShouldBe(["2:Active", "1:Archived"]);

        // A new booking renders version 2; the earlier dispatch keeps version 1, its text and its hash (R-NTF-07).
        using var khalid = await CustomerWithPhoneAsync(w.Factory, "خالد", IdentityTestData.NewPhone(), ct);
        var second = (await OkAsync(BookAsync(khalid, w.SlugA, w.Haircut, w.Faisal, At(Target, 14), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await ProcessOutboxAsync(w.Factory, ct);
        var after = (await DispatchesAsync(w.Factory, second, ct)).Single(d => d.Audience == MessageAudience.Professional);
        after.TemplateVersionNumber.ShouldBe(2);
        after.Body.ShouldStartWith("نسخة ٢: حجز خالد يوم");
        var unchanged = (await DispatchesAsync(w.Factory, first, ct)).Single(d => d.Audience == MessageAudience.Professional);
        unchanged.TemplateVersionNumber.ShouldBe(1);
        unchanged.TemplateVersionId.ShouldBe(before.TemplateVersionId);
        unchanged.Body.ShouldBe(before.Body);
        unchanged.ContentHash.ShouldBe(before.ContentHash);
        (await AuditAsync(w.Factory, "whatsapp_template.activated", ct)).ShouldHaveSingleItem().Summary.ShouldBe("BookingConfirmed · Professional · ar: v1 → v2");

        // A permanently failing number: the dispatch fails, the admins who watch WhatsApp are told, and a retry is audited.
        await SetWhatsAppAsync(w.Admin, w.Omar, "+966512349999", ct);
        using var sara = await CustomerWithPhoneAsync(w.Factory, "سارة", IdentityTestData.NewPhone(), ct);
        var third = (await OkAsync(BookAsync(sara, w.SlugA, w.Haircut, w.Omar, At(Target, 16), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await ProcessOutboxAsync(w.Factory, ct);
        var failing = (await DispatchesAsync(w.Factory, third, ct)).Single(d => d.Audience == MessageAudience.Professional);
        await RunSendsAsync(w.Factory, [failing], ct);
        failing = (await DispatchesAsync(w.Factory, third, ct)).Single(d => d.Audience == MessageAudience.Professional);
        failing.Status.ShouldBe(DispatchStatus.Failed);
        failing.LastError.ShouldNotBeNull().ShouldStartWith("fake.rejected");
        (await ContactAsync(w.Factory, w.Omar, ct)).Verification.ShouldBe(WhatsAppVerification.Failed);
        (await OkAsync(w.Admin.GetAsync("/api/v1/me/notifications", ct), ct)).GetProperty("items").EnumerateArray()
            .ShouldContain(n => n.GetProperty("kind").GetString() == "whatsapp.dispatch_failed");

        using var support = await IdentityTestData.SignInNewStaffAsync(w.Factory, SystemRoles.Support, ct);
        await FailsAsync(support.PostAsync($"/api/v1/admin/whatsapp/dispatches/{failing.Id.Value}/retry", null, ct), HttpStatusCode.Forbidden, null, ct);
        var retried = await OkAsync(w.Admin.PostAsync($"/api/v1/admin/whatsapp/dispatches/{failing.Id.Value}/retry", null, ct), ct);
        retried.GetProperty("status").GetString().ShouldBe("Queued");
        await FailsAsync(w.Admin.PostAsync($"/api/v1/admin/whatsapp/dispatches/{failing.Id.Value}/retry", null, ct), HttpStatusCode.Conflict, "dispatch.not_retryable", ct);
        (await AuditAsync(w.Factory, "whatsapp_dispatch.retried", ct)).ShouldHaveSingleItem();

        var log = await OkAsync(w.Admin.GetAsync("/api/v1/admin/whatsapp/dispatches?audience=Professional", ct), ct);
        log.GetProperty("counts").GetProperty("queued").GetInt32().ShouldBeGreaterThan(0);
        log.GetRawText().ShouldNotContain("512349999");
    }

    [Fact]
    public async Task TestSend_RequiresExplicitTestRecipient()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "ntf_testsend", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        using var support = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.Support, ct);
        // A number both the sign-in rule and libphonenumber's mobile check accept (NewPhone can produce +96652…, which
        // the stricter test-send parser refuses as invalid before it looks for a customer).
        var customerPhone = "+96655" + Random.Shared.Next(1_000_000, 10_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var customer = await IdentityTestData.SignInCustomerAsync(factory, customerPhone, ct);
        var templateId = (await OkAsync(admin.GetAsync("/api/v1/admin/whatsapp/templates", ct), ct)).GetProperty("items")[0].GetProperty("id").GetGuid();
        var path = $"/api/v1/admin/whatsapp/templates/{templateId}/test-send";

        await FailsAsync(admin.PostAsync(path, new { recipient = "+966512300077", confirmTestRecipient = false }, ct), HttpStatusCode.BadRequest, "confirmTestRecipient", ct);
        await FailsAsync(admin.PostAsync(path, new { recipient = "12", confirmTestRecipient = true }, ct), HttpStatusCode.BadRequest, "recipient", ct);
        await FailsAsync(admin.PostAsync(path, new { recipient = customerPhone, confirmTestRecipient = true }, ct), HttpStatusCode.Conflict, "whatsapp.test_recipient_is_customer", ct);
        await FailsAsync(support.PostAsync(path, new { recipient = "+966512300077", confirmTestRecipient = true }, ct), HttpStatusCode.Forbidden, null, ct);

        var sent = await OkAsync(admin.PostAsync(path, new { recipient = "+966512300077", confirmTestRecipient = true }, ct), ct);
        sent.GetProperty("kind").GetString().ShouldBe("Test");
        sent.GetProperty("status").GetString().ShouldBe("Delivered");
        sent.GetProperty("recipientMasked").GetString().ShouldBe("+966 5•• ••• •77");
        sent.GetProperty("bookingId").ValueKind.ShouldBe(JsonValueKind.Null);
        var audit = (await AuditAsync(factory, "whatsapp_template.test_sent", ct)).ShouldHaveSingleItem();
        (audit.Summary + audit.Reason).ShouldNotContain("512300077");
        factory.Services.GetRequiredService<FakeWhatsAppInbox>().All().ShouldContain(m => m.RecipientMasked == "+966 5•• ••• •77" && m.Body.Contains("سارة العتيبي"));
    }

    [Fact]
    public async Task Outbox_ProcessedOnce_RetriesWithBackoff_AndDeadLetters()
    {
        var ct = TestContext.Current.CancellationToken;
        // Whole seconds: PostgreSQL keeps microseconds, so exact instants compare equal after a round trip.
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        var probe = new ProbeConsumers();
        var factory = new TrimmeApiFactory(await postgres.CreateDatabaseAsync("ntf_outbox", ct))
        {
            ConfigureTestServices = services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton(probe);
                services.AddScoped<Trimme.BuildingBlocks.Application.Messaging.IOutboxConsumer, ProbeConsumerA>();
                services.AddScoped<Trimme.BuildingBlocks.Application.Messaging.IOutboxConsumer, ProbeConsumerB>();
            },
        };
        await using var _ = factory;
        await factory.MigrateAsync(ct);
        var ok = await AddOutboxAsync(factory, "probe.ok", clock, ct);
        var flaky = await AddOutboxAsync(factory, "probe.flaky", clock, ct);
        var broken = await AddOutboxAsync(factory, "probe.broken", clock, ct);

        await ProcessOnceAsync(factory, ct);
        await ProcessOnceAsync(factory, ct);
        probe.Count("A:probe.ok").ShouldBe(1, "a processed message is never delivered again");
        probe.Count("A:probe.flaky").ShouldBe(1);
        probe.Count("B:probe.flaky").ShouldBe(1, "B failed once; not retried before the backoff");
        var flakyRow = await OutboxRowAsync(factory, flaky, ct);
        flakyRow.ProcessedAt.ShouldBeNull();
        flakyRow.Attempts.ShouldBe(1);
        flakyRow.NextAttemptAt.ShouldBe(clock.GetUtcNow().AddSeconds(30));
        flakyRow.LastError.ShouldNotBeNull().ShouldContain("probe B is flaky");
        (await OutboxRowAsync(factory, ok, ct)).ProcessedAt.ShouldNotBeNull();

        clock.Advance(TimeSpan.FromSeconds(31));
        await ProcessOnceAsync(factory, ct);
        probe.Count("A:probe.flaky").ShouldBe(1, "consumer A succeeded earlier and is not run again (processed-message record)");
        probe.Count("B:probe.flaky").ShouldBe(2);
        (await OutboxRowAsync(factory, flaky, ct)).ProcessedAt.ShouldNotBeNull();

        for (var i = 0; i < OutboxMessage.MaxAttempts + 2; i++)
        {
            clock.Advance(TimeSpan.FromHours(1));
            await ProcessOnceAsync(factory, ct);
        }

        var dead = await OutboxRowAsync(factory, broken, ct);
        dead.DeadLetteredAt.ShouldNotBeNull();
        dead.Attempts.ShouldBe(OutboxMessage.MaxAttempts);
        probe.Count("A:probe.broken").ShouldBe(OutboxMessage.MaxAttempts, "a dead-lettered message is not delivered again");
    }

    [Fact]
    public async Task Rollback_NoDispatch_AndSeed_DoesNotDispatch()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "ntf_rollback", ct);

        // A rolled-back transaction leaves no outbox message, so nothing is ever sent (R-NTF-09).
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.Add(OutboxMessage.Create("booking.created", new { bookingId = Guid.CreateVersion7(), status = "Confirmed" }, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct);
            await transaction.RollbackAsync(ct);
        }

        // The development seed writes history only: no outbox message, no queued dispatch, no reminder job.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            foreach (var seeder in scope.ServiceProvider.GetServices<IDevSeeder>().OrderBy(s => s.Order))
            {
                await seeder.SeedAsync(scope.ServiceProvider, ct);
            }
        }

        await ProcessOutboxAsync(factory, ct);
        await using var read = factory.Services.CreateAsyncScope();
        var context = read.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        (await context.Set<OutboxMessage>().CountAsync(ct)).ShouldBe(0);
        var dispatches = await context.Set<WhatsAppDispatch>().AsNoTracking().ToListAsync(ct);
        dispatches.ShouldNotBeEmpty("the seed has demo history");
        dispatches.ShouldAllBe(d => d.Status != DispatchStatus.Queued && d.JobId == null);
        (await context.Set<ReminderSchedule>().CountAsync(ct)).ShouldBe(0);
        factory.Services.GetRequiredService<FakeWhatsAppInbox>().All().ShouldBeEmpty();
    }

    [Fact]
    public async Task Webhook_VerifiesTheSignature_AndMovesDispatchStatuses()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var plain = await IdentityTestData.CreateFactoryAsync(postgres, "ntf_hook_off", ct))
        {
            using var anonymous = ApiSession.Create(plain);
            await FailsAsync(anonymous.GetAsync("/api/v1/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=x&hub.challenge=1", ct), HttpStatusCode.NotFound, null, ct);
        }

        var settings = new Dictionary<string, string?> { ["WhatsApp:Meta:AppSecret"] = "app-secret", ["WhatsApp:Meta:VerifyToken"] = "verify-me" };
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "ntf_hook", ct, settings: settings);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        using var meta = ApiSession.Create(factory);
        (await (await meta.GetAsync("/api/v1/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=verify-me&hub.challenge=4815", ct)).Content.ReadAsStringAsync(ct)).ShouldBe("4815");
        await FailsAsync(meta.GetAsync("/api/v1/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=4815", ct), HttpStatusCode.Forbidden, null, ct);

        var templateId = (await OkAsync(admin.GetAsync("/api/v1/admin/whatsapp/templates", ct), ct)).GetProperty("items")[0].GetProperty("id").GetGuid();
        var dispatchId = (await OkAsync(admin.PostAsync($"/api/v1/admin/whatsapp/templates/{templateId}/test-send", new { recipient = "+966512300088", confirmTestRecipient = true }, ct), ct))
            .GetProperty("id").GetGuid();
        var providerId = (await OkAsync(admin.GetAsync($"/api/v1/admin/whatsapp/dispatches/{dispatchId}", ct), ct)).GetProperty("providerMessageId").GetString();
        var body = "{\"object\":\"whatsapp_business_account\",\"entry\":[{\"changes\":[{\"value\":{\"statuses\":[{\"id\":\"" + providerId
                   + "\",\"status\":\"read\"}]}}]}]}";

        using (var unsigned = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/whatsapp") { Content = new StringContent(body, Encoding.UTF8, "application/json") })
        {
            unsigned.Headers.Add("X-Hub-Signature-256", "sha256=" + new string('0', 64));
            (await meta.Client.SendAsync(unsigned, ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "no CSRF header is needed, but the signature is");
        }

        using var signed = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/whatsapp") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        signed.Headers.Add("X-Hub-Signature-256", "sha256=" + Convert.ToHexString(HMACSHA256.HashData("app-secret"u8, Encoding.UTF8.GetBytes(body))).ToLowerInvariant());
        (await meta.Client.SendAsync(signed, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await OkAsync(admin.GetAsync($"/api/v1/admin/whatsapp/dispatches/{dispatchId}", ct), ct)).GetProperty("dispatch").GetProperty("status").GetString().ShouldBe("Read");
    }

    [Fact]
    public async Task SubscriptionExpiry_NotifiesTheShopAndAdmins_OncePerMilestone()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "ntf_subs", ct, clock);
        var end = (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/subscription", ct), ct)).GetProperty("endDate").GetString()!;
        var endDate = DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture);

        clock.SetUtcNow(At(endDate.AddDays(-6), 9).ToUniversalTime());
        await RunExpiryAsync(w.Factory, ct);
        await RunExpiryAsync(w.Factory, ct);

        // Sessions expired with the clock a year ahead: read the stored notices directly.
        var shopNotices = await ShopNoticesAsync(w.Factory, w.Shops.A.ShopId, ct);
        var notice = shopNotices.Where(n => n.Kind == "subscription.expiring").ShouldHaveSingleItem();
        notice.Parameters["daysLeft"].ShouldBe("7");
        notice.Parameters["endDate"].ShouldBe(end);
        (await ShopNoticesAsync(w.Factory, w.Shops.B.ShopId, ct)).ShouldHaveSingleItem().Kind.ShouldBe("subscription.expiring");
        (await UserNoticesAsync(w.Factory, ct)).Count(n => n.Kind == "subscription.expiring").ShouldBe(2, "the one admin gets a notice per shop");

        clock.SetUtcNow(At(endDate.AddDays(1), 9).ToUniversalTime());
        await RunExpiryAsync(w.Factory, ct);
        (await ShopNoticesAsync(w.Factory, w.Shops.A.ShopId, ct)).Select(n => n.Kind).ShouldBe(["subscription.expiring", "subscription.expired"]);
    }

    [Fact]
    public async Task Otp_OverWhatsApp_UsesTheAuthenticationTemplate_AndNeverStoresTheCode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(
            postgres, "ntf_otp", ct, settings: new Dictionary<string, string?> { ["Identity:Otp:Sender"] = "WhatsApp" });
        using var session = ApiSession.Create(factory);
        await IdentityTestData.RequestOtpAsync(session, "+966512300099", ct);

        var inbox = factory.Services.GetRequiredService<FakeWhatsAppInbox>().All();
        inbox.ShouldHaveSingleItem().ShouldSatisfyAllConditions(m => m.Authentication.ShouldBeTrue(), m => m.RecipientMasked.ShouldBe("+966 5•• ••• •99"), m => m.Body.ShouldBeEmpty());
        factory.Services.GetRequiredService<DevOtpInbox>().Latest("+966512300099").ShouldBeNull("the development inbox is not used");
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<WhatsAppDispatch>().CountAsync(ct)).ShouldBe(0, "a sign-in code is never stored as a dispatch");
    }

    [Fact]
    public async Task JobsDashboard_IsOnlyForAdminsWithThePermission()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "ntf_dashboard", ct);
        using var anonymous = ApiSession.Create(factory);
        using var customer = await IdentityTestData.SignInCustomerAsync(factory, IdentityTestData.NewPhone(), ct);
        using var support = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.Support, ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);

        foreach (var caller in new[] { anonymous, customer, support })
        {
            using var refused = await caller.GetAsync(JobsSetup.DashboardPath, ct);
            refused.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        }

        using var page = await admin.GetAsync(JobsSetup.DashboardPath, ct);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        page.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        page.Headers.GetValues("Content-Security-Policy").Single().ShouldBe(SecurityHeadersMiddleware.DashboardContentSecurityPolicy);
        (await page.Content.ReadAsStringAsync(ct)).ShouldNotContain("Host=", Case.Insensitive, "the storage connection string is never shown");

        // Inner pages run inside the dashboard's branch, which moves its prefix into PathBase: they keep the dashboard's
        // policy rather than the API's `default-src 'none'`, which blocked their scripts and styles (found by the Phase 17
        // E2E CSP guard).
        using var inner = await admin.GetAsync($"{JobsSetup.DashboardPath}/recurring", ct);
        inner.StatusCode.ShouldBe(HttpStatusCode.OK);
        inner.Headers.GetValues("Content-Security-Policy").Single().ShouldBe(SecurityHeadersMiddleware.DashboardContentSecurityPolicy);
    }

    [Fact]
    public async Task ReviewContactShop_NotifiesOnlyThatShop_AndIsAuditedWithoutTheText()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "ntf_review_contact", ct);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            foreach (var seeder in scope.ServiceProvider.GetServices<IDevSeeder>().OrderBy(s => s.Order))
            {
                await seeder.SeedAsync(scope.ServiceProvider, ct);
            }
        }

        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        using var support = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.Support, ct);
        var review = (await OkAsync(admin.GetAsync($"/api/v1/admin/reviews?queue=All&shopId={DemoData.AlAsala.Id.Value}", ct), ct)).GetProperty("items")[0];
        var path = $"/api/v1/admin/reviews/{review.GetProperty("id").GetGuid()}/contact-shop";

        await FailsAsync(admin.PostAsync(path, new { message = "hi" }, ct), HttpStatusCode.BadRequest, "message", ct);
        await FailsAsync(support.PostAsync(path, new { message = "يرجى مراجعة التقييم" }, ct), HttpStatusCode.Forbidden, null, ct);
        await OkAsync(admin.PostAsync(path, new { message = "يرجى التواصل مع العميل بخصوص هذا التقييم" }, ct), ct, HttpStatusCode.NoContent);

        using var alAsala = await IdentityTestData.SignInStaffAsync(factory, DemoData.AlAsala.OwnerEmail, ct, DemoData.DefaultPassword);
        using var barberHouse = await IdentityTestData.SignInStaffAsync(factory, DemoData.BarberHouse.OwnerEmail, ct, DemoData.DefaultPassword);
        var inbox = await OkAsync(alAsala.GetAsync("/api/v1/shop/notifications", ct), ct);
        var notice = inbox.GetProperty("items").EnumerateArray().Single(n => n.GetProperty("kind").GetString() == "admin.message");
        notice.GetProperty("parameters").GetProperty("message").GetString().ShouldBe("يرجى التواصل مع العميل بخصوص هذا التقييم");
        (await OkAsync(barberHouse.GetAsync("/api/v1/shop/notifications", ct), ct)).GetProperty("items").EnumerateArray()
            .ShouldNotContain(n => n.GetProperty("kind").GetString() == "admin.message");
        var audit = (await AuditAsync(factory, "review.shop_contacted", ct)).ShouldHaveSingleItem();
        (audit.Summary + audit.Reason).ShouldNotContain("يرجى");
    }

    [Fact]
    public async Task RetentionAndMaintenance_ClearOldTextAndRecords_KeepHashesVersionsDeadLettersAndRecentRows()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "ntf_retention", ct, clock);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var templateId = (await OkAsync(admin.GetAsync("/api/v1/admin/whatsapp/templates", ct), ct)).GetProperty("items")[0].GetProperty("id").GetGuid();
        var path = $"/api/v1/admin/whatsapp/templates/{templateId}/test-send";
        var delivered = (await OkAsync(admin.PostAsync(path, new { recipient = "+966512300011", confirmTestRecipient = true }, ct), ct)).GetProperty("id").GetGuid();
        var queued = await OkAsync(admin.PostAsync(path, new { recipient = "+966512340000", confirmTestRecipient = true }, ct), ct);
        queued.GetProperty("status").GetString().ShouldBe("Queued", "a transient failure keeps the dispatch queued");
        var before = await DispatchAsync(factory, delivered, ct);

        // Old reliability records, written at the start.
        var start = clock.GetUtcNow();
        Guid oldProcessed, deadLettered, pending;
        var user = Guid.CreateVersion7();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
            var processed = OutboxMessage.Create("probe.old", new { n = 1 }, start);
            processed.MarkProcessed(start);
            var dead = OutboxMessage.Create("probe.dead", new { n = 2 }, start);
            for (var i = 0; i < OutboxMessage.MaxAttempts; i++)
            {
                dead.RecordFailure("boom", start);
            }

            var waiting = OutboxMessage.Create("probe.waiting", new { n = 3 }, start);
            db.AddRange(processed, dead, waiting, new ProcessedMessage(processed.Id, "probe", start), new IdempotencyRecord(user, "probe", "old", "hash", start));
            await db.SaveChangesAsync(ct);
            (oldProcessed, deadLettered, pending) = (processed.Id, dead.Id, waiting.Id);
        }

        // 91 days later: recent records, then both jobs.
        clock.Advance(TimeSpan.FromDays(91));
        var now = clock.GetUtcNow();
        Guid recentProcessed;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
            var recent = OutboxMessage.Create("probe.recent", new { n = 4 }, now);
            recent.MarkProcessed(now);
            db.AddRange(recent, new ProcessedMessage(recent.Id, "probe", now), new IdempotencyRecord(user, "probe", "recent", "hash", now));
            await db.SaveChangesAsync(ct);
            recentProcessed = recent.Id;
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DispatchRetentionJob>().RunAsync(ct);
            await scope.ServiceProvider.GetRequiredService<OutboxMaintenanceJob>().RunAsync(ct);
        }

        // Retention: the delivered message's text, buttons and parameters are gone; hash and version stay; the queued one is untouched.
        var purged = await DispatchAsync(factory, delivered, ct);
        purged.Body.ShouldBeNull();
        purged.Buttons.ShouldBeEmpty();
        purged.Parameters.ShouldBeEmpty();
        purged.ContentPurgedAt.ShouldBe(now);
        purged.ContentHash.ShouldBe(before.ContentHash);
        purged.TemplateVersionId.ShouldBe(before.TemplateVersionId);
        before.Body.ShouldNotBeNull();
        (await DispatchAsync(factory, queued.GetProperty("id").GetGuid(), ct)).Body.ShouldNotBeNull("a queued message is never purged");

        // Maintenance: old processed messages, delivery records and expired keys go; dead letters, pending and recent rows stay.
        await using var read = factory.Services.CreateAsyncScope();
        var context = read.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var remaining = await context.Set<OutboxMessage>().AsNoTracking().Select(m => m.Id).ToListAsync(ct);
        remaining.ShouldNotContain(oldProcessed);
        remaining.ShouldContain(deadLettered);
        remaining.ShouldContain(pending);
        remaining.ShouldContain(recentProcessed);
        (await context.Set<ProcessedMessage>().AsNoTracking().Select(p => p.MessageId).ToListAsync(ct)).ShouldBe([recentProcessed]);
        (await context.Set<IdempotencyRecord>().AsNoTracking().Where(r => r.UserId == user).Select(r => r.Key).ToListAsync(ct)).ShouldBe(["recent"]);
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<ApiSession> CustomerWithPhoneAsync(TrimmeApiFactory factory, string name, string phone, CancellationToken ct)
    {
        var session = await IdentityTestData.SignInCustomerAsync(factory, phone, ct);
        await OkAsync(session.PostAsync("/api/v1/auth/profile/complete", new { displayName = name, preferredLocale = "ar", termsAccepted = true }, ct), ct);
        return session;
    }

    private static Task<JsonElement> SetWhatsAppAsync(ApiSession admin, Guid professionalId, string number, CancellationToken ct) =>
        OkAsync(admin.PutAsync($"/api/v1/admin/professionals/{professionalId}/whatsapp", new { whatsAppNumber = number, notificationsEnabled = true }, ct), ct);

    private static async Task ProcessOutboxAsync(TrimmeApiFactory factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        for (var round = 0; round < 10 && await processor.ProcessBatchAsync(ct) > 0; round++)
        {
        }
    }

    private static async Task ProcessOnceAsync(TrimmeApiFactory factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(ct);
    }

    private static async Task<List<WhatsAppDispatch>> DispatchesAsync(TrimmeApiFactory factory, Guid bookingId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<WhatsAppDispatch>().AsNoTracking()
            .Where(d => d.BookingId == bookingId).OrderBy(d => d.CreatedAt).ToListAsync(ct);
    }

    private static async Task<WhatsAppDispatch> DispatchAsync(TrimmeApiFactory factory, Guid dispatchId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var id = DispatchId.From(dispatchId);
        return await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<WhatsAppDispatch>().AsNoTracking().SingleAsync(d => d.Id == id, ct);
    }

    private static async Task<List<ReminderSchedule>> RemindersAsync(TrimmeApiFactory factory, Guid bookingId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<ReminderSchedule>().AsNoTracking()
            .Where(r => r.BookingId == bookingId).OrderBy(r => r.CreatedAt).ToListAsync(ct);
    }

    private static async Task RunSendsAsync(TrimmeApiFactory factory, IEnumerable<WhatsAppDispatch> dispatches, CancellationToken ct)
    {
        foreach (var dispatch in dispatches)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SendDispatchJob>().RunAsync(dispatch.Id.Value, ct);
        }
    }

    private static async Task RunReminderAsync(TrimmeApiFactory factory, Guid reminderId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BookingReminderJob>().RunAsync(reminderId, ct);
    }

    private static async Task RunExpiryAsync(TrimmeApiFactory factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SubscriptionExpiryJob>().RunAsync(ct);
    }

    private static string? JobState(TrimmeApiFactory factory, string jobId)
    {
        using var connection = factory.Services.GetRequiredService<JobStorage>().GetConnection();
        return connection.GetStateData(jobId)?.Name;
    }

    private static async Task<ProfessionalContact> ContactAsync(TrimmeApiFactory factory, Guid professionalId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ISystemDataScope>().Begin();
        var id = new ProfessionalId(professionalId);
        return await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<ProfessionalContact>().AsNoTracking().SingleAsync(c => c.ProfessionalId == id, ct);
    }

    private static async Task<List<ShopNotification>> ShopNoticesAsync(TrimmeApiFactory factory, Guid shopId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ISystemDataScope>().Begin();
        var id = new ShopId(shopId);
        return await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<ShopNotification>().AsNoTracking()
            .Where(n => n.ShopId == id).OrderBy(n => n.CreatedAt).ToListAsync(ct);
    }

    private static async Task<List<UserNotification>> UserNoticesAsync(TrimmeApiFactory factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<UserNotification>().AsNoTracking().ToListAsync(ct);
    }

    private static async Task<List<AuditEntry>> AuditAsync(TrimmeApiFactory factory, string action, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<AuditEntry>().AsNoTracking().Where(a => a.Action == action).ToListAsync(ct);
    }

    private static async Task<Guid> AddOutboxAsync(TrimmeApiFactory factory, string type, TimeProvider clock, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var message = OutboxMessage.Create(type, new { probe = true }, clock.GetUtcNow());
        db.Add(message);
        await db.SaveChangesAsync(ct);
        return message.Id;
    }

    private static async Task<OutboxMessage> OutboxRowAsync(TrimmeApiFactory factory, Guid id, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == id, ct);
    }
}

/// <summary>Counts deliveries per consumer and message type.</summary>
internal sealed class ProbeConsumers
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _counts = new();

    public int Hit(string key) => _counts.AddOrUpdate(key, 1, (_, n) => n + 1);

    public int Count(string key) => _counts.GetValueOrDefault(key);
}

/// <summary>Handles every probe type; always fails on <c>probe.broken</c>.</summary>
internal sealed class ProbeConsumerA(ProbeConsumers probe) : Trimme.BuildingBlocks.Application.Messaging.IOutboxConsumer
{
    public string Name => "probe.a";

    public bool Handles(string messageType) => messageType.StartsWith("probe.", StringComparison.Ordinal);

    public Task HandleAsync(Trimme.BuildingBlocks.Application.Messaging.OutboxEnvelope message, Trimme.BuildingBlocks.Application.Messaging.OutboxConsumerContext context, CancellationToken cancellationToken)
    {
        probe.Hit($"A:{message.Type}");
        return message.Type == "probe.broken" ? throw new InvalidOperationException("probe A cannot handle this") : Task.CompletedTask;
    }
}

/// <summary>Handles <c>probe.flaky</c> only, failing the first time.</summary>
internal sealed class ProbeConsumerB(ProbeConsumers probe) : Trimme.BuildingBlocks.Application.Messaging.IOutboxConsumer
{
    public string Name => "probe.b";

    public bool Handles(string messageType) => messageType == "probe.flaky";

    public Task HandleAsync(Trimme.BuildingBlocks.Application.Messaging.OutboxEnvelope message, Trimme.BuildingBlocks.Application.Messaging.OutboxConsumerContext context, CancellationToken cancellationToken) =>
        probe.Hit($"B:{message.Type}") == 1 ? throw new InvalidOperationException("probe B is flaky") : Task.CompletedTask;
}
