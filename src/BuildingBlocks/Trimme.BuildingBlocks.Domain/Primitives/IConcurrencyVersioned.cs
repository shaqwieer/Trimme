namespace Trimme.BuildingBlocks.Domain.Primitives;

/// <summary>
/// Aggregates with optimistic concurrency. The persistence layer maps <see cref="Version"/>
/// to PostgreSQL's <c>xmin</c> system column; clients send it back (ETag / version field) on updates.
/// </summary>
public interface IConcurrencyVersioned
{
    uint Version { get; }
}
