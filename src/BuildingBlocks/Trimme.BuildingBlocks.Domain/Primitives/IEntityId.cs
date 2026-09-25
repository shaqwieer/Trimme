namespace Trimme.BuildingBlocks.Domain.Primitives;

/// <summary>
/// Marker for strongly typed identifiers backed by a <see cref="Guid"/> (UUIDv7).
/// Implementations are <c>readonly record struct</c>s with a single <c>Guid Value</c> constructor parameter,
/// which lets the persistence layer map them by convention.
/// </summary>
public interface IEntityId
{
    Guid Value { get; }
}

/// <summary>Strongly typed identifier with a factory, e.g. <c>public readonly record struct ShopId(Guid Value) : IEntityId&lt;ShopId&gt;</c>.</summary>
public interface IEntityId<TSelf> : IEntityId
    where TSelf : struct, IEntityId<TSelf>
{
    static abstract TSelf From(Guid value);
}

public static class EntityId
{
    /// <summary>Creates a new time-ordered (UUIDv7) identifier, which keeps B-tree indexes compact.</summary>
    public static TId New<TId>()
        where TId : struct, IEntityId<TId>
        => TId.From(Guid.CreateVersion7());

    public static TId Parse<TId>(string value)
        where TId : struct, IEntityId<TId>
        => TId.From(Guid.Parse(value));

    public static bool TryParse<TId>(string? value, out TId id)
        where TId : struct, IEntityId<TId>
    {
        if (Guid.TryParse(value, out var guid) && guid != Guid.Empty)
        {
            id = TId.From(guid);
            return true;
        }

        id = default;
        return false;
    }
}
