using Shouldly;
using Trimme.BuildingBlocks.Domain.Primitives;

namespace Trimme.UnitTests.BuildingBlocks.Domain;

public sealed class EntityIdTests
{
    [Fact]
    public void New_creates_non_empty_version7_identifier()
    {
        var id = EntityId.New<SampleId>();

        id.Value.ShouldNotBe(Guid.Empty);
        id.Value.Version.ShouldBe(7);
    }

    [Fact]
    public void New_identifiers_are_time_ordered()
    {
        var first = EntityId.New<SampleId>();
        Thread.Sleep(2);
        var second = EntityId.New<SampleId>();

        string.CompareOrdinal(first.Value.ToString(), second.Value.ToString()).ShouldBeLessThan(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void TryParse_rejects_invalid_or_empty_values(string? value)
    {
        EntityId.TryParse<SampleId>(value, out var id).ShouldBeFalse();
        id.ShouldBe(default);
    }

    [Fact]
    public void Parse_round_trips()
    {
        var original = EntityId.New<SampleId>();

        EntityId.Parse<SampleId>(original.Value.ToString()).ShouldBe(original);
    }

    [Fact]
    public void Entity_rejects_empty_identifier()
    {
        Should.Throw<ArgumentException>(() => new SampleAggregate(default));
    }

    [Fact]
    public void AggregateRoot_collects_and_clears_domain_events()
    {
        var aggregate = new SampleAggregate(EntityId.New<SampleId>());

        aggregate.DoSomething();
        aggregate.DoSomething();

        aggregate.DomainEvents.Count.ShouldBe(2);
        aggregate.ClearDomainEvents();
        aggregate.DomainEvents.ShouldBeEmpty();
    }
}

internal readonly record struct SampleId(Guid Value) : IEntityId<SampleId>
{
    public static SampleId From(Guid value) => new(value);
}

internal sealed record SampleHappened(DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed class SampleAggregate(SampleId id) : AggregateRoot<SampleId>(id)
{
    public void DoSomething() => Raise(new SampleHappened(DateTimeOffset.UtcNow));
}
