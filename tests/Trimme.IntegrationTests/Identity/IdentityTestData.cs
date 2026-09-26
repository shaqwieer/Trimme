using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Identity.Infrastructure.Otp;
using Trimme.Modules.Identity.Infrastructure.Persistence;

namespace Trimme.IntegrationTests.Identity;

/// <summary>Builders for signed-in customers and staff used across the identity tests.</summary>
internal static class IdentityTestData
{
    public const string StaffPassword = "correct horse battery";

    /// <summary>A unique, valid Saudi mobile in E.164.</summary>
    public static string NewPhone() => "+9665" + RandomNumberGenerator.GetInt32(10_000_000, 100_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string NewEmail(string prefix = "staff") => $"{prefix}.{Guid.NewGuid():N}@trimme.test";

    /// <summary>A migrated API whose clock is <paramref name="clock"/> (so tests can move time forward).</summary>
    public static async Task<TrimmeApiFactory> CreateFactoryAsync(
        PostgresFixture postgres,
        string prefix,
        CancellationToken cancellationToken,
        FakeTimeProvider? clock = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var factory = new TrimmeApiFactory(await postgres.CreateDatabaseAsync(prefix, cancellationToken), settings)
        {
            ConfigureTestServices = services =>
            {
                if (clock is not null)
                {
                    services.AddSingleton<TimeProvider>(clock);
                }
            },
        };
        await factory.MigrateAsync(cancellationToken);
        return factory;
    }

    public static string LatestOtp(TrimmeApiFactory factory, string phone) =>
        factory.Services.GetRequiredService<DevOtpInbox>().Latest(phone)?.Code
        ?? throw new InvalidOperationException("No OTP was sent to this number.");

    public static async Task<Guid> RequestOtpAsync(ApiSession session, string phone, CancellationToken cancellationToken, bool termsAccepted = true)
    {
        using var response = await session.PostAsync("/api/v1/auth/otp/request", new { phone, termsAccepted, locale = "ar" }, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(cancellationToken));
        return (await response.JsonAsync(cancellationToken)).GetProperty("challengeId").GetGuid();
    }

    /// <summary>Signs a customer in through the real OTP flow (creating the account the first time).</summary>
    public static async Task<ApiSession> SignInCustomerAsync(TrimmeApiFactory factory, string phone, CancellationToken cancellationToken)
    {
        var session = ApiSession.Create(factory);
        var challengeId = await RequestOtpAsync(session, phone, cancellationToken);
        using var verify = await session.PostAsync(
            "/api/v1/auth/otp/verify", new { challengeId, code = LatestOtp(factory, phone) }, cancellationToken);
        verify.StatusCode.ShouldBe(HttpStatusCode.OK, await verify.Content.ReadAsStringAsync(cancellationToken));
        return session;
    }

    public static async Task<Guid> CreateStaffAsync(TrimmeApiFactory factory, string email, string role, CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var definition = SystemRoles.Find(role) ?? throw new ArgumentException($"Unknown role {role}", nameof(role));
        var id = Guid.CreateVersion7();
        var user = new ApplicationUser
        {
            Id = id,
            UserName = $"staff-{id:N}",
            Email = email,
            EmailConfirmed = true,
            DisplayName = "موظف اختبار",
            UserType = definition.UserType,
            CreatedAt = DateTimeOffset.UtcNow,
            LockoutEnabled = true,
        };
        (await users.CreateAsync(user, StaffPassword)).Succeeded.ShouldBeTrue();
        (await users.AddToRoleAsync(user, role)).Succeeded.ShouldBeTrue();
        cancellationToken.ThrowIfCancellationRequested();
        return id;
    }

    public static async Task<ApiSession> SignInStaffAsync(TrimmeApiFactory factory, string email, CancellationToken cancellationToken, string password = StaffPassword)
    {
        var session = ApiSession.Create(factory);
        using var response = await session.PostAsync("/api/v1/auth/staff/sign-in", new { email, password }, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        return session;
    }

    public static async Task<ApiSession> SignInNewStaffAsync(TrimmeApiFactory factory, string role, CancellationToken cancellationToken)
    {
        var email = NewEmail(role.ToLowerInvariant());
        await CreateStaffAsync(factory, email, role, cancellationToken);
        return await SignInStaffAsync(factory, email, cancellationToken);
    }
}
