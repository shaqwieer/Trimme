using Microsoft.AspNetCore.Http;
using Trimme.BuildingBlocks.Domain.Results;

namespace Trimme.BuildingBlocks.Web.Errors;

/// <summary>Converts domain <see cref="Result"/>s into HTTP responses for minimal API endpoints.</summary>
public static class ResultHttpExtensions
{
    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.BusinessRule => StatusCodes.Status422UnprocessableEntity,
            ErrorKind.RateLimited => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status500InternalServerError,
        };

        var extensions = new Dictionary<string, object?> { [ApiErrorCodes.ErrorCodeExtension] = error.Code };
        if (error.FieldErrors is not null)
        {
            extensions[ApiErrorCodes.FieldErrorsExtension] = error.FieldErrors;
        }

        return TypedResults.Problem(title: error.Message, statusCode: status, extensions: extensions);
    }

    public static IResult ToHttpResult(this Result result, Func<IResult>? onSuccess = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? onSuccess?.Invoke() ?? TypedResults.NoContent() : result.Error.ToProblem();
    }

    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? onSuccess?.Invoke(result.Value) ?? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}
