namespace Trimme.BuildingBlocks.Application.Validation;

/// <summary>
/// Raised by the validation behaviour when a request fails FluentValidation rules.
/// The API maps it to a 400 problem details response with code <see cref="ErrorCode"/>.
/// </summary>
public sealed class RequestValidationException : Exception
{
    public const string ErrorCode = "validation.failed";

    public RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public RequestValidationException()
        : this(new Dictionary<string, string[]>())
    {
    }

    public RequestValidationException(string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]>();
    }

    public RequestValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = new Dictionary<string, string[]>();
    }

    /// <summary>Field name (camelCase) mapped to stable error codes.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
