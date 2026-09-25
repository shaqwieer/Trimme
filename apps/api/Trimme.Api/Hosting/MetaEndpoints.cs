namespace Trimme.Api.Hosting;

/// <summary>Platform metadata used by clients to check connectivity and clock skew.</summary>
internal static class MetaEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/meta", GetMeta)
            .WithName("GetApiMeta")
            .WithTags("Meta")
            .WithSummary("API name, version and current server time (UTC).");
    }

    private static Task<ApiMetaResponse> GetMeta(TimeProvider clock, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ApiMetaResponse("TRIMME API", "v1", clock.GetUtcNow()));
    }
}

public sealed record ApiMetaResponse(string Name, string Version, DateTimeOffset ServerTimeUtc);
