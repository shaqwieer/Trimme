using System.Net;
using Microsoft.AspNetCore.Hosting;
using Shouldly;
using Trimme.IntegrationTests.Infrastructure;

namespace Trimme.IntegrationTests.Identity;

/// <summary>Development conveniences (dev OTP inbox, dev lookup key, no SMTP) can never reach production.</summary>
public sealed class ProductionStartupTests(PostgresFixture postgres)
{
    private static readonly Dictionary<string, string?> ProductionSecrets = new()
    {
        ["PersonalData:LookupKey"] = Convert.ToBase64String(new byte[32]),
        ["Email:Smtp:Host"] = "smtp.invalid",
    };

    [Theory]
    [InlineData("PersonalData:LookupKey")]
    [InlineData("Email:Smtp:Host")]
    public async Task Production_RefusesToStart_WithoutRequiredSecret(string missing)
    {
        var ct = TestContext.Current.CancellationToken;
        var settings = new Dictionary<string, string?>(ProductionSecrets) { [missing] = null };
        await using var factory = new ProductionFactory(await postgres.CreateDatabaseAsync("prod_secrets", ct), settings);

        Should.Throw<Exception>(() => factory.CreateClient()).ToString().ShouldContain(missing);
    }

    [Fact]
    public async Task Production_RefusesToStart_WithTheDevOtpInbox()
    {
        var ct = TestContext.Current.CancellationToken;
        var settings = new Dictionary<string, string?>(ProductionSecrets) { ["Identity:Otp:Sender"] = "DevInbox" };
        await using var factory = new ProductionFactory(await postgres.CreateDatabaseAsync("prod_inbox", ct), settings);

        Should.Throw<Exception>(() => factory.CreateClient()).ToString().ShouldContain("Identity:Otp:Sender=DevInbox");
    }

    [Fact]
    public async Task Production_HasNoDevInbox_AndReportsUndeliverableCodes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ProductionFactory(await postgres.CreateDatabaseAsync("prod_otp", ct), ProductionSecrets);
        await factory.MigrateAsync(ct);
        using var session = ApiSession.Create(factory);

        using var inbox = await session.GetAsync($"/api/v1/dev/otp-inbox/latest?phone={Uri.EscapeDataString(IdentityTestData.NewPhone())}", ct);
        inbox.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var request = await session.PostAsync("/api/v1/auth/otp/request", new { phone = IdentityTestData.NewPhone(), termsAccepted = true }, ct);
        request.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await request.ErrorCodeAsync(ct)).ShouldBe("otp.delivery_unavailable");
    }

    private sealed class ProductionFactory(string connectionString, IReadOnlyDictionary<string, string?> settings)
        : TrimmeApiFactory(connectionString, settings)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
        }
    }
}
