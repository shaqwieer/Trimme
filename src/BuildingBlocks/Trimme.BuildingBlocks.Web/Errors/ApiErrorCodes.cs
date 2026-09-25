namespace Trimme.BuildingBlocks.Web.Errors;

/// <summary>
/// Stable, transport-level error codes placed in the <c>errorCode</c> extension of every
/// RFC 7807 problem details response. Feature modules define their own domain codes (e.g. <c>booking.slot_unavailable</c>).
/// These values are public API contract: never rename a released code.
/// </summary>
public static class ApiErrorCodes
{
    public const string BadRequest = "request.invalid";
    public const string ValidationFailed = "validation.failed";
    public const string Unauthenticated = "auth.unauthenticated";
    public const string Forbidden = "auth.forbidden";
    public const string NotFound = "resource.not_found";
    public const string MethodNotAllowed = "http.method_not_allowed";
    public const string Conflict = "resource.conflict";
    public const string PayloadTooLarge = "request.too_large";
    public const string UnsupportedMediaType = "request.unsupported_media_type";
    public const string RateLimited = "rate_limit.exceeded";
    public const string Unexpected = "server.unexpected";

    public const string ErrorCodeExtension = "errorCode";
    public const string CorrelationIdExtension = "correlationId";
    public const string FieldErrorsExtension = "errors";

    public static string ForStatus(int statusCode) => statusCode switch
    {
        400 => BadRequest,
        401 => Unauthenticated,
        403 => Forbidden,
        404 => NotFound,
        405 => MethodNotAllowed,
        409 => Conflict,
        413 => PayloadTooLarge,
        415 => UnsupportedMediaType,
        429 => RateLimited,
        _ when statusCode >= 500 => Unexpected,
        _ => BadRequest,
    };
}
