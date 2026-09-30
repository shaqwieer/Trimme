namespace Trimme.BuildingBlocks.Application.Privacy;

/// <summary>
/// Protects personal data at rest (D-026): reversible encryption for values that must be read back
/// (for example a mobile number the OTP sender needs) and a keyed, deterministic hash for lookups and uniqueness.
/// Phase 04 introduces the port; Phase 05 finalises key management and the phone value object.
/// </summary>
public interface IPersonalDataProtector
{
    /// <summary>Encrypts <paramref name="plaintext"/>; <paramref name="purpose"/> isolates key material per data type.</summary>
    string Protect(string plaintext, string purpose);

    string Unprotect(string protectedValue, string purpose);

    /// <summary>Keyed HMAC-SHA256 (hex) of an already-normalized value. Stable across restarts and instances.</summary>
    string LookupHash(string normalizedValue, string purpose);
}

public static class PersonalDataPurposes
{
    public const string MobileNumber = "trimme.mobile-number";

    /// <summary>A professional's WhatsApp number (Phase 06); readable by authorized admin commands and the notification worker.</summary>
    public const string ProfessionalWhatsApp = "trimme.professional-whatsapp";
    public const string IpAddress = "trimme.ip-address";
    public const string OtpCode = "trimme.otp-code";

    /// <summary>A QR scan's visitor: the day and the IP address, hashed together so visits cannot be linked across days (D-114).</summary>
    public const string QrVisitor = "trimme.qr-visitor";
}
