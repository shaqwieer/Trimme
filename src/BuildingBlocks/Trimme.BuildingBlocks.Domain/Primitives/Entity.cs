namespace Trimme.BuildingBlocks.Domain.Primitives;

public abstract class Entity<TId>
    where TId : struct, IEntityId
{
    protected Entity(TId id)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", nameof(id));
        }

        Id = id;
    }

    /// <summary>Required by EF Core materialization.</summary>
    protected Entity()
    {
    }

    public TId Id { get; private init; }
}
