using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Trimme.BuildingBlocks.Web.Realtime;
using Trimme.IntegrationTests.Infrastructure;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Bookings;

/// <summary>
/// Live operations updates (R-SD-10, R-TEN-06 realtime part, D-099): a shop's dashboard receives its own bookings'
/// changes and never another shop's; admins receive all; nobody chooses their group; customers and anonymous callers
/// cannot connect; messages carry ids, times and status only; WebSockets are accepted only from the web app's origin.
/// </summary>
public sealed class RealtimeTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private static DateOnly Target => TodayAt(DateTimeOffset.UtcNow).AddDays(2);

    /// <summary>A hub connection over long polling through the test server, with the session's cookies.</summary>
    private sealed class HubClient : IAsyncDisposable
    {
        private readonly Channel<string> _messages = Channel.CreateUnbounded<string>();

        public HubClient(TrimmeApiFactory factory, ApiSession session)
        {
            var cookies = session.Cookies.GetCookieHeader(ApiSession.Origin);
            Connection = new HubConnectionBuilder()
                .WithUrl(new Uri(factory.Server.BaseAddress, OperationsHub.Path), options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    options.Headers.Add("Cookie", cookies);
                })
                .Build();
            Connection.On<JsonElement>(OperationsHub.EventName, message => _messages.Writer.TryWrite(message.GetRawText()));
            Connection.Closed += _ =>
            {
                Closed.TrySetResult();
                return Task.CompletedTask;
            };
        }

        public HubConnection Connection { get; }

        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<JsonElement> NextAsync(CancellationToken ct)
        {
            var raw = await _messages.Reader.ReadAsync(ct).AsTask().WaitAsync(Wait, ct);
            raw.ShouldNotContain("customer", Case.Insensitive);
            raw.ShouldNotContain("phone", Case.Insensitive);
            raw.ShouldNotContain("mobile", Case.Insensitive);
            raw.ShouldNotContain("+966");
            return JsonDocument.Parse(raw).RootElement;
        }

        public bool HasMessage => _messages.Reader.TryPeek(out _);

        public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
    }

    private static async Task<HubClient> ConnectAsync(TrimmeApiFactory factory, ApiSession session, CancellationToken ct)
    {
        var client = new HubClient(factory, session);
        await client.Connection.StartAsync(ct);
        return client;
    }

    private static async Task<JsonElement> WalkInAsync(ApiSession owner, Guid serviceId, Guid professionalId, DateTimeOffset start, CancellationToken ct) =>
        await OkAsync(owner.PostAsync("/api/v1/shop/bookings/walk-in", new { serviceId, professionalId, startsAt = start, customerName = "زائر" }, ct), ct, HttpStatusCode.Created);

    [Fact]
    public async Task EachShop_ReceivesOnlyItsOwnBookings_AdminsReceiveAll_AndMessagesCarryNoCustomerData()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "rt_isolation", ct);
        await using var shopA = await ConnectAsync(w.Factory, w.OwnerA, ct);
        await using var shopB = await ConnectAsync(w.Factory, w.OwnerB, ct);
        await using var admin = await ConnectAsync(w.Factory, w.Admin, ct);

        // A walk-in at shop A (one save, no explicit transaction) reaches A and the admins.
        var walkIn = await WalkInAsync(w.OwnerA, w.Haircut, w.Faisal, At(Target, 10), ct);
        var seen = await shopA.NextAsync(ct);
        seen.GetProperty("kind").GetString().ShouldBe("booking.created");
        seen.GetProperty("bookingId").GetGuid().ShouldBe(walkIn.GetProperty("id").GetGuid());
        seen.GetProperty("shopId").GetGuid().ShouldBe(w.Shops.A.ShopId);
        seen.GetProperty("status").GetString().ShouldBe("Confirmed");
        (await admin.NextAsync(ct)).GetProperty("bookingId").GetGuid().ShouldBe(walkIn.GetProperty("id").GetGuid());

        // A customer's online booking (an explicit transaction) is published after its commit.
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        var online = await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Omar, At(Target, 11), ct), ct, HttpStatusCode.Created);
        (await shopA.NextAsync(ct)).GetProperty("bookingId").GetGuid().ShouldBe(online.GetProperty("id").GetGuid());

        // A status change too.
        await OkAsync(w.OwnerA.PostAsync($"/api/v1/shop/bookings/{walkIn.GetProperty("id").GetGuid()}/transitions",
            new { to = "CancelledByShop", reason = "اختبار", version = walkIn.GetProperty("version").GetUInt32() }, ct), ct);
        (await shopA.NextAsync(ct)).GetProperty("kind").GetString().ShouldBe("booking.cancelled");

        // Shop B saw none of that: the first thing it receives is its own booking (R-TEN-06).
        shopB.HasMessage.ShouldBeFalse();
        var own = await WalkInAsync(w.OwnerB, w.ServiceB, w.ProB, At(Target, 10), ct);
        var first = await shopB.NextAsync(ct);
        first.GetProperty("bookingId").GetGuid().ShouldBe(own.GetProperty("id").GetGuid());
        first.GetProperty("shopId").GetGuid().ShouldBe(w.Shops.B.ShopId);
        shopA.HasMessage.ShouldBeFalse();

        // The admins received every change, in order.
        (await admin.NextAsync(ct)).GetProperty("bookingId").GetGuid().ShouldBe(online.GetProperty("id").GetGuid());
        (await admin.NextAsync(ct)).GetProperty("kind").GetString().ShouldBe("booking.cancelled");
        (await admin.NextAsync(ct)).GetProperty("bookingId").GetGuid().ShouldBe(own.GetProperty("id").GetGuid());

        // No group can be requested: the hub has no client-callable methods.
        await Should.ThrowAsync<HubException>(() => shopB.Connection.InvokeAsync("JoinGroup", $"shop:{w.Shops.A.ShopId}", ct));
        await Should.ThrowAsync<HubException>(() => shopB.Connection.InvokeAsync("AddToGroupAsync", $"shop:{w.Shops.A.ShopId}", ct));
    }

    [Fact]
    public async Task Customers_AndAnonymousCallers_CannotConnect()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "rt_refused", ct);

        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        await using var customer = new HubClient(w.Factory, noura);
        try
        {
            await customer.Connection.StartAsync(ct);
        }
        catch (Exception)
        {
            customer.Closed.TrySetResult();
        }

        await customer.Closed.Task.WaitAsync(Wait, ct);
        customer.Connection.State.ShouldBe(HubConnectionState.Disconnected);

        using var anonymous = ApiSession.Create(w.Factory);
        using (var negotiate = await anonymous.PostAsync($"{OperationsHub.Path}/negotiate?negotiateVersion=1", null, ct))
        {
            negotiate.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        await using var nobody = new HubClient(w.Factory, anonymous);
        await Should.ThrowAsync<Exception>(() => nobody.Connection.StartAsync(ct));
    }

    [Fact]
    public async Task HubRequests_FromAnotherOrigin_AreRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "rt_origin_http", ct);
        var path = $"{OperationsHub.Path}/negotiate?negotiateVersion=1";
        foreach (var (origin, status) in new[] { ("https://evil.example", HttpStatusCode.Forbidden), (TrimmeApiFactory.AllowedTestOrigin, HttpStatusCode.OK) })
        {
            using var response = await w.OwnerA.SendAsync(HttpMethod.Post, path, null, ct, headers: new Dictionary<string, string> { ["Origin"] = origin });
            response.StatusCode.ShouldBe(status, origin);
        }
    }

    [Fact]
    public async Task WebSockets_AreAcceptedOnlyFromTheWebAppsOrigin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "rt_origin", ct);
        var cookies = w.OwnerA.Cookies.GetCookieHeader(ApiSession.Origin);

        HubConnection Connect(string origin) => new HubConnectionBuilder()
            .WithUrl(new Uri(w.Factory.Server.BaseAddress, OperationsHub.Path), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, token) =>
                {
                    var client = w.Factory.Server.CreateWebSocketClient();
                    client.ConfigureRequest = request =>
                    {
                        request.Headers.Cookie = cookies;
                        request.Headers.Origin = origin;
                    };
                    return await client.ConnectAsync(context.Uri, token);
                };
            })
            .Build();

        await using var foreign = Connect("https://evil.example");
        await Should.ThrowAsync<Exception>(() => foreign.StartAsync(ct));

        await using var own = Connect(TrimmeApiFactory.AllowedTestOrigin);
        await own.StartAsync(ct);
        own.State.ShouldBe(HubConnectionState.Connected);
    }
}
