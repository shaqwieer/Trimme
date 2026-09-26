using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Identity.Domain;

public readonly record struct InvitationId(Guid Value) : IEntityId<InvitationId>
{
    public static InvitationId From(Guid value) => new(value);
}

/// <summary>
/// An emailed invitation to create a staff account (spec §9: shop and admin accounts are created or invited by the
/// platform admin; nobody self-registers as staff). Only the token's hash is stored. Shop invitations (with a
/// <see cref="ShopId"/>) are issued from Phase 05, when shops exist.
/// </summary>
public sealed class Invitation : AggregateRoot<InvitationId>, ITenantMember
{
    public Invitation(
        InvitationId id,
        string email,
        string normalizedEmail,
        UserType userType,
        string roleName,
        ShopId? shopId,
        string tokenHash,
        string locale,
        Guid invitedByUserId,
        DateTimeOffset now,
        TimeSpan lifetime)
        : base(id)
    {
        Email = email;
        NormalizedEmail = normalizedEmail;
        UserType = userType;
        RoleName = roleName;
        ShopId = shopId;
        TokenHash = tokenHash;
        Locale = locale;
        InvitedByUserId = invitedByUserId;
        CreatedAt = now;
        ExpiresAt = now + lifetime;
    }

    private Invitation()
    {
        Email = NormalizedEmail = RoleName = TokenHash = Locale = string.Empty;
    }

    public string Email { get; private set; }

    public string NormalizedEmail { get; private set; }

    public UserType UserType { get; private set; }

    public string RoleName { get; private set; }

    public ShopId? ShopId { get; private set; }

    public string TokenHash { get; private set; }

    public string Locale { get; private set; }

    public Guid InvitedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public Guid? AcceptedUserId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsPending(DateTimeOffset now) => AcceptedAt is null && RevokedAt is null && ExpiresAt > now;

    public void Accept(Guid userId, DateTimeOffset now)
    {
        AcceptedAt = now;
        AcceptedUserId = userId;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
