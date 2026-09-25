namespace Trimme.BuildingBlocks.Domain.Primitives;

/// <summary>Creation/modification instants (UTC), stamped by the persistence layer.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }

    DateTimeOffset? UpdatedAt { get; }
}
