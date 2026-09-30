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

    /// <summary>The review E2E's customer (E7): completed, unreviewed visits only (<see cref="DemoReviewableVisits"/>).</summary>
    public static readonly DemoCustomer Sara = new(Guid.Parse("0199a0de-5a10-7000-8000-000000000903"), "سارة العنزي", "+966500100303");

    public static IReadOnlyList<DemoCustomer> All { get; } = [Noura, Khalid, Sara];
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

/// <summary>
/// Sara's completed, unreviewed visits at Barber House with Majed (D-097): each review E2E run reviews one. They are
/// dated one to four days before seeding, so on a database seeded more than a week ago (the review window) none is
/// reviewable any more; reset the volume. Her one upcoming booking (<see cref="UpcomingBookingId"/>) cannot be reviewed.
/// </summary>
public static class DemoReviewableVisits
{
    public static readonly Guid ProfessionalId = Guid.Parse("0199a0de-5a10-7000-8000-000000000202");

    public static readonly Guid ServiceId = Guid.Parse("0199a0de-5a10-7000-8000-000000000411");

    public static readonly Guid UpcomingBookingId = Guid.Parse("0199a0de-5a10-7000-8000-000000000b11");

    /// <summary>Booking id, days before seeding, local start hour.</summary>
    public static IReadOnlyList<(Guid BookingId, int DaysAgo, int Hour)> All { get; } =
    [
        .. Enumerable.Range(0, 8).Select(i => (Guid.Parse($"0199a0de-5a10-7000-8000-000000000b{i + 1:00}"), 1 + (i / 2), i % 2 == 0 ? 12 : 15)),
    ];
}

/// <summary>
/// Demo QR codes (spec §20, D-114): a shop code and barber codes for both demo shops, and one switched-off code. The QR
/// seeder adds their scans over the 27 days before seeding; three demo bookings are credited to a scan (the Bookings
/// seeder sets the credit, the QR seeder the scan it came from). Faisal has no code (his slots are asserted exactly).
/// </summary>
public static class DemoQr
{
    public static readonly DemoQrCode AlAsalaShop = new(Guid.Parse("0199a0de-5a10-7000-8000-00000000c001"), DemoData.AlAsala.Id, "aswn7qkd", null, "واجهة المحل", true);

    public static readonly DemoQrCode AlAsalaSultan = new(
        Guid.Parse("0199a0de-5a10-7000-8000-00000000c002"), DemoData.AlAsala.Id, "assu2tnm", Guid.Parse("0199a0de-5a10-7000-8000-000000000102"), "مرآة سلطان", true);

    public static readonly DemoQrCode AlAsalaRakan = new(
        Guid.Parse("0199a0de-5a10-7000-8000-00000000c003"), DemoData.AlAsala.Id, "asrk4npx", Guid.Parse("0199a0de-5a10-7000-8000-000000000103"), "مرآة راكان", true);

    public static readonly DemoQrCode BarberHouseShop = new(Guid.Parse("0199a0de-5a10-7000-8000-00000000c004"), DemoData.BarberHouse.Id, "bhsh5mzc", null, "الكاونتر", true);

    public static readonly DemoQrCode BarberHouseOmar = new(
        Guid.Parse("0199a0de-5a10-7000-8000-00000000c005"), DemoData.BarberHouse.Id, "bhwm6twy", Guid.Parse("0199a0de-5a10-7000-8000-000000000201"), "مرآة عمر", true);

    /// <summary>A retired window sticker: it no longer resolves, and its old scans still count in the analytics.</summary>
    public static readonly DemoQrCode BarberHouseRetired = new(Guid.Parse("0199a0de-5a10-7000-8000-00000000c006"), DemoData.BarberHouse.Id, "bhxx8dfg", null, "ملصق قديم", false);

    public static IReadOnlyList<DemoQrCode> Codes { get; } = [AlAsalaShop, AlAsalaSultan, AlAsalaRakan, BarberHouseShop, BarberHouseOmar, BarberHouseRetired];

    /// <summary>
    /// Demo bookings credited to a scan: the booking, the code, the scan's id and when the scan happened (local day before
    /// seeding and hour), always before the booking was made and within the attribution window.
    /// </summary>
    public static IReadOnlyList<DemoQrBooking> Bookings { get; } =
    [
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000a01"), AlAsalaRakan.Id, Guid.Parse("0199a0de-5a10-7000-8000-00000000cf01"), 9, 14),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000a24"), BarberHouseShop.Id, Guid.Parse("0199a0de-5a10-7000-8000-00000000cf02"), 11, 16),
        new(Guid.Parse("0199a0de-5a10-7000-8000-000000000a11"), BarberHouseOmar.Id, Guid.Parse("0199a0de-5a10-7000-8000-00000000cf03"), 1, 18),
    ];
}

public sealed record DemoQrCode(Guid Id, ShopId ShopId, string Code, Guid? ProfessionalId, string Label, bool IsActive);

public sealed record DemoQrBooking(Guid BookingId, Guid CodeId, Guid VisitId, int VisitDaysAgo, int VisitHour);

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
