using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

[assembly: AssemblyFixture(typeof(Trimme.IntegrationTests.Infrastructure.MailpitFixture))]

namespace Trimme.IntegrationTests.Infrastructure;

/// <summary>
/// One Mailpit container per test run: the same SMTP sink the local compose stack uses (spec Phase 04 item 4.11).
/// Tests address mail to unique recipients, so they can run in parallel without clearing the inbox.
/// </summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    public const string Image = "axllent/mailpit:v1.31.2";
    private const int SmtpPort = 1025;
    private const int HttpPort = 8025;

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithPortBinding(SmtpPort, assignRandomHostPort: true)
        .WithPortBinding(HttpPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(HttpPort).ForPath("/api/v1/messages")))
        .Build();

    private HttpClient? _api;

    private HttpClient Api => _api ?? throw new InvalidOperationException("Mailpit is not started.");

    public string SmtpHost => _container.Hostname;

    public int SmtpMappedPort => _container.GetMappedPublicPort(SmtpPort);

    /// <summary>Settings that point the API's SMTP sender at this container.</summary>
    public IReadOnlyDictionary<string, string?> SmtpSettings => new Dictionary<string, string?>
    {
        ["Email:Smtp:Host"] = SmtpHost,
        ["Email:Smtp:Port"] = SmtpMappedPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Email:Smtp:Security"] = "None",
        ["Web:PublicBaseUrl"] = "https://app.trimme.test",
    };

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _api = new HttpClient { BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(HttpPort)}") };
    }

    public async ValueTask DisposeAsync()
    {
        _api?.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>Waits for the newest message addressed to <paramref name="recipient"/> and returns it with its text body.</summary>
    public async Task<MailpitMessage> WaitForMessageAsync(string recipient, CancellationToken cancellationToken, int expectedCount = 1)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var list = await Api.GetFromJsonAsync<MessageList>(
                $"/api/v1/search?query={Uri.EscapeDataString($"to:\"{recipient}\"")}", cancellationToken);
            if (list is { Messages.Count: > 0 } && list.Messages.Count >= expectedCount)
            {
                var newest = list.Messages[0];
                var full = await Api.GetFromJsonAsync<MessageDetail>($"/api/v1/message/{newest.Id}", cancellationToken);
                return new MailpitMessage(newest.Subject, full!.Text, full.Html);
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException($"No email reached Mailpit for {recipient}.");
    }

    public async Task<int> CountMessagesAsync(string recipient, CancellationToken cancellationToken)
    {
        var list = await Api.GetFromJsonAsync<MessageList>(
            $"/api/v1/search?query={Uri.EscapeDataString($"to:\"{recipient}\"")}", cancellationToken);
        return list?.Messages.Count ?? 0;
    }

    private sealed record MessageList([property: JsonPropertyName("messages")] List<MessageSummary> Messages);

    private sealed record MessageSummary(
        [property: JsonPropertyName("ID")] string Id,
        [property: JsonPropertyName("Subject")] string Subject);

    private sealed record MessageDetail(
        [property: JsonPropertyName("Text")] string Text,
        [property: JsonPropertyName("HTML")] string Html);
}

public sealed record MailpitMessage(string Subject, string Text, string Html);
