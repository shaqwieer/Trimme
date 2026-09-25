namespace Trimme.BuildingBlocks.Domain.Primitives;

/// <summary>Something that happened inside an aggregate. Dispatched after the aggregate is persisted.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
