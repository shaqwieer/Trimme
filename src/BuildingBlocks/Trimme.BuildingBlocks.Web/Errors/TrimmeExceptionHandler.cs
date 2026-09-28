using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
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
            DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict, ApiErrorCodes.ConcurrencyConflict, "The resource was changed by someone else. Reload and try again.", null),
            // A unique index lost a race the handler's own check could not see (two concurrent creates): 409, not 500.
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                (StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "The resource already exists or was changed at the same time.", null),
            // An exclusion constraint (a booking overlapping another, R-BKG-03) that a handler did not translate itself.
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation } } =>
                (StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "The time is no longer available.", null),
            // A deadlock PostgreSQL broke between two conflicting writers (the loser of a race), wrapped or not.
            _ when exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected } }
                   || exception.InnerException is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected } } =>
                (StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "The resource was changed at the same time. Try again.", null),
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
