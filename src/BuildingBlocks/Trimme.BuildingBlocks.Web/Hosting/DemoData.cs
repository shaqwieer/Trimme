using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.BuildingBlocks.Web.Hosting;

/// <summary>
/// Fixed identifiers of the development demo data (spec §20), shared by the modules' seeders so the data is
/// deterministic across runs and machines. Development only: seeders run only through <c>seed --dev</c>.
/// </summary>
public static class DemoData
{
    public const string PasswordVariable = "TRIMME_DEMO_PASSWORD";
    public const string DefaultPassword = "trimme local demo";

    public static readonly DemoShop AlAsala = new(
        new ShopId(Guid.Parse("0199a0de-5a10-7000-8000-000000000001")),
        "al-asala",
        "صالون الأصالة للحلاقة",
        "Al Asala Barbershop",
        "owner@al-asala.trimme.local",
        "staff@al-asala.trimme.local");

    public static readonly DemoShop BarberHouse = new(
        new ShopId(Guid.Parse("0199a0de-5a10-7000-8000-000000000002")),
        "barber-house",
        "باربر هاوس",
        "Barber House",
        "owner@barber-house.trimme.local",
        "staff@barber-house.trimme.local");

    public static IReadOnlyList<DemoShop> Shops { get; } = [AlAsala, BarberHouse];

    /// <summary>
    /// Two more active shops without users or catalogue, seeded only so every subscription status has an example
    /// (spec §20): Lamsat Al Rajul's subscription has expired and Al Madina's is suspended.
    /// </summary>
    public static readonly DemoExtraShop LamsatAlRajul = new(
        new ShopId(Guid.Parse("0199a0de-5a10-7000-8000-000000000003")), "lamsat-al-rajul", "لمسة الرجل", "Lamsat Al Rajul", 24.8375, 46.6620, "النرجس");

    public static readonly DemoExtraShop AlMadina = new(
        new ShopId(Guid.Parse("0199a0de-5a10-7000-8000-000000000004")), "al-madina", "حلاقة المدينة", "Al Madina Barbers", 24.6300, 46.6720, "السويدي");

    public static IReadOnlyList<DemoExtraShop> ExtraShops { get; } = [LamsatAlRajul, AlMadina];

    /// <summary>
    /// Separate professionals per shop (spec §20). The WhatsApp numbers are fake, valid Saudi mobiles in the
    /// <c>+966 50 010 01xx</c> range; seeding never sends messages.
    /// </summary>
    public static IReadOnlyList<DemoProfessional> Professionals { get; } =
    [
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000101"), AlAsala.Id, "faisal", "فيصل القحطاني", "Faisal Al-Qahtani", "تدريج وفيد", "Fades", "+966500100101"),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000102"), AlAsala.Id, "sultan", "سلطان الحربي", "Sultan Al-Harbi", "لحية وعناية", "Beard care", "+966500100102"),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000103"), AlAsala.Id, "rakan", "راكان المطيري", "Rakan Al-Mutairi", "حلاقة كلاسيك", "Classic cuts", "+966500100103"),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000201"), BarberHouse.Id, "omar", "عمر السالم", "Omar Al-Salem", "حلاقة أطفال", "Kids' cuts", "+966500100201"),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000202"), BarberHouse.Id, "majed", "ماجد العتيبي", "Majed Al-Otaibi", "حلاقة كلاسيك", "Classic cuts", "+966500100202"),
    ];
}

public sealed record DemoProfessional(
    Guid Id,
    ShopId ShopId,
    string Slug,
    string NameAr,
    string NameEn,
    string SpecialtyAr,
    string SpecialtyEn,
    string WhatsApp);

public sealed record DemoShop(ShopId Id, string Slug, string NameAr, string NameEn, string OwnerEmail, string StaffEmail);

public sealed record DemoExtraShop(ShopId Id, string Slug, string NameAr, string NameEn, double Latitude, double Longitude, string District);
