using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Trimme.BuildingBlocks.Web.Observability;

/// <summary>
/// Accepts a well-formed <c>X-Correlation-Id</c> from the caller (web app, Nginx) or generates one,
/// echoes it on the response and pushes it into the log context.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var incoming = context.Request.Headers[CorrelationId.HeaderName].ToString();
        var correlationId = IsValid(incoming) ? incoming : Guid.CreateVersion7().ToString("N");

        context.Items[CorrelationId.ItemKey] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationId.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static bool IsValid(string value) => !string.IsNullOrEmpty(value) && AllowedPattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._-]{8,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedPattern();
}

public static class CorrelationId
{
    public const string HeaderName = "X-Correlation-Id";
    internal const string ItemKey = "Trimme.CorrelationId";

    public static string Get(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(ItemKey, out var value) && value is string id ? id : context.TraceIdentifier;
    }
}
