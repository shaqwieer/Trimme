using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Trimme.Api.Hosting;
using Trimme.IntegrationTests.Infrastructure;

namespace Trimme.IntegrationTests.Api;

public sealed class PlatformEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TrimmeApiFactory _factory = null!;

    public async ValueTask InitializeAsync() =>
        _factory = await TrimmeApiFactory.CreateMigratedAsync(postgres, "platform", TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Health_Live_Returns200()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_Ready_ChecksDatabase()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("status").GetString().ShouldBe("Healthy");
        body.RootElement.GetProperty("checks").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString())
            .ShouldContain("database");
    }

    [Fact]
    public async Task Health_Ready_Returns503_WhenDatabaseIsUnreachable()
    {
        await using var broken = new TrimmeApiFactory("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2");
        using var client = broken.CreateClient();

        using var liveResponse = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
        using var readyResponse = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        liveResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        readyResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await readyResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("Password");
    }

    [Fact]
    public async Task Meta_endpoint_is_versioned_under_api_v1()
    {
        using var client = _factory.CreateClient();

        var meta = await client.GetFromJsonAsync<ApiMetaResponse>(new Uri("/api/v1/meta", UriKind.Relative), TestContext.Current.CancellationToken);

        meta.ShouldNotBeNull();
        meta.Name.ShouldBe("TRIMME API");
        meta.Version.ShouldBe("v1");
        meta.ServerTimeUtc.Offset.ShouldBe(TimeSpan.Zero);
    }
}
