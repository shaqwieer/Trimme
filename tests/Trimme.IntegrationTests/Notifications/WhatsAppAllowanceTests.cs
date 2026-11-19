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
/// D-149, the plans' WhatsApp allowance (the client's 6-5): a plan sends up to its monthly number of WhatsApp messages to
/// the shop's customers (none at 0, unlimited without one), counted when they reach the provider and checked before; the
/// in-app notices go on; the owner is told once a month; staff messages and sign-in codes are never counted; two sends at
/// the last slot never both pass; a new month starts again.
/// </summary>
public sealed class WhatsAppAllowanceTests(PostgresFixture postgres)
{
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

    private static async Task SendAsync(TrimmeApiFactory factory, WhatsAppDispatch dispatch, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SendDispatchJob>().RunAsync(dispatch.Id.Value, ct);
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

    private static async Task<Guid> BookedAsync(BookingWorld w, DateTimeOffset start, CancellationToken ct)
    {
        using var customer = await CustomerAsync(w.Factory, "نورة", ct);
        var id = (await OkAsync(BookAsync(customer, w.SlugA, w.Haircut, w.Faisal, start, ct), ct, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await ProcessOutboxAsync(w.Factory, ct);
        return id;
    }

    private static async Task<int> OwnerNoticesAsync(BookingWorld w, CancellationToken ct)
    {
        using var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);
        return (await OkAsync(owner.GetAsync("/api/v1/shop/notifications", ct), ct)).GetProperty("items").EnumerateArray()
            .Count(n => n.GetProperty("kind").GetString() == WhatsAppAllowanceGate.OwnerNoticeKind);
    }

    [Fact]
    public async Task AnAllowanceOfZero_SendsNoCustomerWhatsApp_StaffStillGetTheirs_AndTheOwnerIsToldOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "wa_zero", ct);
        var target = TodayAt(DateTimeOffset.UtcNow).AddDays(2);

        // SuperAdmin edits the shop's plan: no WhatsApp messages to customers (versioned like the plan's other details).
        var planId = (await OkAsync(w.Admin.GetAsync($"/api/v1/admin/shops/{w.Shops.A.ShopId}/subscription", ct), ct)).GetProperty("planId").GetGuid();
        var plan = await OkAsync(w.Admin.GetAsync($"/api/v1/admin/subscription-plans/{planId}", ct), ct);
        plan.GetProperty("monthlyWhatsAppMessages").ValueKind.ShouldBe(JsonValueKind.Null);
        object Edit(int? messages, uint version) => new
        {
            nameAr = plan.GetProperty("nameAr").GetString(), nameEn = plan.GetProperty("nameEn").GetString(), descriptionAr = (string?)null, descriptionEn = (string?)null,
            features = new[] { new { ar = "ظهور", en = "Listed" } }, maxProfessionals = (int?)null, maxServices = (int?)null, intervalUnit = "Month", intervalCount = 12,
            trialDays = (int?)null, graceDays = (int?)null, availableToNewShops = true, version, monthlyWhatsAppMessages = messages,
        };
        await FailsAsync(w.Admin.PutAsync($"/api/v1/admin/subscription-plans/{planId}", Edit(-1, plan.GetProperty("version").GetUInt32()), ct),
            HttpStatusCode.BadRequest, "monthlyWhatsAppMessages", ct);
        await FailsAsync(w.OwnerA.PutAsync($"/api/v1/admin/subscription-plans/{planId}", Edit(0, plan.GetProperty("version").GetUInt32()), ct), HttpStatusCode.Forbidden, null, ct);
        (await OkAsync(w.Admin.PutAsync($"/api/v1/admin/subscription-plans/{planId}", Edit(0, plan.GetProperty("version").GetUInt32()), ct), ct))
            .GetProperty("monthlyWhatsAppMessages").GetInt32().ShouldBe(0);

        // Faisal's number gets the staff message; the customer's is not sent, and is not a delivery failure for the admins.
        await OkAsync(w.Admin.PutAsync($"/api/v1/admin/professionals/{w.Faisal}/whatsapp", new { whatsAppNumber = "+966512399001", notificationsEnabled = true }, ct), ct);
        var first = await BookedAsync(w, At(target, 10), ct);
        foreach (var dispatch in await DispatchesAsync(w.Factory, first, ct))
        {
            await SendAsync(w.Factory, dispatch, ct);
        }

        var sent = await DispatchesAsync(w.Factory, first, ct);
        var toCustomer = sent.Single(d => d.Audience == MessageAudience.Customer);
        toCustomer.Status.ShouldBe(DispatchStatus.Failed);
        toCustomer.LastError.ShouldNotBeNull().ShouldStartWith("whatsapp.allowance_reached");
        toCustomer.Attempts.ShouldBe(1);
        toCustomer.SentAt.ShouldBeNull();
        sent.Single(d => d.Audience == MessageAudience.Professional).Status.ShouldBe(DispatchStatus.Delivered);
        (await OkAsync(w.Admin.GetAsync("/api/v1/me/notifications", ct), ct)).GetProperty("items").EnumerateArray()
            .ShouldNotContain(n => n.GetProperty("kind").GetString() == "dispatch.failed");

        // The in-app notices go on: the shop hears about the booking. The owner is told once, with the allowance.
        using (var owner = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct))
        {
            var inbox = (await OkAsync(owner.GetAsync("/api/v1/shop/notifications", ct), ct)).GetProperty("items").EnumerateArray().ToList();
            inbox.ShouldContain(n => n.GetProperty("kind").GetString() == "booking.created");
            var told = inbox.Single(n => n.GetProperty("kind").GetString() == WhatsAppAllowanceGate.OwnerNoticeKind);
            told.GetProperty("parameters").GetProperty("allowance").GetString().ShouldBe("0");
        }

        var second = await BookedAsync(w, At(target, 11), ct);
        foreach (var dispatch in await DispatchesAsync(w.Factory, second, ct))
        {
            await SendAsync(w.Factory, dispatch, ct);
        }

        (await DispatchesAsync(w.Factory, second, ct)).Single(d => d.Audience == MessageAudience.Customer).Status.ShouldBe(DispatchStatus.Failed);
        (await OwnerNoticesAsync(w, ct)).ShouldBe(1);

        // The subscription page: none used of none.
        using var ownerNow = await IdentityTestData.SignInStaffAsync(w.Factory, w.Shops.A.OwnerEmail, ct);
        var usage = (await OkAsync(ownerNow.GetAsync("/api/v1/shop/subscription", ct), ct)).GetProperty("messageUsage");
        usage.GetProperty("used").GetInt32().ShouldBe(0);
        usage.GetProperty("allowance").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task TheLastSlotOfTheMonth_GoesToOneOfTwoSends_AndANewMonthStartsAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var w = await ArrangeAsync(postgres, "wa_last_slot", ct, clock);
        await w.EnsureBarberLoginAsync(w.Faisal, ct);
        var shopId = w.Shops.A.ShopId;
        await ExecuteAsync(w.Factory,
            "UPDATE subscriptions.subscription_plans SET monthly_whats_app_messages = 500 WHERE id = (SELECT plan_id FROM subscriptions.shop_subscriptions WHERE shop_id = @s)",
            ct, ("s", shopId));

        // 499 of 500 used this month: the 500th passes, the 501st does not — even when both are sent at once.
        var month = WhatsAppMonths.Of(clock.GetUtcNow(), "Asia/Riyadh");
        await ExecuteAsync(w.Factory, "INSERT INTO notifications.whatsapp_usage (shop_id, month, used) VALUES (@s, @m, 499)", ct, ("s", shopId), ("m", month));
        var target = TodayAt(clock.GetUtcNow()).AddDays(2);
        var first = (await DispatchesAsync(w.Factory, await BookedAsync(w, At(target, 10), ct), ct)).Single(d => d.Audience == MessageAudience.Customer);
        var second = (await DispatchesAsync(w.Factory, await BookedAsync(w, At(target, 11), ct), ct)).Single(d => d.Audience == MessageAudience.Customer);
        await Task.WhenAll(SendAsync(w.Factory, first, ct), SendAsync(w.Factory, second, ct));

        var outcomes = new[] { (await DispatchesAsync(w.Factory, first.BookingId!.Value, ct)).Single(), (await DispatchesAsync(w.Factory, second.BookingId!.Value, ct)).Single() };
        outcomes.Count(d => d.Status == DispatchStatus.Delivered).ShouldBe(1);
        outcomes.Single(d => d.Status != DispatchStatus.Delivered).LastError.ShouldNotBeNull().ShouldStartWith("whatsapp.allowance_reached");
        (await ScalarAsync(w.Factory, "SELECT used FROM notifications.whatsapp_usage WHERE shop_id = @s AND month = @m", ct, ("s", shopId), ("m", month))).ShouldBe(500);
        (await OwnerNoticesAsync(w, ct)).ShouldBe(1);
        using (var admin = await IdentityTestData.SignInNewStaffAsync(w.Factory, Trimme.Modules.Identity.Domain.SystemRoles.SuperAdmin, ct))
        {
            var usage = (await OkAsync(admin.GetAsync($"/api/v1/admin/shops/{shopId}/subscription", ct), ct)).GetProperty("messageUsage");
            usage.GetProperty("used").GetInt32().ShouldBe(500);
            usage.GetProperty("allowance").GetInt32().ShouldBe(500);
        }

        // The 1st of the next month (shop time): customer messages go out again, counted afresh.
        var next = month.AddMonths(1);
        clock.SetUtcNow(new DateTimeOffset(next.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(3)).ToUniversalTime());
        var later = TodayAt(clock.GetUtcNow()).AddDays(2);
        var nextMonth = (await DispatchesAsync(w.Factory, await BookedAsync(w, At(later, 10), ct), ct)).Single(d => d.Audience == MessageAudience.Customer);
        await SendAsync(w.Factory, nextMonth, ct);
        (await DispatchesAsync(w.Factory, nextMonth.BookingId!.Value, ct)).Single().Status.ShouldBe(DispatchStatus.Delivered);
        (await ScalarAsync(w.Factory, "SELECT used FROM notifications.whatsapp_usage WHERE shop_id = @s AND month = @m", ct, ("s", shopId), ("m", next))).ShouldBe(1);
        (await OwnerNoticesAsync(w, ct)).ShouldBe(1);
    }
}
