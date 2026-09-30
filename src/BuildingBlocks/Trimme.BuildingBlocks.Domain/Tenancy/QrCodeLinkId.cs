using Trimme.BuildingBlocks.Domain.Primitives;

namespace Trimme.BuildingBlocks.Domain.Tenancy;

/// <summary>
/// Identifier of a printed QR code (D-114). It lives here, like <see cref="ProfessionalId"/>, because a booking references
/// the code it is credited to through the composite <c>(shop_id, qr_link_id)</c> foreign key, which needs the same key type
/// on both sides.
/// </summary>
public readonly record struct QrCodeLinkId(Guid Value) : IEntityId<QrCodeLinkId>
{
    public static QrCodeLinkId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
