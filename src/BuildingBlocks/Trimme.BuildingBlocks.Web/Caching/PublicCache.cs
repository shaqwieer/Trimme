using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Trimme.BuildingBlocks.Infrastructure.Persistence;

namespace Trimme.BuildingBlocks.Web.Caching;

/// <summary>
/// The public response cache (spec §21, D-093): anonymous public reads (shop pages, catalogue, reviews, sitemap data)
/// are cached in memory for <see cref="Expiry"/> and evicted as a whole after any save that changes public content
/// (<see cref="Domain.Primitives.IPublicContent"/>). Only anonymous requests are cached (the framework's default policy
/// skips authenticated requests and responses that set cookies), and public handlers answer the same for every caller,
/// so nothing authorization-sensitive is ever shared. The cache is per API instance: another instance can serve a page
/// up to <see cref="Expiry"/> old.
/// </summary>
public static class PublicCache
{
    public const string PolicyName = "public";
    public const string Tag = "public";

    public static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    /// <summary>
    /// What browsers and shared caches may keep (Phase 17, D-121): short, because the eviction on save (D-093) cannot reach
    /// them. Anonymous successful responses only; anything else keeps the API's <c>no-store</c>.
    /// </summary>
    public const string HttpCacheControl = "public, max-age=60, stale-while-revalidate=60";

    public static IServiceCollection AddTrimmePublicCache(this IServiceCollection services)
    {
        services.AddOutputCache(options => options.AddPolicy(
            PolicyName, policy => policy.Expire(Expiry).Tag(Tag).SetVaryByQuery("*")));
        services.AddSingleton<IPublicContentChangeSink, OutputCacheEviction>();
        return services;
    }

    /// <summary>
    /// Caches an anonymous public GET endpoint's successful responses (D-093); browsers and shared caches may keep them
    /// briefly (<see cref="HttpCacheControl"/>, set by the security headers middleware, also on a cache hit).
    /// </summary>
    public static TBuilder CachePublicly<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.CacheOutput(PolicyName).WithMetadata(PublicReadMarker.Instance);

    /// <summary>
    /// True for an anonymous, successful answer of a <see cref="CachePublicly"/> endpoint that sets no cookie. Routing has
    /// chosen the endpoint even when the output cache answers, so a replayed response is recognised too.
    /// </summary>
    public static bool MayBeKeptByHttpCaches(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Response.StatusCode == StatusCodes.Status200OK
            && context.User.Identity?.IsAuthenticated != true
            && !context.Response.Headers.ContainsKey(HeaderNames.SetCookie)
            && context.GetEndpoint()?.Metadata.GetMetadata<PublicReadMarker>() is not null;
    }

    /// <summary>Marks <see cref="CachePublicly"/> endpoints (the output cache's own metadata is not public).</summary>
    private sealed class PublicReadMarker
    {
        public static readonly PublicReadMarker Instance = new();
    }

    private sealed class OutputCacheEviction(IOutputCacheStore store) : IPublicContentChangeSink
    {
        public Task PublicContentChangedAsync(CancellationToken cancellationToken) =>
            store.EvictByTagAsync(Tag, cancellationToken).AsTask();
    }
}

