using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Trimme.BuildingBlocks.Web.Security;

/// <summary>
/// Encryption at rest of the Data Protection key ring (D-120). Bound from <c>DataProtection</c>. The keys live in the
/// same database as the data they protect (customer and professional numbers, D-026), so a database copy alone must not be
/// enough to read them: each key is wrapped with a certificate that is mounted into the API containers and never stored in
/// the database. Required outside Development and Testing.
/// </summary>
public sealed class KeyEncryptionOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>Path of the PKCS#12 (.pfx) file whose certificate wraps new keys.</summary>
    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    /// <summary>Certificates replaced by a rotation, kept only to unwrap keys written before it.</summary>
    public List<PreviousCertificate> PreviousCertificates { get; set; } = [];

    public sealed class PreviousCertificate
    {
        public string? Path { get; set; }

        public string? Password { get; set; }
    }
}

public static class KeyEncryptionSetup
{
    /// <summary>
    /// Wraps new keys with the configured certificate and unwraps with it or any previous one. The certificate files are
    /// read here, during registration, and only when a path is configured: Development (where EF tooling builds the host)
    /// configures none.
    /// </summary>
    public static IDataProtectionBuilder ProtectTrimmeKeys(this IDataProtectionBuilder builder, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var required = !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
        builder.Services.AddOptions<KeyEncryptionOptions>()
            .Bind(configuration.GetSection(KeyEncryptionOptions.SectionName))
            .Validate(
                options => !required || !string.IsNullOrWhiteSpace(options.CertificatePath),
                $"{KeyEncryptionOptions.SectionName}:CertificatePath must be configured outside Development and Testing.")
            .ValidateOnStart();

        var options = configuration.GetSection(KeyEncryptionOptions.SectionName).Get<KeyEncryptionOptions>() ?? new KeyEncryptionOptions();
        if (string.IsNullOrWhiteSpace(options.CertificatePath))
        {
            return builder;
        }

        var current = Load(options.CertificatePath, options.CertificatePassword);
        var previous = options.PreviousCertificates.Where(p => !string.IsNullOrWhiteSpace(p.Path)).Select(p => Load(p.Path!, p.Password));
        return builder.ProtectKeysWithCertificate(current).UnprotectKeysWithAnyCertificate([current, .. previous]);
    }

    private static X509Certificate2 Load(string path, string? password) =>
        X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);
}
