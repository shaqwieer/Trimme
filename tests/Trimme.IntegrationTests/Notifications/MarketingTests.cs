using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shouldly;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Jobs;
using Trimme.IntegrationTests.Bookings;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Notifications.Domain;
using Trimme.Modules.Notifications.Jobs;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Notifications;

/// <summary>
/// D-155, the plans' features and the marketing limit (the client's «كيف الموقع يعرف الباقة وحد الـ ٥٠٠» and «حد رسايل
/// الواتساب انطبق على كل الرسايل»):
/// <list type="bullet">
/// <item>the plan carries loyalty, the win-back message and after how many days, and the monthly marketing messages;</item>
/// <item>only the win-back message counts: a plan with 0 still sends every confirmation, change and cancellation;</item>
/// <item>the win-back job invites back a customer whose last visit was N days ago, once, not one who opted out or who
/// has an upcoming booking, and keeps a copy in their bell;</item>
/// <item>the limit is checked when the message is sent: at the last slot two sends never both pass, the other is
/// «وصل الحد», the owner is told once, and the window runs a month from the subscription's start.</item>
/// </list>
/// </summary>
public sealed class MarketingTests(PostgresFixture postgres)
{
    private static async Task ProcessOutboxAsync(TrimmeApiFactory factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        for (var round = 0; round < 10 && await processor.ProcessBatchAsync(ct) > 0; round++)
        {
        }
    }

    private static async Task<List<WhatsAppDispatch>> DispatchesAsync(TrimmeApiFactory factory, CancellationToken ct, Guid? bookingId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var all = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<WhatsAppDispatch>().AsNoTracking();
        return await (bookingId is { } id ? all.Where(d => d.BookingId == id) : all).OrderBy(d => d.CreatedAt).ToListAsync(ct);
    }

    private static async Task SendAsync(TrimmeApiFactory factory, WhatsAppDispatch dispatch, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SendDispatchJob>().RunAsync(dispatch.Id.Value, ct);
    }

    private static async Task RunWinBackAsync(TrimmeApiFactory factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WinBackJob>().RunAsync(ct);
    }

    private static async Task ExecuteAsync(TrimmeApiFactory factory, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> ScalarAsync(TrimmeApiFactory factory, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteScalarAsync(ct);
    }

    /// <summary>SuperAdmin edits shop A's plan: its marketing messages a month and the win-back message.</summary>
    private static async Task<JsonElement> EditPlanAsync(BookingWorld w, int? messages, bool winBack, CancellationToken ct, int afterDays = 10, bool loyalty = false)
    {
        var planId = (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/subscription", ct), ct)).GetProperty("planId").GetGuid();
        var plan = await OkAsync(w.Admin.GetAsync($"/api/v1/admin/subscription-plans/{planId}", ct), ct);
        return await OkAsync(w.Admin.PutAsync($"/api/v1/admin/subscription-plans/{planId}", Edit(plan, messages, winBack, afterDays, loyalty), ct), ct);
    }

    private static object Edit(JsonElement plan, int? messages, bool winBack, int afterDays, bool loyalty) => new
    {
        nameAr = plan.GetProperty("nameAr").GetString(), nameEn = plan.GetProperty("nameEn").GetString(), descriptionAr = (string?)null, descriptionEn = (string?)null,
        features = new[] { new { ar = "ظهور", en = "Listed" } }, maxProfessionals = (int?)null, maxServices = (int?)null, intervalUnit = "Month", intervalCount = 12,
        trialDays = (int?)null, graceDays = (int?)null, availableToNewShops = true, version = plan.GetProperty("version").GetUInt32(),
        monthlyMarketingMessages = messages, includesLoyalty = loyalty, winBackEnabled = winBack, winBackAfterDays = afterDays,
    };

    /// <summary>A customer's completed visit at shop A, <paramref name="daysAgo"/> days ago (the seed's way: straight into the table).</summary>
    private static async Task<(ApiSession Customer, Guid CustomerId, Guid BookingId)> VisitedAsync(BookingWorld w, int daysAgo, DateTimeOffset now, CancellationToken ct)
    {
        var customer = await CustomerAsync(w.Factory, "نورة", ct);
        var me = await OkAsync(customer.GetAsync("/api/v1/me", ct), ct);
        var customerId = me.GetProperty("id").GetGuid();
        var start = now.AddDays(-daysAgo).AddHours(-1);
        var bookingId = Guid.CreateVersion7();
        await ExecuteAsync(w.Factory, """
            INSERT INTO bookings.bookings (id, shop_id, customer_id, customer_name, reference, professional_id, professional_name_ar, professional_name_en,
                service_id, item_name_ar, item_name_en, price, currency, duration_minutes, starts_at, ends_at, blocked_until, buffer_minutes, status, channel,
                payment_status, amount_due, loyalty_discount, created_at, package_items)
            SELECT @id, @shop, @customer, 'نورة', @reference, p.id, p.name_ar, p.name_en, @service, 'حلاقة', 'Haircut', 60, 'SAR', 30, @start, @end, @end, 0,
                'Completed', 'Online', 'NotApplicable', 60, 0, @start, '[]'
            FROM professionals.professionals p WHERE p.id = @pro
            """, ct, ("id", bookingId), ("shop", w.Shops.A.ShopId), ("customer", customerId), ("reference", $"GH-{Guid.NewGuid():N}"[..8].ToUpperInvariant()),
            ("service", w.Haircut), ("start", start), ("end", start.AddMinutes(30)), ("pro", w.Faisal));
        return (customer, customerId, bookingId);
    }

    [Fact]
    public async Task APlanCarriesItsFeatures_AndAPlanWithNoMarketingMessages_StillSendsEveryConfirmation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "mk_features", ct);

        // The plan form's real switches (D-155): validated, versioned like the plan's other details, SuperAdmin only.
        var planId = (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/subscription", ct), ct)).GetProperty("planId").GetGuid();
        var plan = await OkAsync(w.Admin.GetAsync($"/api/v1/admin/subscription-plans/{planId}", ct), ct);
        plan.GetProperty("monthlyMarketingMessages").ValueKind.ShouldBe(JsonValueKind.Null);
        plan.GetProperty("includesLoyalty").GetBoolean().ShouldBeFalse();
        plan.GetProperty("winBackEnabled").GetBoolean().ShouldBeFalse();
        plan.GetProperty("winBackAfterDays").GetInt32().ShouldBe(10);
        await FailsAsync(w.Admin.PutAsync($"/api/v1/admin/subscription-plans/{planId}", Edit(plan, -1, true, 10, true), ct), HttpStatusCode.BadRequest, "monthlyMarketingMessages", ct);
        await FailsAsync(w.Admin.PutAsync($"/api/v1/admin/subscription-plans/{planId}", Edit(plan, 0, true, 0, true), ct), HttpStatusCode.BadRequest, "winBackAfterDays", ct);
        await FailsAsync(w.OwnerA.PutAsync($"/api/v1/admin/subscription-plans/{planId}", Edit(plan, 0, true, 10, true), ct), HttpStatusCode.Forbidden, null, ct);
        var edited = await OkAsync(w.Admin.PutAsync($"/api/v1/admin/subscription-plans/{planId}", Edit(plan, 0, true, 14, true), ct), ct);
        edited.GetProperty("monthlyMarketingMessages").GetInt32().ShouldBe(0);
        edited.GetProperty("includesLoyalty").GetBoolean().ShouldBeTrue();
        edited.GetProperty("winBackEnabled").GetBoolean().ShouldBeTrue();
        edited.GetProperty("winBackAfterDays").GetInt32().ShouldBe(14);

        // The shop follows its plan: the owner's and the admin's subscription pages show the features and the usage.
        using (var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct))
        {
            var mine = await OkAsync(owner.GetAsync("/api/v1/shop/subscription", ct), ct);
            mine.GetProperty("features").GetProperty("loyaltyActive").GetBoolean().ShouldBeTrue();
            mine.GetProperty("features").GetProperty("winBackAfterDays").GetInt32().ShouldBe(14);
            mine.GetProperty("marketingUsage").GetProperty("allowance").GetInt32().ShouldBe(0);
            mine.GetProperty("marketingUsage").GetProperty("used").GetInt32().ShouldBe(0);
            (await OkAsync(owner.GetAsync("/api/v1/shop/loyalty", ct), ct)).GetProperty("included").GetBoolean().ShouldBeTrue();
        }

        // 0 marketing messages stops nothing else (the client's «رسايل التأكيد والتغيير والإلغاء توصل دايم»).
        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        var target = TodayAt(DateTimeOffset.UtcNow).AddDays(2);
        var booked = (await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, At(target, 10), ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await ProcessOutboxAsync(w.Factory, ct);
        var confirmation = (await DispatchesAsync(w.Factory, ct, booked)).Single(d => d.Audience == MessageAudience.Customer);
        await SendAsync(w.Factory, confirmation, ct);
        (await DispatchesAsync(w.Factory, ct, booked)).Single(d => d.Audience == MessageAudience.Customer).Status.ShouldBe(DispatchStatus.Delivered);
        (await ScalarAsync(w.Factory, "SELECT count(*) FROM notifications.whatsapp_usage WHERE shop_id = @s", ct, ("s", w.Shops.A.ShopId))).ShouldBe(0L);
    }

    [Fact]
    public async Task TheWinBackJob_InvitesOnce_NotTheOptedOutNorTheBooked_AndKeepsACopyInTheBell()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "mk_winback", ct);
        await EditPlanAsync(w, null, winBack: true, ct, afterDays: 10);
        var now = DateTimeOffset.UtcNow;

        var due = await VisitedAsync(w, 10, now, ct);
        var tooRecent = await VisitedAsync(w, 5, now, ct);
        var tooOld = await VisitedAsync(w, 40, now, ct);
        var optedOut = await VisitedAsync(w, 11, now, ct);
        var booked = await VisitedAsync(w, 12, now, ct);
        (await OkAsync(optedOut.Customer.PutAsync("/api/v1/me/win-back", new { enabled = false }, ct), ct)).GetProperty("winBackMessages").GetBoolean().ShouldBeFalse();
        await OkAsync(BookAsync(booked.Customer, w.SlugA, w.Haircut, w.Omar, At(TodayAt(now).AddDays(3), 12), ct), ct, HttpStatusCode.Created);

        await RunWinBackAsync(w.Factory, ct);
        await RunWinBackAsync(w.Factory, ct);

        var winBacks = (await DispatchesAsync(w.Factory, ct)).Where(d => d.Kind == DispatchKind.WinBack).ToList();
        winBacks.Count.ShouldBe(1, "once per visit, however many runs");
        winBacks[0].ShouldSatisfyAllConditions(
            d => d.BookingId.ShouldBe(due.BookingId),
            d => d.RecipientId.ShouldBe(due.CustomerId),
            d => d.Event.ShouldBe(MessageEvent.WinBack),
            d => d.Status.ShouldBe(DispatchStatus.Queued));
        (await OkAsync(due.Customer.GetAsync("/api/v1/me/notifications", ct), ct)).GetProperty("items").EnumerateArray()
            .ShouldContain(n => n.GetProperty("kind").GetString() == NoticeKinds.CustomerWinBack);
        (await OkAsync(optedOut.Customer.GetAsync("/api/v1/me/profile", ct), ct)).GetProperty("winBackMessages").GetBoolean().ShouldBeFalse();
        foreach (var other in new[] { tooRecent, tooOld, optedOut, booked })
        {
            other.Customer.Dispose();
        }

        due.Customer.Dispose();
    }

    [Fact]
    public async Task TheLastSlotOfTheWindow_GoesToOneOfTwoSends_TheOtherIsLimitReached_AndTheWindowRunsFromTheSubscription()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "mk_last_slot", ct, clock);
        var shopId = w.Shops.A.ShopId;
        await EditPlanAsync(w, 500, winBack: true, ct, afterDays: 10);
        var now = clock.GetUtcNow();
        var first = await VisitedAsync(w, 10, now, ct);
        var second = await VisitedAsync(w, 11, now, ct);

        // The window starts on the subscription's first day (today in this world), not the 1st of the month.
        var start = (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{shopId}/subscription", ct), ct)).GetProperty("startDate").GetString()!;
        var window = DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture);
        await ExecuteAsync(w.Factory, "INSERT INTO notifications.whatsapp_usage (shop_id, month, used) VALUES (@s, @m, 499)", ct, ("s", shopId), ("m", window));

        await RunWinBackAsync(w.Factory, ct);
        var queued = (await DispatchesAsync(w.Factory, ct)).Where(d => d.Kind == DispatchKind.WinBack).ToList();
        queued.Count.ShouldBe(2);
        await Task.WhenAll(queued.Select(d => SendAsync(w.Factory, d, ct)));

        var outcomes = (await DispatchesAsync(w.Factory, ct)).Where(d => d.Kind == DispatchKind.WinBack).ToList();
        outcomes.Count(d => d.Status == DispatchStatus.Delivered).ShouldBe(1);
        var kept = outcomes.Single(d => d.Status != DispatchStatus.Delivered);
        kept.Status.ShouldBe(DispatchStatus.LimitReached);
        kept.LastError.ShouldNotBeNull().ShouldStartWith("marketing.limit_reached");
        kept.SentAt.ShouldBeNull();
        (await ScalarAsync(w.Factory, "SELECT used FROM notifications.whatsapp_usage WHERE shop_id = @s AND month = @m", ct, ("s", shopId), ("m", window))).ShouldBe(500);

        // The owner is told once, and both subscription pages read 500 of 500 for this window.
        using (var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct))
        {
            var inbox = (await OkAsync(owner.GetAsync("/api/v1/shop/notifications", ct), ct)).GetProperty("items").EnumerateArray().ToList();
            inbox.Count(n => n.GetProperty("kind").GetString() == NoticeKinds.MarketingLimitReached).ShouldBe(1);
            var usage = (await OkAsync(owner.GetAsync("/api/v1/shop/subscription", ct), ct)).GetProperty("marketingUsage");
            usage.GetProperty("used").GetInt32().ShouldBe(500);
            usage.GetProperty("allowance").GetInt32().ShouldBe(500);
            usage.GetProperty("windowStart").GetString().ShouldBe(start);
        }

        // «وصل الحد» is counted apart in the admin's log and cannot be retried.
        var log = await OkAsync(w.Admin.GetAsync("/api/v1/admin/whatsapp/dispatches", ct), ct);
        log.GetProperty("counts").GetProperty("limitReached").GetInt32().ShouldBe(1);
        await FailsAsync(w.Admin.PostAsync($"/api/v1/admin/whatsapp/dispatches/{kept.Id.Value}/retry", new { reason = "إعادة المحاولة" }, ct), HttpStatusCode.Conflict, null, ct);

        // A month after the subscription's start, the count starts again.
        var next = window.AddMonths(1);
        MarketingWindows.Of(window, next).Start.ShouldBe(next);
        MarketingWindows.Of(window, next.AddDays(-1)).ShouldBe((window, next.AddDays(-1)));
        first.Customer.Dispose();
        second.Customer.Dispose();
    }
}
