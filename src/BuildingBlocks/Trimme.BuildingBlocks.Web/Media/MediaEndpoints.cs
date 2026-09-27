using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Trimme.BuildingBlocks.Application.Media;
using Trimme.BuildingBlocks.Domain.Media;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Media;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Errors;

namespace Trimme.BuildingBlocks.Web.Media;

/// <summary>Serves images stored in the database (D-064).</summary>
public static class MediaEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/media/{mediaId:guid}", Get)
            .AllowAnonymous()
            .WithTags("Media")
            .WithName("GetMedia")
            .WithSummary("A stored image (JPEG, PNG or WebP). Public and immutable: a new upload always gets a new id.")
            .Produces(StatusCodes.Status200OK, contentType: ImageSanitizer.Jpeg, additionalContentTypes: [ImageSanitizer.Png, ImageSanitizer.WebP])
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Get(Guid mediaId, HttpContext http, TrimmeDbContext db, CancellationToken cancellationToken)
    {
        var id = new MediaId(mediaId);
        var media = await db.Set<StoredMedia>().AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new { m.ContentType, m.Sha256, m.Content })
            .SingleOrDefaultAsync(cancellationToken);
        if (media is null)
        {
            return Error.NotFound("media.not_found", "The image was not found.").ToProblem();
        }

        http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return TypedResults.File(media.Content, media.ContentType, entityTag: new EntityTagHeaderValue($"\"{media.Sha256}\""));
    }
}

/// <summary>Shared conventions for endpoints that receive one image as <c>multipart/form-data</c> (field <c>file</c>).</summary>
public static class ImageUpload
{
    public const string FieldName = "file";

    /// <summary>
    /// Raises the body limit for this endpoint only, documents the multipart body and turns off ASP.NET antiforgery
    /// (the API's own double-submit CSRF check, D-053, still applies to every unsafe request).
    /// </summary>
    public static RouteHandlerBuilder AcceptsImageUpload(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MediaRules.MaxUploadRequestBytes))
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);
    }

    /// <summary>The uploaded bytes; the media store then validates what they are.</summary>
    public static async Task<Result<byte[]>> ReadAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return MediaErrors.Missing();
        }

        if (file.Length > MediaRules.MaxImageBytes)
        {
            return MediaErrors.TooLarge();
        }

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
