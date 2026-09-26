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
}

public sealed record DemoShop(ShopId Id, string Slug, string NameAr, string NameEn, string OwnerEmail, string StaffEmail);
