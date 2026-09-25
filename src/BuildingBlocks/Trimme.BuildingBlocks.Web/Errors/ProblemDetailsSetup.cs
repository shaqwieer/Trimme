using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Web.Observability;

namespace Trimme.BuildingBlocks.Web.Errors;

public static class ProblemDetailsSetup
{
    /// <summary>
    /// RFC 7807 responses for every error: a stable <c>errorCode</c> and the request <c>correlationId</c>
    /// are always present; exception details are never included (they are logged instead).
    /// </summary>
    public static IServiceCollection AddTrimmeProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            var status = problem.Status ?? context.HttpContext.Response.StatusCode;

            problem.Extensions.TryAdd(ApiErrorCodes.ErrorCodeExtension, ApiErrorCodes.ForStatus(status));
            problem.Extensions[ApiErrorCodes.CorrelationIdExtension] = CorrelationId.Get(context.HttpContext);
            problem.Extensions.Remove("traceId");
            problem.Instance ??= context.HttpContext.Request.Path;
        });

        services.AddExceptionHandler<TrimmeExceptionHandler>();
        return services;
    }

    public static IApplicationBuilder UseTrimmeProblemDetails(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }

    /// <summary>Writes a problem details response with an explicit error code.</summary>
    public static async Task WriteProblemAsync(
        this HttpContext httpContext,
        int statusCode,
        string errorCode,
        string title,
        string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.StatusCode = statusCode;
        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
                Extensions = { [ApiErrorCodes.ErrorCodeExtension] = errorCode },
            },
        });
    }
}
