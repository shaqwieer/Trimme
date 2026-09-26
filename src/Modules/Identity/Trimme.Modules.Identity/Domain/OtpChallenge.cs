using Trimme.BuildingBlocks.Domain.Primitives;

namespace Trimme.Modules.Identity.Domain;

public readonly record struct OtpChallengeId(Guid Value) : IEntityId<OtpChallengeId>
{
    public static OtpChallengeId From(Guid value) => new(value);
}

/// <summary>
/// A one-time code sent to a mobile number for passwordless customer sign-in (D-005, D-037). The code and the number
/// are never stored in clear: the code as a keyed hash, the number encrypted plus a lookup hash.
/// </summary>
public sealed class OtpChallenge : AggregateRoot<OtpChallengeId>
{
    public OtpChallenge(
        OtpChallengeId id,
        string phoneHash,
        string protectedPhone,
        string codeHash,
        DateTimeOffset now,
        OtpPolicy policy,
        bool termsAccepted,
        string locale,
        string? ipHash)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(policy);

        PhoneHash = phoneHash;
        ProtectedPhone = protectedPhone;
        CodeHash = codeHash;
        CreatedAt = now;
        ExpiresAt = now + policy.CodeLifetime;
        ResendAvailableAt = now + policy.ResendCooldown;
        MaxAttempts = policy.MaxAttemptsPerCode;
        TermsAccepted = termsAccepted;
        Locale = locale;
        IpHash = ipHash;
    }

    private OtpChallenge()
    {
        PhoneHash = ProtectedPhone = CodeHash = Locale = string.Empty;
    }

    public string PhoneHash { get; private set; }

    public string ProtectedPhone { get; private set; }

    public string CodeHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset ResendAvailableAt { get; private set; }

    public int FailedAttempts { get; private set; }

    public int MaxAttempts { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>Set when a newer code is requested for the same number.</summary>
    public DateTimeOffset? SupersededAt { get; private set; }

    public bool TermsAccepted { get; private set; }

    public string Locale { get; private set; }

    public string? IpHash { get; private set; }

    public int AttemptsRemaining => Math.Max(0, MaxAttempts - FailedAttempts);

    public bool IsOpen(DateTimeOffset now) => ConsumedAt is null && SupersededAt is null && ExpiresAt > now && AttemptsRemaining > 0;

    public void Supersede(DateTimeOffset now) => SupersededAt ??= now;
}

/// <summary>OTP limits (D-037). Defaults: 6 digits, 5-minute expiry, 3 attempts per code, 30 s resend cooldown, 5 codes per hour per number.</summary>
public sealed class OtpPolicy
{
    public const int CodeLength = 6;

    public TimeSpan CodeLifetime { get; init; } = TimeSpan.FromMinutes(5);

    public int MaxAttemptsPerCode { get; init; } = 3;

    public TimeSpan ResendCooldown { get; init; } = TimeSpan.FromSeconds(30);

    public int MaxCodesPerNumber { get; init; } = 5;

    public TimeSpan CodeRequestWindow { get; init; } = TimeSpan.FromHours(1);
}
