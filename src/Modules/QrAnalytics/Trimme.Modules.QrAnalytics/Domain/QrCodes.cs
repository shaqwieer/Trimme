using System.Security.Cryptography;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.QrAnalytics.Domain;

public readonly record struct QrVisitId(Guid Value) : IEntityId<QrVisitId>
{
    public static QrVisitId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>What a code opens (c-qr print notes): the shop page, or one professional's page «for the chair mirror».</summary>
public enum QrTargetType
{
    Shop,
    Professional,
}

/// <summary>
/// A printed QR code of one shop (R-QR-01, D-114): a unique short code that opens the shop's or one of its professionals'
/// landing page. Shop-owned; created and switched on or off by platform admins only. A code is never deleted, so its
/// visits and the bookings it brought keep their meaning; a deactivated code stops resolving.
/// </summary>
public sealed class QrCodeLink : AggregateRoot<QrCodeLinkId>, IShopOwned, IConcurrencyVersioned
{
    public const int MaxLabelLength = 80;

    private QrCodeLink(
        QrCodeLinkId id, ShopId shopId, string code, QrTargetType targetType, ProfessionalId? professionalId, string? label, Guid? createdBy, DateTimeOffset now)
        : base(id)
    {
        ShopId = shopId;
        Code = code;
        TargetType = targetType;
        ProfessionalId = professionalId;
        Label = label;
        IsActive = true;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    private QrCodeLink()
    {
        Code = string.Empty;
    }

    public ShopId ShopId { get; private set; }

    /// <summary>The short code in the printed URL (<c>/q/{code}</c>), unique across the platform; never changes.</summary>
    public string Code { get; private set; }

    public QrTargetType TargetType { get; private set; }

    /// <summary>The professional a <see cref="QrTargetType.Professional"/> code opens; a professional of the same shop.</summary>
    public ProfessionalId? ProfessionalId { get; private set; }

    /// <summary>Where the code is used, for the admins (for example «واجهة المحل» or «مرآة الكرسي ٢»).</summary>
    public string? Label { get; private set; }

    public bool IsActive { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DeactivatedAt { get; private set; }

    public uint Version { get; private set; }

    public static Result<QrCodeLink> Create(
        QrCodeLinkId id, ShopId shopId, string code, ProfessionalId? professionalId, string? label, Guid? createdBy, DateTimeOffset now)
    {
        if (!QrCodeFormat.IsWellFormed(code))
        {
            throw new ArgumentException("The code is not well formed.", nameof(code));
        }

        var text = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        if (text is { Length: > MaxLabelLength })
        {
            return QrErrors.LabelTooLong();
        }

        return new QrCodeLink(
            id, shopId, code, professionalId is null ? QrTargetType.Shop : QrTargetType.Professional, professionalId, text, createdBy, now);
    }

    public Result Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return QrErrors.AlreadyInactive();
        }

        IsActive = false;
        DeactivatedAt = now;
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsActive)
        {
            return QrErrors.AlreadyActive();
        }

        IsActive = true;
        DeactivatedAt = null;
        return Result.Success();
    }
}

/// <summary>
/// The code → shop map that lets an anonymous scan find the shop before any shop-owned row is read (D-114): the public data
/// scope shows shop-owned rows of one known shop only. Ids only; written with the link in the same unit of work.
/// </summary>
public sealed class QrCodeRoute
{
    private QrCodeRoute(string code, ShopId shopId, QrCodeLinkId linkId)
    {
        Code = code;
        ShopId = shopId;
        LinkId = linkId;
    }

    private QrCodeRoute()
    {
        Code = string.Empty;
    }

    public string Code { get; private set; }

    public ShopId ShopId { get; private set; }

    public QrCodeLinkId LinkId { get; private set; }

    public static QrCodeRoute For(QrCodeLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return new QrCodeRoute(link.Code, link.ShopId, link.Id);
    }
}

/// <summary>The kind of device that scanned, from the user agent; nothing more of the user agent is kept.</summary>
public enum QrDeviceClass
{
    Mobile,
    Tablet,
    Desktop,
    Bot,
    Other,
}

/// <summary>
/// One scan of a code (R-QR-02, D-114): the time, the device class, the page language and a per-day visitor hash (a keyed
/// hash of the day and the IP address). No raw IP address, no full user agent, no cookie beyond the visit id, no
/// third-party tracking. A platform log keyed by the code: written by the anonymous visit endpoint, read by the admin
/// analytics, and by a shop only through its own codes.
/// </summary>
public sealed class QrVisit : Entity<QrVisitId>
{
    public const int VisitorHashLength = 32;

    private QrVisit(QrVisitId id, QrCodeLinkId linkId, ShopId shopId, DateTimeOffset visitedAt, string visitorHash, QrDeviceClass device, string locale)
        : base(id)
    {
        LinkId = linkId;
        ShopId = shopId;
        VisitedAt = visitedAt;
        VisitorHash = visitorHash;
        Device = device;
        Locale = locale;
    }

    private QrVisit()
    {
        VisitorHash = Locale = string.Empty;
    }

    public QrCodeLinkId LinkId { get; private set; }

    public ShopId ShopId { get; private set; }

    public DateTimeOffset VisitedAt { get; private set; }

    /// <summary>Hex; the same visitor gives the same value on one day only.</summary>
    public string VisitorHash { get; private set; }

    public QrDeviceClass Device { get; private set; }

    /// <summary><c>ar</c> or <c>en</c>: the landing page's language.</summary>
    public string Locale { get; private set; }

    public static QrVisit Record(QrVisitId id, QrCodeLink link, DateTimeOffset now, string visitorHash, QrDeviceClass device, string? locale)
    {
        ArgumentNullException.ThrowIfNull(link);
        return new QrVisit(id, link.Id, link.ShopId, now, visitorHash, device, locale == "en" ? "en" : "ar");
    }

    /// <summary>A visit restored by the development seed.</summary>
    public static QrVisit Seeded(QrVisitId id, QrCodeLinkId linkId, ShopId shopId, DateTimeOffset at, string visitorHash, QrDeviceClass device, string locale) =>
        new(id, linkId, shopId, at, visitorHash, device, locale);
}

/// <summary>The short code: 8 characters of a lowercase alphabet without look-alikes (no 0/o, 1/l/i).</summary>
public static class QrCodeFormat
{
    public const int Length = 8;
    public const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    public static string NewCode()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>Codes are matched lowercase, so a code typed in capitals still opens.</summary>
    public static string? Normalize(string? code)
    {
        var text = code?.Trim().ToLowerInvariant();
        return IsWellFormed(text) ? text : null;
    }

    public static bool IsWellFormed(string? code) => code is { Length: Length } && code.All(c => Alphabet.Contains(c, StringComparison.Ordinal));
}

public static class QrDevices
{
    /// <summary>A coarse class from the user agent (bots first, then tablets before phones, as iPads and Android tablets say «Mobile» rarely).</summary>
    public static QrDeviceClass Classify(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return QrDeviceClass.Other;
        }

        bool Has(string token) => userAgent.Contains(token, StringComparison.OrdinalIgnoreCase);
        if (Has("bot") || Has("crawler") || Has("spider") || Has("preview") || Has("headless"))
        {
            return QrDeviceClass.Bot;
        }

        if (Has("ipad") || Has("tablet") || (Has("android") && !Has("mobile")))
        {
            return QrDeviceClass.Tablet;
        }

        if (Has("mobi") || Has("iphone") || Has("android"))
        {
            return QrDeviceClass.Mobile;
        }

        return Has("windows") || Has("macintosh") || Has("x11") || Has("cros") ? QrDeviceClass.Desktop : QrDeviceClass.Other;
    }
}

public static class QrErrors
{
    public static Error NotFound() => Error.NotFound("qr.not_found", "The QR code was not found.");

    public static Error AlreadyActive() => Error.Conflict("qr.already_active", "The QR code is already active.");

    public static Error AlreadyInactive() => Error.Conflict("qr.already_inactive", "The QR code is already inactive.");

    public static Error ShopNotFound() => Error.NotFound("shop.not_found", "The shop was not found.");

    public static Error ProfessionalNotFound() => Error.NotFound("professional.not_found", "The professional was not found.");

    public static Error InvalidFormat() =>
        Error.Validation("validation.failed", "The format must be png, svg or pdf.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["format"] = ["validation.invalid"] });

    public static Error InvalidPeriod() =>
        Error.Validation("validation.failed", "The period must start before it ends and span at most a year.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["to"] = ["validation.out_of_range"] });

    public static Error LabelTooLong() =>
        Error.Validation("validation.failed", "The label is too long.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["label"] = ["validation.too_long"] });
}
