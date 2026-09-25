using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Shouldly;
using Trimme.IntegrationTests.Infrastructure;

namespace Trimme.IntegrationTests.Api;

/// <summary>
/// The committed OpenAPI document (<c>apps/api/openapi/v1.json</c>) is the contract the web client is generated from.
/// This test fails when the API surface changes without regenerating it (R-FND-05).
/// Regenerate with: <c>TRIMME_UPDATE_OPENAPI=1 dotnet test --project tests/Trimme.IntegrationTests</c>.
/// </summary>
public sealed class OpenApiContractTests(PostgresFixture postgres)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [Fact]
    public async Task OpenApi_document_matches_committed_contract()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new TrimmeApiFactory(await postgres.CreateDatabaseAsync("openapi", ct));
        using var client = factory.CreateClient();

        var live = Normalize(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), ct));
        var path = Path.Combine(RepositoryRoot(), "apps", "api", "openapi", "v1.json");

        if (Environment.GetEnvironmentVariable("TRIMME_UPDATE_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, live + "\n", ct);
        }

        File.Exists(path).ShouldBeTrue($"Missing {path}. Generate it with TRIMME_UPDATE_OPENAPI=1.");
        var committed = Normalize(await File.ReadAllTextAsync(path, ct));
        committed.ShouldBe(live, "The API contract changed. Regenerate apps/api/openapi/v1.json with TRIMME_UPDATE_OPENAPI=1 and commit it.");
    }

    [Fact]
    public async Task OpenApi_document_is_not_served_in_production()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ProductionFactory(await postgres.CreateDatabaseAsync("openapi_prod", ct));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), ct);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }

    private static string Normalize(string json) =>
        JsonNode.Parse(json)!.ToJsonString(Indented).ReplaceLineEndings("\n");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Trimme.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (Trimme.slnx) not found.");
    }

    private sealed class ProductionFactory(string connectionString) : TrimmeApiFactory(connectionString)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
        }
    }
}
