namespace Trimme.Modules.Identity.Application;

/// <summary>
/// Field error codes. Their last segment maps onto the web app's <c>validation.*</c> message keys
/// (<c>validation.email_invalid</c> → <c>emailInvalid</c>), see <c>apps/web/src/lib/forms/problem.ts</c>.
/// </summary>
internal static class ValidationCodes
{
    public const string Required = "validation.required";
    public const string Invalid = "validation.invalid";
    public const string TooLong = "validation.too_long";
    public const string EmailInvalid = "validation.email_invalid";
    public const string OtpIncomplete = "validation.otp_incomplete";
    public const string TermsRequired = "validation.terms_required";
    public const string PasswordTooShort = "validation.password_too_short";
    public const string PasswordTooWeak = "validation.password_too_weak";
}

internal static class Locales
{
    public const string Arabic = "ar";
    public const string English = "en";

    public static bool IsSupported(string? locale) => locale is Arabic or English;
}
