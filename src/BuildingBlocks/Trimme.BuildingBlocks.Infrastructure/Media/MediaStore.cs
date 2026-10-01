using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Media;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Domain.Media;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;

namespace Trimme.BuildingBlocks.Infrastructure.Media;

/// <summary>
/// An image stored in PostgreSQL (<c>media.media_files</c>, D-064). It carries no shop id: ownership comes from the
/// aggregate that references it, and only that aggregate's commands create or delete it. The bytes are immutable.
/// Queries must project the metadata they need; loading the entity loads the bytes.
/// </summary>
public sealed class StoredMedia
{
    public StoredMedia(MediaId id, MediaPurpose purpose, string contentType, int width, int height, byte[] content, string sha256, DateTimeOffset createdAt, Guid? createdBy)
    {
        Id = id;
        Purpose = purpose;
        ContentType = contentType;
        Width = width;
        Height = height;
        Content = content;
        SizeBytes = content.Length;
        Sha256 = sha256;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    /// <summary>A key-only instance, used to delete without loading the bytes.</summary>
    internal StoredMedia(MediaId id)
    {
        Id = id;
        ContentType = Sha256 = string.Empty;
        Content = [];
    }

    public MediaId Id { get; private set; }

    public MediaPurpose Purpose { get; private set; }

    public string ContentType { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public long SizeBytes { get; private set; }

    /// <summary>Hex SHA-256 of <see cref="Content"/>; the HTTP entity tag.</summary>
    public string Sha256 { get; private set; }

    public byte[] Content { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }
}

internal static class StoredMediaModel
{
    public const string Schema = "media";

    public static void Configure(EntityTypeBuilder<StoredMedia> media)
    {
        media.ToTable("media_files", Schema);
        media.HasKey(m => m.Id);
        media.Property(m => m.Purpose).HasConversion<string>().HasMaxLength(30);
        media.Property(m => m.ContentType).HasMaxLength(40);
        media.Property(m => m.Sha256).HasMaxLength(64).IsFixedLength();
        media.Property(m => m.Content).HasColumnType("bytea");
    }
}

internal sealed class MediaStore(TrimmeDbContext db, TimeProvider clock, ICurrentUser user) : IMediaStore
{
    public Result<StoredImage> AddImage(ReadOnlySpan<byte> content, MediaPurpose purpose)
    {
        if (content.IsEmpty)
        {
            return MediaErrors.Missing();
        }

        if (content.Length > MediaRules.MaxImageBytes)
        {
            return MediaErrors.TooLarge();
        }

        // The type and size come from the file's own headers, before anything is decoded (D-119).
        if (ImageSanitizer.TrySanitize(content) is not { } header)
        {
            return MediaErrors.UnsupportedType();
        }

        if (header.Width > MediaRules.MaxDimension || header.Height > MediaRules.MaxDimension
            || (long)header.Width * header.Height > MediaRules.MaxPixels)
        {
            return MediaErrors.TooLarge();
        }

        var minimum = MediaRules.MinDimension(purpose);
        if (header.Width < minimum || header.Height < minimum)
        {
            return MediaErrors.TooSmall();
        }

        // Only pixels are stored: a fresh file encoded from the decoded image.
        if (ImageReencoder.TryReencode(content, header) is not { } image)
        {
            return MediaErrors.UnsupportedType();
        }

        var stored = new StoredMedia(
            EntityId.New<MediaId>(),
            purpose,
            image.ContentType,
            image.Width,
            image.Height,
            image.Bytes,
            Convert.ToHexStringLower(SHA256.HashData(image.Bytes)),
            clock.GetUtcNow(),
            user.UserId);
        db.Add(stored);
        return new StoredImage(stored.Id, stored.ContentType, stored.Width, stored.Height, stored.SizeBytes);
    }

    public void Remove(MediaId id)
    {
        var tracked = db.ChangeTracker.Entries<StoredMedia>().FirstOrDefault(e => e.Entity.Id == id);
        if (tracked is not null)
        {
            tracked.State = tracked.State == EntityState.Added ? EntityState.Detached : EntityState.Deleted;
            return;
        }

        db.Remove(new StoredMedia(id));
    }
}

public static class MediaServiceCollectionExtensions
{
    public static IServiceCollection AddTrimmeMedia(this IServiceCollection services)
    {
        services.AddScoped<IMediaStore, MediaStore>();
        return services;
    }
}
