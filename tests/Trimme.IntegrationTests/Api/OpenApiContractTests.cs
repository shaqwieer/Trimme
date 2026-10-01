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

    /// <summary>R-NEG-02: v1 takes no payments — no payment, checkout or card endpoint, and bookings are never "paid".</summary>
    [Fact]
    public async Task OpenApi_has_no_payment_surface()
    {
        var path = Path.Combine(RepositoryRoot(), "apps", "api", "openapi", "v1.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;

        var paths = document["paths"]!.AsObject().Select(p => p.Key).ToList();
        paths.ShouldNotContain(p => p.Contains("payment", StringComparison.OrdinalIgnoreCase)
                                    || p.Contains("checkout", StringComparison.OrdinalIgnoreCase)
                                    || p.Contains("card", StringComparison.OrdinalIgnoreCase));
        var statuses = document["components"]!["schemas"]!["PaymentStatus"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>());
        statuses.ShouldBe(["NotApplicable"]);
    }

    /// <summary>R-NEG-03: no bulk export of bookings or customer data (no export, CSV or download operation).</summary>
    [Fact]
    public async Task OpenApi_has_no_export_surface()
    {
        var path = Path.Combine(RepositoryRoot(), "apps", "api", "openapi", "v1.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;

        var paths = document["paths"]!.AsObject().Select(p => p.Key).ToList();
        paths.ShouldNotContain(p => p.Contains("export", StringComparison.OrdinalIgnoreCase)
                                    || p.Contains("csv", StringComparison.OrdinalIgnoreCase)
                                    || p.Contains("download", StringComparison.OrdinalIgnoreCase)
                                    || p.Contains("xlsx", StringComparison.OrdinalIgnoreCase));
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

            // Production refuses to start without these (see ProductionStartupTests).
            builder.UseSetting("PersonalData:LookupKey", Convert.ToBase64String(new byte[32]));
            builder.UseSetting("Email:Smtp:Host", "smtp.invalid");
            builder.UseSetting("DataProtection:CertificatePath", KeyCertificate.Path);
            builder.UseSetting("DataProtection:CertificatePassword", KeyCertificate.Password);
        }

        private static readonly Identity.TestCertificates.File KeyCertificate = Identity.TestCertificates.Create("openapi");
    }
}
