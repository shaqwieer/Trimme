using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
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
/// An endpoint that needs a larger body (e.g. image uploads) opts in with
/// <c>.WithMetadata(new RequestSizeLimitAttribute(bytes))</c>; the override applies to that endpoint only.
/// </summary>
public sealed class RequestSizeLimitMiddleware(RequestDelegate next, IOptions<RequestLimitsOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var limit = ResolveLimit(context, options.Value.MaxBodyBytes);
        if (context.Request.ContentLength > limit)
        {
            await context.WriteProblemAsync(
                StatusCodes.Status413PayloadTooLarge,
                ApiErrorCodes.PayloadTooLarge,
                "The request body is too large.");
            return;
        }

        var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = limit;
        }

        await next(context);
    }

    /// <summary>
    /// The endpoint's explicit limit when it declares one (routing runs before this middleware), otherwise the default.
    /// A declared "unlimited" (null) is not honoured: every endpoint keeps a finite ceiling.
    /// </summary>
    private static long ResolveLimit(HttpContext context, long defaultLimit)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>();
        return metadata?.MaxRequestBodySize is { } endpointLimit and > 0 ? endpointLimit : defaultLimit;
    }
}
