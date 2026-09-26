using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Application.Privacy;

namespace Trimme.BuildingBlocks.Web.Privacy;

public sealed class PersonalDataOptions
{
    public const string SectionName = "PersonalData";

    /// <summary>
    /// Base64 key (at least 32 bytes) for lookup hashes. Required outside Development and Testing, where a fixed,
    /// publicly known development key is used instead. Rotating it invalidates every stored lookup hash.
    /// </summary>
    public string? LookupKey { get; set; }
}

/// <summary>
/// Data Protection encryption plus a keyed HMAC-SHA256 for lookups (D-026). The HMAC key is never derived from
/// Data Protection because lookups must stay stable when the key ring rotates.
/// </summary>
internal sealed class PersonalDataProtector : IPersonalDataProtector
{
    /// <summary>
    /// Publicly known development key, derived from a fixed phrase. Only used when no key is configured, which startup
    /// validation allows only in Development and Testing.
    /// </summary>
    private static readonly byte[] DevelopmentLookupKey =
        SHA256.HashData(Encoding.UTF8.GetBytes("TRIMME development lookup key (public, never used in production)"));

    private readonly IDataProtectionProvider _provider;
    private readonly byte[] _lookupKey;

    public PersonalDataProtector(IDataProtectionProvider provider, IOptions<PersonalDataOptions> options)
    {
        _provider = provider;
        _lookupKey = options.Value.LookupKey is { Length: > 0 } configured
            ? Convert.FromBase64String(configured)
            : DevelopmentLookupKey;
        if (_lookupKey.Length < 32)
        {
            throw new InvalidOperationException($"{PersonalDataOptions.SectionName}:LookupKey must be at least 32 bytes.");
        }
    }

    public string Protect(string plaintext, string purpose)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return _provider.CreateProtector(purpose).Protect(plaintext);
    }

    public string Unprotect(string protectedValue, string purpose)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        return _provider.CreateProtector(purpose).Unprotect(protectedValue);
    }

    public string LookupHash(string normalizedValue, string purpose)
    {
        ArgumentNullException.ThrowIfNull(normalizedValue);
        ArgumentNullException.ThrowIfNull(purpose);

        var hash = HMACSHA256.HashData(_lookupKey, Encoding.UTF8.GetBytes($"{purpose}\n{normalizedValue}"));
        return Convert.ToHexStringLower(hash);
    }
}
