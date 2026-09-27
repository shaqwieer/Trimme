using Shouldly;
using Trimme.BuildingBlocks.Domain.Media;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.BuildingBlocks.Domain.Privacy;
using Trimme.Modules.Shops.Domain;

namespace Trimme.UnitTests.Shops;

public sealed class ShopProfileDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

    private static Shop NewShop() => Shop.Create(new ShopId(Guid.CreateVersion7()), "al-malqa", "صالون", "Salon", null, Now).Value;

    [Fact]
    public void NewShop_HasTheDefaultEditPolicy_WithNameCategoryAndLocationLocked()
    {
        var shop = NewShop();

        shop.EditableFields.ShouldBe(Shop.DefaultEditableFields, ignoreOrder: true);
        shop.IsEditableByShop(ShopProfileField.Description).ShouldBeTrue();
        shop.IsEditableByShop(ShopProfileField.Name).ShouldBeFalse();
        shop.IsEditableByShop(ShopProfileField.Category).ShouldBeFalse();
        shop.IsEditableByShop(ShopProfileField.Location).ShouldBeFalse();
    }

    [Fact]
    public void ChangedFields_ReportsOnlyFieldsWhoseValueChanges()
    {
        var shop = NewShop();
        var same = ShopProfile.Create(" صالون ", "Salon", "", null, ShopCategory.Barbershop, null, []);
        shop.ChangedFields(same).ShouldBeEmpty("trimmed and empty-as-null values are the same");

        var edited = ShopProfile.Create("صالون جديد", "Salon", "وصف", null, ShopCategory.Barbershop, "+966114567890", [ShopAmenity.WiFi]);
        shop.ChangedFields(edited).ShouldBe([ShopProfileField.Name, ShopProfileField.Description, ShopProfileField.PublicPhone, ShopProfileField.Amenities]);
    }

    [Fact]
    public void Amenities_AreDeduplicatedAndOrdered_SoReorderingIsNotAChange()
    {
        var shop = NewShop();
        shop.UpdateProfile(ShopProfile.Create("صالون", "Salon", null, null, ShopCategory.Barbershop, null, [ShopAmenity.WiFi, ShopAmenity.Parking, ShopAmenity.WiFi]), Now);

        shop.Amenities.ShouldBe([ShopAmenity.Parking, ShopAmenity.WiFi]);
        shop.ChangedFields(ShopProfile.Create("صالون", "Salon", null, null, ShopCategory.Barbershop, null, [ShopAmenity.WiFi, ShopAmenity.Parking]))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Location_StoresLongitudeAsX_LatitudeAsY_WithSrid4326_RoundedTo6Places()
    {
        var location = ShopLocation.Create(24.81234567, 46.60119999, "طريق أنس بن مالك", "الملقا", "الرياض", null, LocationSource.Manual, Now, null).Value;

        location.Point.SRID.ShouldBe(4326);
        location.Point.X.ShouldBe(46.6012);
        location.Point.Y.ShouldBe(24.812346);
        location.Latitude.ShouldBe(24.812346);
        location.Longitude.ShouldBe(46.6012);
    }

    [Theory]
    [InlineData(91, 46)]
    [InlineData(-91, 46)]
    [InlineData(24, 181)]
    [InlineData(double.NaN, 46)]
    public void Location_RejectsImpossibleCoordinates(double latitude, double longitude) =>
        ShopLocation.Create(latitude, longitude, null, null, null, null, LocationSource.Manual, Now, null).IsFailure.ShouldBeTrue();

    [Fact]
    public void Gallery_HoldsAtMostTwelveImages_AndRemovesOnlyItsOwn()
    {
        var shop = NewShop();
        var images = Enumerable.Range(0, Shop.MaxGalleryImages).Select(_ => new MediaId(Guid.CreateVersion7())).ToArray();
        foreach (var image in images)
        {
            shop.AddGalleryImage(image, Now).IsSuccess.ShouldBeTrue();
        }

        shop.AddGalleryImage(new MediaId(Guid.CreateVersion7()), Now).Error!.FieldErrors!["file"].ShouldBe(["validation.gallery_full"]);
        shop.RemoveGalleryImage(new MediaId(Guid.CreateVersion7()), Now).ShouldBeFalse("an image of another shop is not in this gallery");
        shop.RemoveGalleryImage(images[3], Now).ShouldBeTrue();
        shop.GalleryMediaIds.Length.ShouldBe(Shop.MaxGalleryImages - 1);
    }

    [Fact]
    public void ReplacingTheCover_ReturnsThePreviousImageForDeletion()
    {
        var shop = NewShop();
        var first = new MediaId(Guid.CreateVersion7());
        shop.ReplaceCover(first, Now).ShouldBeNull();
        shop.ReplaceCover(new MediaId(Guid.CreateVersion7()), Now).ShouldBe(first);
    }

    [Fact]
    public void DemoProfessionalNumbers_AreValidMobiles()
    {
        foreach (var demo in DemoData.Professionals)
        {
            PhoneNumber.TryParseMobile(demo.WhatsApp, out var phone).ShouldBeTrue(demo.Slug);
            phone!.E164.ShouldBe(demo.WhatsApp);
        }

        DemoData.Professionals.Select(p => p.WhatsApp).ShouldBeUnique();
        PhoneNumber.TryParse("+966114567890", out _).ShouldBeTrue("the demo shop business number is valid");
    }
}
