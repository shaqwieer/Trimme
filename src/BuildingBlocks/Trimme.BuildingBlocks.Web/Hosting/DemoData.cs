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

/// <summary>
/// Demo customers (spec §20) with fake Saudi mobiles in the <c>+966 50 010 03xx</c> range; they sign in with the
/// development OTP inbox. Their bookings are seeded by the Bookings module.
/// </summary>
public static class DemoCustomers
{
    public static readonly DemoCustomer Noura = new(Guid.Parse("0199a0de-5a10-7000-8000-000000000901"), "نورة السبيعي", "+966500100301");

    public static readonly DemoCustomer Khalid = new(Guid.Parse("0199a0de-5a10-7000-8000-000000000902"), "خالد الدوسري", "+966500100302");

    public static IReadOnlyList<DemoCustomer> All { get; } = [Noura, Khalid];
}

public sealed record DemoCustomer(Guid Id, string Name, string Mobile);

/// <summary>
/// Completed demo visits and the review each customer left (spec §20, D-092). The Bookings seeder records the completed
/// booking, then the Reviews seeder adds the review and the rating aggregates. Dates are relative to the seeding day.
/// Faisal has none: the schedule E2E asserts his exact free slots.
/// </summary>
public static class DemoVisits
{
    public static IReadOnlyList<DemoVisit> All { get; } =
    [
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000a01"), DemoData.AlAsala.Id, DemoCustomers.Noura,
            Guid.Parse("0199a0de-5a10-7000-8000-000000000103"), Guid.Parse("0199a0de-5a10-7000-8000-000000000401"), 7, 17, 0,
            4, "الحجز وفّر علي الانتظار، دخلت وجلست على الكرسي مباشرة."),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000a21"), DemoData.AlAsala.Id, DemoCustomers.Khalid,
            Guid.Parse("0199a0de-5a10-7000-8000-000000000102"), Guid.Parse("0199a0de-5a10-7000-8000-000000000402"), 12, 19, 0,
            5, "التزام دقيق بالموعد، وتهذيب اللحية نظيف جداً."),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000a22"), DemoData.AlAsala.Id, DemoCustomers.Noura,
            Guid.Parse("0199a0de-5a10-7000-8000-000000000103"), Guid.Parse("0199a0de-5a10-7000-8000-000000000403"), 20, 17, 30,
            5, "حجزت لابني، والحلاق صبور والمكان مرتب."),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000a23"), DemoData.AlAsala.Id, DemoCustomers.Khalid,
            Guid.Parse("0199a0de-5a10-7000-8000-000000000102"), Guid.Parse("0199a0de-5a10-7000-8000-000000000404"), 15, 18, 0,
            5, "جلسة عناية بالوجه مريحة، والمكان نظيف ومرتب."),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000a24"), DemoData.BarberHouse.Id, DemoCustomers.Khalid,
            Guid.Parse("0199a0de-5a10-7000-8000-000000000202"), Guid.Parse("0199a0de-5a10-7000-8000-000000000411"), 9, 20, 0,
            4, "خدمة ممتازة والأسعار واضحة، لا توجد مفاجآت."),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000a25"), DemoData.BarberHouse.Id, DemoCustomers.Noura,
            Guid.Parse("0199a0de-5a10-7000-8000-000000000201"), Guid.Parse("0199a0de-5a10-7000-8000-000000000413"), 25, 16, 0,
            5, "تعامل رائع مع الأطفال، سنعود بإذن الله."),
    ];
}

/// <summary>A completed booking <c>DaysAgo</c> local days before seeding, and its review's stars (1–5) and comment.</summary>
public sealed record DemoVisit(
    Guid BookingId,
    ShopId ShopId,
    DemoCustomer Customer,
    Guid ProfessionalId,
    Guid ServiceId,
    int DaysAgo,
    int Hour,
    int Minute,
    int Rating,
    string Comment);

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
