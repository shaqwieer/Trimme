using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Trimme.BuildingBlocks.Web.Errors;

namespace Trimme.BuildingBlocks.Web.Security;

public sealed class RequestLimitsOptions
{
    public const string SectionName = "RequestLimits";

    /// <summary>Default maximum request body for JSON endpoints. Upload endpoints opt into larger limits explicitly.</summary>
    public long MaxBodyBytes { get; set; } = 1_048_576;
}

/// <summary>
/// Rejects oversized bodies early with a 413 problem response, and lowers the server's streaming limit
/// for chunked requests. Kestrel's global limit is configured to the same value as a backstop.
/// </summary>
public sealed class RequestSizeLimitMiddleware(RequestDelegate next, IOptions<RequestLimitsOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var limit = options.Value.MaxBodyBytes;
        if (context.Request.ContentLength > limit)
        {
            await context.WriteProblemAsync(
                StatusCodes.Status413PayloadTooLarge,
                ApiErrorCodes.PayloadTooLarge,
                "The request body is too large.");
            return;
        }

        var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false } && (sizeFeature.MaxRequestBodySize is null || sizeFeature.MaxRequestBodySize > limit))
        {
            sizeFeature.MaxRequestBodySize = limit;
        }

        await next(context);
    }
}
