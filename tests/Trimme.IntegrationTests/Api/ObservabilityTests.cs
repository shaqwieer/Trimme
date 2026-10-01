using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Shouldly;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Identity.Domain;

namespace Trimme.IntegrationTests.Api;

/// <summary>Traces and health of background work (D-118, Phase 17).</summary>
public sealed class ObservabilityTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Requests_AreTracedWithTheirDatabaseCommands_AndNoSpanCarriesAPhoneNumber()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new SpanSink();
        await using var factory = new TrimmeApiFactory(await postgres.CreateDatabaseAsync("obs_traces", ct))
        {
            ConfigureTestServices = services => services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(sink)),
        };
        await factory.MigrateAsync(ct);

        var phone = IdentityTestData.NewPhone();
        using var customer = await IdentityTestData.SignInCustomerAsync(factory, phone, ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        using (var search = await admin.GetAsync($"/api/v1/admin/customers?search={Uri.EscapeDataString(phone)}", ct))
        {
            search.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var failed = await customer.PostAsync("/api/v1/auth/otp/request", new { phone = phone + "x", termsAccepted = true, locale = "ar" }, ct))
        {
            failed.IsSuccessStatusCode.ShouldBeFalse();
        }

        // Every test host in this process listens to the same sources, so other tests' spans may be here too.
        var captured = sink.Spans.ToArray();
        var databaseTraces = captured.Where(s => s.Source.Name == "Npgsql").Select(s => s.TraceId).ToHashSet();
        captured.ShouldContain(
            s => s.Kind == ActivityKind.Server && Route(s).EndsWith("/admin/customers", StringComparison.Ordinal) && databaseTraces.Contains(s.TraceId),
            "a request span with its EF Core commands as Npgsql child spans");
        captured.ShouldContain(s => s.Kind == ActivityKind.Server && Route(s).EndsWith("/auth/otp/verify", StringComparison.Ordinal));

        // Not vacuous: the search's query string is recorded, with its value redacted by the instrumentation.
        captured.ShouldContain(
            s => s.Kind == ActivityKind.Server && Route(s).EndsWith("/admin/customers", StringComparison.Ordinal)
                 && Equals(s.GetTagItem("url.query"), "?search=Redacted"),
            "the admin search span records its query, with the value redacted");

        // The number, with or without its country code, appears in no span name, tag, event or status of any request.
        var local = phone[4..];
        foreach (var span in captured)
        {
            var text = JsonSerializer.Serialize(new
            {
                span.DisplayName,
                span.StatusDescription,
                Tags = span.TagObjects.Select(t => $"{t.Key}={t.Value}").ToArray(),
                Events = span.Events.Select(e => e.Name + string.Join(",", e.Tags.Select(t => $"{t.Key}={t.Value}"))).ToArray(),
            });
            text.ShouldNotContain(local, customMessage: $"span {span.DisplayName} ({span.Source.Name}) leaks the phone number");
        }
    }

    [Fact]
    public async Task Readiness_ReportsJobsAndOutboxLag_AsDegradedWithoutFailing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await TrimmeApiFactory.CreateMigratedAsync(postgres, "obs_health", ct);
        using var client = factory.CreateClient();

        var healthy = await ReadyAsync(client, ct);
        healthy.Status.ShouldBe("Healthy");
        healthy.Checks.ShouldContainKeyAndValue("jobs", "Healthy");
        healthy.Checks.ShouldContainKeyAndValue("outbox", "Healthy");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
            db.Add(OutboxMessage.Create("test.stalled", new { }, DateTimeOffset.UtcNow.AddHours(-1)));
            await db.SaveChangesAsync(ct);
        }

        var behind = await ReadyAsync(client, ct);
        behind.Status.ShouldBe("Degraded", "an undelivered message an hour old is an alert, not an outage");
        behind.Checks.ShouldContainKeyAndValue("outbox", "Degraded");
        behind.Checks.ShouldContainKeyAndValue("database", "Healthy");
    }

    private static string Route(Activity span) => (span.GetTagItem("http.route") as string ?? string.Empty).TrimEnd('/');

    /// <summary>Collects finished spans; thread-safe because every host in the process reports to every provider.</summary>
    private sealed class SpanSink : BaseProcessor<Activity>
    {
        public System.Collections.Concurrent.ConcurrentQueue<Activity> Spans { get; } = new();

        public override void OnEnd(Activity data) => Spans.Enqueue(data);
    }

    private static async Task<(string Status, Dictionary<string, string> Checks)> ReadyAsync(HttpClient client, CancellationToken ct)
    {
        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var checks = json.RootElement.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("status").GetString()!);
        return (json.RootElement.GetProperty("status").GetString()!, checks);
    }
}
