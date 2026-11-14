using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Jobs;
using Trimme.IntegrationTests.Bookings;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Notifications.Domain;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Notifications;

/// <summary>
/// D-145 messages: a change of barber tells the customer (WhatsApp and in-app), the barber it left («cancelled») and the
/// barber it went to («confirmed»); a walk-in whose mobile the shop took gets the confirmation; a barber's login hears
/// about their own bookings, never about what they did themselves.
/// </summary>
public sealed class BarberMessagesTests(PostgresFixture postgres)
{
    private const string FaisalNumber = "+966512350001";
    private const string OmarNumber = "+966512350002";

    private static DateOnly Target => TodayAt(DateTimeOffset.UtcNow).AddDays(2);

    [Fact]
    public async Task AChangeOfBarber_TellsTheCustomer_TheBarberItLeft_AndTheBarberItWentTo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "ntf_barber_move", ct);
        using var faisal = await BarberWithMessagesAsync(w, w.Faisal, FaisalNumber, ct);
        using var omar = await BarberWithMessagesAsync(w, w.Omar, OmarNumber, ct);
        var customerPhone = IdentityTestData.NewPhone();
        using var noura = await IdentityTestData.SignInCustomerAsync(w.Factory, customerPhone, ct);
        await OkAsync(noura.PostAsync("/api/v1/auth/profile/complete", new { displayName = "نورة", preferredLocale = "ar", termsAccepted = true }, ct), ct);

        // The customer books Faisal: Faisal's login hears about it, Omar's does not.
        var booking = await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created);
        var id = booking.GetProperty("id").GetGuid();
        await ProcessOutboxAsync(w.Factory, ct);
        (await KindsAsync(faisal, ct)).ShouldBe(["booking.created"]);
        (await KindsAsync(omar, ct)).ShouldBeEmpty();
        (await DispatchesAsync(w.Factory, id, ct)).Select(Describe).ShouldBe(["BookingConfirmed:Customer", $"BookingConfirmed:Professional:{w.Faisal}"], ignoreOrder: true);

        // The owner gives it to Omar at the same time.
        await OkAsync(
            w.OwnerA.SendAsync(HttpMethod.Post, $"/api/v1/shop/bookings/{id}/professional", new { professionalId = w.Omar, version = booking.GetProperty("version").GetUInt32() }, ct,
                headers: Key()),
            ct);
        await ProcessOutboxAsync(w.Factory, ct);
        var afterMove = await DispatchesAsync(w.Factory, id, ct);
        afterMove.Select(Describe).ShouldBe(
            [
                "BookingConfirmed:Customer", $"BookingConfirmed:Professional:{w.Faisal}",
                "BookingProfessionalChanged:Customer", $"BookingCancelled:Professional:{w.Faisal}", $"BookingConfirmed:Professional:{w.Omar}",
            ],
            ignoreOrder: true);
        var toCustomer = afterMove.Single(d => d.Event == MessageEvent.BookingProfessionalChanged);
        toCustomer.ProviderTemplateName.ShouldBe("trimme_customer_booking_professional_changed");
        toCustomer.Body.ShouldNotBeNull().ShouldContain("عمر");
        toCustomer.Body.ShouldContain("في نفس الموعد");
        toCustomer.Buttons.ShouldContain(b => b.Url.EndsWith($"/ar/account/bookings/{id}", StringComparison.Ordinal));
        afterMove.Single(d => d.Event == MessageEvent.BookingCancelled).Body.ShouldNotBeNull().ShouldContain("نورة");

        // In-app: the customer, both barbers; not the shop's inbox (the shop did it).
        var customerNotice = (await NoticesAsync(noura, ct)).Single(n => n.GetProperty("kind").GetString() == "booking.professional_changed");
        customerNotice.GetProperty("parameters").GetProperty("professionalNameEn").GetString().ShouldBe("Omar");
        customerNotice.GetProperty("parameters").GetProperty("previousProfessionalNameAr").GetString().ShouldNotBeNullOrEmpty();
        (await KindsAsync(faisal, ct)).ShouldBe(["booking.professional_changed", "booking.created"]);
        (await KindsAsync(omar, ct)).ShouldBe(["booking.professional_changed"]);
        (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/notifications", ct), ct)).GetProperty("items").EnumerateArray()
            .ShouldNotContain(n => n.GetProperty("kind").GetString() == "booking.professional_changed");

        // Omar cancels it himself: the customer and Omar's WhatsApp are told (Omar heard of it), Faisal is not (no longer his);
        // Omar's login gets no notice of his own action.
        var current = await OkAsync(omar.GetAsync($"/api/v1/shop/bookings/{id}", ct), ct);
        await OkAsync(
            omar.PostAsync($"/api/v1/shop/bookings/{id}/transitions",
                new { to = "CancelledByShop", reason = "مرض مفاجئ", version = current.GetProperty("booking").GetProperty("version").GetUInt32() }, ct),
            ct);
        await ProcessOutboxAsync(w.Factory, ct);
        (await DispatchesAsync(w.Factory, id, ct)).Skip(afterMove.Count).Select(Describe)
            .ShouldBe(["BookingCancelled:Customer", $"BookingCancelled:Professional:{w.Omar}"], ignoreOrder: true);
        (await KindsAsync(omar, ct)).ShouldBe(["booking.professional_changed"]);
        (await KindsAsync(faisal, ct)).ShouldBe(["booking.professional_changed", "booking.created"]);
        (await NoticesAsync(noura, ct)).Select(n => n.GetProperty("kind").GetString()).ShouldContain("booking.cancelled");

        // The owner hears what the barber did (not what the owner did): the cancellation, with the barber's name.
        var ownerInbox = (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/notifications", ct), ct)).GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("bookingId").ValueKind == JsonValueKind.String && n.GetProperty("bookingId").GetGuid() == id)
            .ToList();
        ownerInbox.Select(n => n.GetProperty("kind").GetString()).ShouldBe(["booking.cancelled", "booking.created"]);
        ownerInbox[0].GetProperty("parameters").GetProperty("actor").GetString().ShouldBe("ShopUser");
        ownerInbox[0].GetProperty("parameters").GetProperty("professionalNameEn").GetString().ShouldBe("Omar");
    }

    [Fact]
    public async Task AWalkIn_WithAMobile_GetsTheConfirmation_WithTheShopsPage_AndNoReminder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "ntf_walk_in", ct);
        using var faisal = await BarberWithMessagesAsync(w, w.Faisal, FaisalNumber, ct);
        var walkInPhone = IdentityTestData.NewPhone();

        // The barber books a walk-in for later today... or the day after, with the customer's mobile; and one without.
        var withMobile = await OkAsync(
            faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
                new { serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(Target, 15), customerName = "زائر الصباح", customerPhone = walkInPhone }, ct,
                headers: Key()),
            ct, HttpStatusCode.Created);
        var withoutMobile = await OkAsync(
            faisal.SendAsync(HttpMethod.Post, "/api/v1/shop/bookings/walk-in",
                new { serviceId = w.Haircut, professionalId = w.Faisal, startsAt = At(Target, 16), customerName = "زائر المساء" }, ct, headers: Key()),
            ct, HttpStatusCode.Created);
        var id = withMobile.GetProperty("id").GetGuid();
        await ProcessOutboxAsync(w.Factory, ct);

        var dispatches = await DispatchesAsync(w.Factory, id, ct);
        dispatches.Select(Describe).ShouldBe(["BookingConfirmed:Customer", $"BookingConfirmed:Professional:{w.Faisal}"], ignoreOrder: true);
        var confirmation = dispatches.Single(d => d.Audience == MessageAudience.Customer);
        confirmation.RecipientId.ShouldBeNull("a walk-in has no account");
        confirmation.Locale.ShouldBe("ar", "the platform's language");
        confirmation.RecipientMasked.ShouldEndWith(walkInPhone[^2..]);
        confirmation.Body.ShouldNotBeNull().ShouldContain("زائر الصباح");
        confirmation.Body.ShouldNotContain(walkInPhone[4..]);
        confirmation.Buttons.ShouldContain(b => b.Url.EndsWith($"/ar/shops/{w.SlugA}", StringComparison.Ordinal));
        confirmation.Buttons.ShouldNotContain(b => b.Url.Contains("/account/bookings/", StringComparison.Ordinal));
        (await DispatchesAsync(w.Factory, withoutMobile.GetProperty("id").GetGuid(), ct)).Select(Describe)
            .ShouldBe([$"BookingConfirmed:Professional:{w.Faisal}"], "no mobile, no customer message");

        // No reminder for the walk-in (no account); the barber's own walk-ins are not announced to them or to the shop.
        (await RemindersAsync(w.Factory, id, ct)).Select(r => r.Audience).ShouldBe([MessageAudience.Professional]);
        (await KindsAsync(faisal, ct)).ShouldBeEmpty();
        (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/notifications", ct), ct)).GetProperty("items").EnumerateArray()
            .ShouldNotContain(n => n.GetProperty("bookingId").ValueKind == JsonValueKind.String && n.GetProperty("bookingId").GetGuid() == id);

        // The owner cancels it: the walk-in is told, and so is the barber (in-app too: the owner did it).
        await OkAsync(
            w.OwnerA.PostAsync($"/api/v1/shop/bookings/{id}/transitions",
                new { to = "CancelledByShop", reason = "إغلاق طارئ", version = withMobile.GetProperty("version").GetUInt32() }, ct),
            ct);
        await ProcessOutboxAsync(w.Factory, ct);
        (await DispatchesAsync(w.Factory, id, ct)).Skip(dispatches.Count).Select(Describe)
            .ShouldBe(["BookingCancelled:Customer", $"BookingCancelled:Professional:{w.Faisal}"], ignoreOrder: true);
        var notices = await NoticesAsync(faisal, ct);
        notices.Select(n => n.GetProperty("kind").GetString()).ShouldBe(["booking.cancelled"]);
        var digits = walkInPhone[4..];
        notices[0].GetProperty("parameters").EnumerateObject().ShouldNotContain(p => p.Value.GetString()!.Contains(digits, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A barber's login, with WhatsApp messages on for their number.</summary>
    private static async Task<ApiSession> BarberWithMessagesAsync(BookingWorld w, Guid professionalId, string number, CancellationToken ct)
    {
        var session = await BarberLoginTests.BarberAsync(w, professionalId, number, ct);
        await OkAsync(w.Admin.PutAsync($"/api/v1/admin/professionals/{professionalId}/whatsapp", new { whatsAppNumber = number, notificationsEnabled = true }, ct), ct);
        return session;
    }

    private static string Describe(WhatsAppDispatch d) =>
        d.Audience == MessageAudience.Professional ? $"{d.Event}:{d.Audience}:{d.RecipientId}" : $"{d.Event}:{d.Audience}";

    private static async Task<List<JsonElement>> NoticesAsync(ApiSession session, CancellationToken ct) =>
        [.. (await OkAsync(session.GetAsync("/api/v1/me/notifications", ct), ct)).GetProperty("items").EnumerateArray()];

    private static async Task<List<string?>> KindsAsync(ApiSession session, CancellationToken ct) =>
        [.. (await NoticesAsync(session, ct)).Select(n => n.GetProperty("kind").GetString())];

    private static async Task ProcessOutboxAsync(TrimmeApiFactory factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        for (var round = 0; round < 10 && await processor.ProcessBatchAsync(ct) > 0; round++)
        {
        }
    }

    private static async Task<List<WhatsAppDispatch>> DispatchesAsync(TrimmeApiFactory factory, Guid bookingId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<WhatsAppDispatch>().AsNoTracking()
            .Where(d => d.BookingId == bookingId).OrderBy(d => d.CreatedAt).ToListAsync(ct);
    }

    private static async Task<List<ReminderSchedule>> RemindersAsync(TrimmeApiFactory factory, Guid bookingId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<ReminderSchedule>().AsNoTracking()
            .Where(r => r.BookingId == bookingId).ToListAsync(ct);
    }
}
