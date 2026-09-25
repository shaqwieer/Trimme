using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Trimme.BuildingBlocks.Application.Validation;

namespace Trimme.BuildingBlocks.Web.Errors;

/// <summary>Maps unhandled exceptions to problem details without leaking internals.</summary>
internal sealed partial class TrimmeExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<TrimmeExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title, fieldErrors) = exception switch
        {
            RequestValidationException validation =>
                (StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationFailed, "One or more validation errors occurred.", validation.Errors),
            BadHttpRequestException badRequest =>
                (badRequest.StatusCode, ApiErrorCodes.ForStatus(badRequest.StatusCode), "The request is invalid.", null),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested =>
                (499, "request.cancelled", "The request was cancelled by the client.", null),
            _ => (StatusCodes.Status500InternalServerError, ApiErrorCodes.Unexpected, "An unexpected error occurred.", (IReadOnlyDictionary<string, string[]>?)null),
        };

        if (status >= 500)
        {
            LogUnhandled(logger, exception);
        }

        httpContext.Response.StatusCode = status;
        var context = new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                Extensions = { [ApiErrorCodes.ErrorCodeExtension] = code },
            },
        };

        if (fieldErrors is not null)
        {
            context.ProblemDetails.Extensions[ApiErrorCodes.FieldErrorsExtension] = fieldErrors;
        }

        return await problemDetailsService.TryWriteAsync(context);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception while processing the request")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
