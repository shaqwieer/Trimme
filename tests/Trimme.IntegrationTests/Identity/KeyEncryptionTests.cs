using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Infrastructure;

namespace Trimme.IntegrationTests.Identity;

/// <summary>
/// D-120 (Phase 17): the Data Protection key ring is stored in the database wrapped with a certificate that is not, and
/// rotating that certificate keeps older data readable while the previous certificate is still configured.
/// </summary>
public sealed class KeyEncryptionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Keys_AreWrappedAtRest_AndACertificateRotationKeepsOldDataReadable()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await postgres.CreateDatabaseAsync("dp_keys", ct);
        var first = TestCertificates.Create("first");
        var second = TestCertificates.Create("second");
        const string Purpose = "trimme.test.key-encryption";

        string protectedValue;
        await using (var before = await StartAsync(database, CertificateSettings(first), ct))
        {
            protectedValue = before.Services.GetRequiredService<IPersonalDataProtector>().Protect("+966512345678", Purpose);
            await using var scope = before.Services.CreateAsyncScope();
            var keys = await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<DataProtectionKeyRecord>().AsNoTracking().ToListAsync(ct);
            keys.ShouldNotBeEmpty();
            keys.ShouldAllBe(k => k.Xml.Contains("encryptedSecret") && k.Xml.Contains("X509Data") && !k.Xml.Contains("<value>"),
                "every stored key is wrapped; the raw key material never reaches the database");
        }

        // Rotation: the new certificate wraps new keys, the old one is kept to unwrap the existing key.
        var rotated = CertificateSettings(second);
        rotated["DataProtection:PreviousCertificates:0:Path"] = first.Path;
        rotated["DataProtection:PreviousCertificates:0:Password"] = first.Password;
        await using (var after = await StartAsync(database, rotated, ct))
        {
            after.Services.GetRequiredService<IPersonalDataProtector>().Unprotect(protectedValue, Purpose).ShouldBe("+966512345678");
        }

        // Without the certificate that wrapped the key, the database copy alone reads nothing.
        await using var withoutOld = await StartAsync(database, CertificateSettings(second), ct);
        Should.Throw<CryptographicException>(() => withoutOld.Services.GetRequiredService<IPersonalDataProtector>().Unprotect(protectedValue, Purpose));
    }

    private static Dictionary<string, string?> CertificateSettings(TestCertificates.File certificate) => new()
    {
        ["DataProtection:CertificatePath"] = certificate.Path,
        ["DataProtection:CertificatePassword"] = certificate.Password,
    };

    private static async Task<TrimmeApiFactory> StartAsync(string database, Dictionary<string, string?> settings, CancellationToken ct)
    {
        var factory = new TrimmeApiFactory(database, settings);
        await factory.MigrateAsync(ct);
        return factory;
    }
}

/// <summary>Self-signed certificates written to temporary PKCS#12 files, as a deployment would mount them.</summary>
internal static class TestCertificates
{
    public sealed record File(string Path, string Password);

    public static File Create(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN=trimme-test-{name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"trimme-dp-{name}-{Guid.NewGuid():N}.pfx");
        System.IO.File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return new File(path, password);
    }
}
