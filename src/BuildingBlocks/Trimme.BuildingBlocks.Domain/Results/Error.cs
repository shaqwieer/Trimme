namespace Trimme.BuildingBlocks.Domain.Results;

/// <summary>
/// A failure with a stable, machine-readable <see cref="Code"/> (e.g. <c>booking.slot_unavailable</c>).
/// Codes are part of the public API contract and must not change once released.
/// <see cref="Message"/> is a developer-facing English description; user-facing text is localized by clients from the code.
/// </summary>
public sealed record Error
{
    public Error(string code, string message, ErrorKind kind, IReadOnlyDictionary<string, string[]>? fieldErrors = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Message = message;
        Kind = kind;
        FieldErrors = fieldErrors;
    }

    public string Code { get; }

    public string Message { get; }

    public ErrorKind Kind { get; }

    /// <summary>Validation failures keyed by field name (camelCase), each with one or more error codes.</summary>
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; }

    /// <summary>
    /// Extra machine-readable values returned with the problem (for example <c>attemptsRemaining</c> or
    /// <c>retryAfterSeconds</c>). Never put personal data here.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Details { get; private init; }

    /// <summary>Returns a copy of this error with one more detail value.</summary>
    public Error WithDetail(string key, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var details = Details is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(Details, StringComparer.Ordinal);
        details[key] = value;
        return this with { Details = details };
    }

    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? fieldErrors = null)
        => new(code, message, ErrorKind.Validation, fieldErrors);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);

    public static Error BusinessRule(string code, string message) => new(code, message, ErrorKind.BusinessRule);

    public static Error RateLimited(string code, string message) => new(code, message, ErrorKind.RateLimited);

    public static Error Unavailable(string code, string message) => new(code, message, ErrorKind.Unavailable);
}
