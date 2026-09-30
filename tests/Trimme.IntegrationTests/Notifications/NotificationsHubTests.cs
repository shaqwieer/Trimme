using System.Net;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Web.Jobs;
using Trimme.BuildingBlocks.Web.Realtime;
using Trimme.IntegrationTests.Infrastructure;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Notifications;

/// <summary>
/// The in-app notification signal (R-NTF-10, R-TEN-06 realtime part, D-112): the server picks each connection's groups
/// (the user's own, and the shop's for an operable shop's users); a shop never hears another shop's notices; the customer
/// hears what the shop did; the signal carries nothing; anonymous callers and foreign origins are refused.
/// </summary>
public sealed class NotificationsHubTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private static DateOnly Target => TodayAt(DateTimeOffset.UtcNow).AddDays(2);

    private sealed class SignalClient : IAsyncDisposable
    {
        private readonly Channel<int> _signals = Channel.CreateUnbounded<int>();

        public SignalClient(TrimmeApiFactory factory, ApiSession session)
        {
            var cookies = session.Cookies.GetCookieHeader(ApiSession.Origin);
            Connection = new HubConnectionBuilder()
                .WithUrl(new Uri(factory.Server.BaseAddress, NotificationsHub.Path), options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    options.Headers.Add("Cookie", cookies);
                })
                .Build();

            // The signal has no arguments: nothing about the notice travels over the connection.
            Connection.On(NotificationsHub.EventName, () => _signals.Writer.TryWrite(1));
        }

        public HubConnection Connection { get; }

        public bool HasSignal => _signals.Reader.TryPeek(out _);

        public async Task NextAsync(CancellationToken ct) => await _signals.Reader.ReadAsync(ct).AsTask().WaitAsync(Wait, ct);

        public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
    }

    private static async Task<SignalClient> ConnectAsync(TrimmeApiFactory factory, ApiSession session, CancellationToken ct)
    {
        var client = new SignalClient(factory, session);
        await client.Connection.StartAsync(ct);
        return client;
    }

    private static async Task ProcessOutboxAsync(TrimmeApiFactory factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        for (var round = 0; round < 10 && await processor.ProcessBatchAsync(ct) > 0; round++)
        {
        }
    }

    [Fact]
    public async Task EachShop_HearsOnlyItsOwnNotices_TheCustomerHearsTheShop_AndNobodyChoosesAGroup()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "ntf_hub", ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        await using var shopA = await ConnectAsync(w.Factory, w.OwnerA, ct);
        await using var staffA = await ConnectAsync(w.Factory, w.StaffA, ct);
        await using var shopB = await ConnectAsync(w.Factory, w.OwnerB, ct);
        await using var customer = await ConnectAsync(w.Factory, noura, ct);

        // The customer books at shop A: both of A's users are signalled, B and the customer are not.
        var booking = await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 10), ct), ct, HttpStatusCode.Created);
        await ProcessOutboxAsync(w.Factory, ct);
        await shopA.NextAsync(ct);
        await staffA.NextAsync(ct);
        shopB.HasSignal.ShouldBeFalse();
        customer.HasSignal.ShouldBeFalse("the customer is not told about their own booking");

        // Shop A cancels: the customer's own group is signalled; A is not told about its own action; B hears nothing.
        var id = booking.GetProperty("id").GetGuid();
        var version = (await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/bookings/{id}", ct), ct)).GetProperty("booking").GetProperty("version").GetUInt32();
        await OkAsync(w.OwnerA.PostAsync($"/api/v1/shop/bookings/{id}/transitions", new { to = "CancelledByShop", reason = "إغلاق طارئ", version }, ct), ct);
        await ProcessOutboxAsync(w.Factory, ct);
        await customer.NextAsync(ct);
        shopA.HasSignal.ShouldBeFalse();
        shopB.HasSignal.ShouldBeFalse();

        // No group can be requested: the hub has no client-callable methods.
        await Should.ThrowAsync<HubException>(() => shopB.Connection.InvokeAsync("JoinGroup", $"shop:{w.Shops.A.ShopId}", ct));
    }

    [Fact]
    public async Task AnonymousCallers_AndForeignOrigins_AreRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "ntf_hub_refused", ct);
        var path = $"{NotificationsHub.Path}/negotiate?negotiateVersion=1";

        using var anonymous = ApiSession.Create(w.Factory);
        using (var negotiate = await anonymous.PostAsync(path, null, ct))
        {
            negotiate.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        foreach (var (origin, status) in new[] { ("https://evil.example", HttpStatusCode.Forbidden), (TrimmeApiFactory.AllowedTestOrigin, HttpStatusCode.OK) })
        {
            using var response = await w.OwnerA.SendAsync(HttpMethod.Post, path, null, ct, headers: new Dictionary<string, string> { ["Origin"] = origin });
            response.StatusCode.ShouldBe(status, origin);
        }
    }
}
