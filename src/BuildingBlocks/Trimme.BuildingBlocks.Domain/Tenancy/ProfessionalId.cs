using Trimme.BuildingBlocks.Domain.Primitives;

namespace Trimme.BuildingBlocks.Domain.Tenancy;

/// <summary>
/// Identifier of a professional (barber/stylist). It lives here, next to <see cref="ShopId"/>, because other modules key
/// their shop-owned rows by it (service assignment, schedules, bookings) through composite <c>(shop_id, professional_id)</c>
/// foreign keys, which need the same key type on both sides.
/// </summary>
public readonly record struct ProfessionalId(Guid Value) : IEntityId<ProfessionalId>
{
    public static ProfessionalId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
