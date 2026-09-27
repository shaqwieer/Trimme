using Shouldly;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Services.Domain;

namespace Trimme.UnitTests.Services;

/// <summary>R-SVC-01/02/05: shop-owned services and packages, their price/duration rules and archive semantics.</summary>
public sealed class CatalogDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly ShopId Shop = new(Guid.CreateVersion7());

    private static ShopService NewService(decimal price = 60m, int duration = 30) =>
        ShopService.Create(new ShopServiceId(Guid.CreateVersion7()), Shop, CatalogText.Create("حلاقة", null, null, null), null, price, duration, true, 1, Now).Value;

    [Theory]
    [InlineData(0, true)]
    [InlineData(60.5, true)]
    [InlineData(100000, true)]
    [InlineData(100000.01, false)]
    [InlineData(10.005, false)]
    [InlineData(-1, false)]
    public void Price_IsZeroTo100000Sar_WithAtMostTwoDecimals(decimal price, bool valid) =>
        CatalogRules.IsValidPrice(price).ShouldBe(valid);

    [Theory]
    [InlineData(5, true)]
    [InlineData(35, true)]
    [InlineData(480, true)]
    [InlineData(0, false)]
    [InlineData(7, false)]
    [InlineData(485, false)]
    public void Duration_IsAMultipleOfFiveMinutes_UpToEightHours(int minutes, bool valid) =>
        CatalogRules.IsValidDuration(minutes).ShouldBe(valid);

    [Fact]
    public void InvalidPriceAndDuration_AreReportedPerField()
    {
        var result = ShopService.Create(new ShopServiceId(Guid.CreateVersion7()), Shop, CatalogText.Create("س", null, null, null), null, 10.005m, 7, true, 1, Now);
        result.Error!.FieldErrors!.Keys.ShouldBe(["price", "durationMinutes"], ignoreOrder: true);
    }

    [Fact]
    public void Archive_IsFinal_AndTurnsTheServiceOff()
    {
        var service = NewService();
        service.Archive(Now).IsSuccess.ShouldBeTrue();
        service.IsActive.ShouldBeFalse();
        service.IsPubliclyAvailable.ShouldBeFalse();

        service.Archive(Now).Error!.Code.ShouldBe("catalog.archived");
        service.SetActive(true, Now).Error!.Code.ShouldBe("catalog.archived");
        service.Update(CatalogText.Create("س", null, null, null), null, 10, 10, true, Now).Error!.Code.ShouldBe("catalog.archived");
    }

    [Fact]
    public void PubliclyAvailable_NeedsActive_NotArchived_AndVisible()
    {
        var service = NewService();
        service.IsPubliclyAvailable.ShouldBeTrue();
        service.Moderate(ModerationState.Hidden, "Misleading name", Now);
        service.IsPubliclyAvailable.ShouldBeFalse();
        service.ModerationReason.ShouldBe("Misleading name");
        service.Moderate(ModerationState.Visible, "ignored", Now);
        service.ModerationReason.ShouldBeNull();
        service.SetActive(false, Now);
        service.IsPubliclyAvailable.ShouldBeFalse();
    }

    [Fact]
    public void Package_Duration_And_Items()
    {
        var a = new ShopServiceId(Guid.CreateVersion7());
        var b = new ShopServiceId(Guid.CreateVersion7());
        var c = new ShopServiceId(Guid.CreateVersion7());
        var package = ServicePackage.Create(new ServicePackageId(Guid.CreateVersion7()), Shop, CatalogText.Create("باقة", null, null, null), 85m, 50, [a, b], 1, Now).Value;

        package.DurationMinutes.ShouldBe(50, "an explicit total, not the sum of the items");
        package.ExpandItems().ShouldBe([a, b]);
        package.Items.ShouldAllBe(i => i.ShopId == Shop);

        // Reordering keeps the existing item rows (their keys cannot be deleted and re-added in one save).
        var itemOfA = package.Items.Single(i => i.ServiceId == a);
        var later = Now.AddMinutes(5);
        package.ReplaceItems([b, a, c], later).IsSuccess.ShouldBeTrue();
        package.ExpandItems().ShouldBe([b, a, c]);
        package.Items.Single(i => i.ServiceId == a).ShouldBeSameAs(itemOfA);
        package.UpdatedAt.ShouldBe(later, "an items-only edit still updates the package row (concurrency check)");
    }

    [Fact]
    public void Package_NeedsTwoToTenDistinctServices()
    {
        var a = new ShopServiceId(Guid.CreateVersion7());
        ServicePackage.Create(new ServicePackageId(Guid.CreateVersion7()), Shop, CatalogText.Create("ب", null, null, null), 50m, 30, [a], 1, Now)
            .Error!.FieldErrors!["serviceIds"].ShouldBe(["validation.package_items"]);
        ServicePackage.Create(new ServicePackageId(Guid.CreateVersion7()), Shop, CatalogText.Create("ب", null, null, null), 50m, 30, [a, a], 1, Now)
            .IsFailure.ShouldBeTrue();
        var eleven = Enumerable.Range(0, 11).Select(_ => new ShopServiceId(Guid.CreateVersion7())).ToList();
        ServicePackage.Create(new ServicePackageId(Guid.CreateVersion7()), Shop, CatalogText.Create("ب", null, null, null), 50m, 30, eleven, 1, Now)
            .IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void CatalogText_TrimsAndTreatsEmptyAsMissing()
    {
        var text = CatalogText.Create("  حلاقة ", " ", null, "  Cut ");
        text.NameAr.ShouldBe("حلاقة");
        text.NameEn.ShouldBeNull("English is optional (D-070)");
        text.DescriptionEn.ShouldBe("Cut");
    }
}
