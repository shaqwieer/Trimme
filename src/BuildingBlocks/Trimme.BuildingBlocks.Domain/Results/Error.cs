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

    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? fieldErrors = null)
        => new(code, message, ErrorKind.Validation, fieldErrors);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);

    public static Error BusinessRule(string code, string message) => new(code, message, ErrorKind.BusinessRule);
}
