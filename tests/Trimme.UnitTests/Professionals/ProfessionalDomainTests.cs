using System.Reflection;
using Shouldly;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Professionals.Api;
using Trimme.Modules.Professionals.Domain;

namespace Trimme.UnitTests.Professionals;

/// <summary>R-NEG-01 / R-PRO-01: a professional belongs to one shop forever; the WhatsApp contact rules.</summary>
public sealed class ProfessionalDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

    private static Professional NewProfessional() =>
        Professional.Create(
            new ProfessionalId(Guid.CreateVersion7()),
            new ShopId(Guid.CreateVersion7()),
            "faisal",
            ProfessionalProfile.Create("فيصل", "Faisal", null, null, null, null),
            Now).Value;

    [Fact]
    public void Professional_ShopId_HasNoPublicSetter()
    {
        var property = typeof(Professional).GetProperty(nameof(Professional.ShopId))!;
        (property.SetMethod?.IsPublic ?? false).ShouldBeFalse();

        // No public method other than the factory takes a shop.
        typeof(Professional).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(ShopId)))
            .Select(m => m.Name)
            .ShouldBe([nameof(Professional.Create)]);
    }

    [Fact]
    public void NoProfessionalUpdateContract_ContainsShopId()
    {
        var updateContracts = new[] { typeof(UpdateProfessionalRequest), typeof(SetProfessionalWhatsAppRequest), typeof(ChangeProfessionalStatusRequest) };
        foreach (var contract in updateContracts)
        {
            contract.GetProperties().Select(p => p.Name).ShouldNotContain(name => name.Contains("Shop", StringComparison.OrdinalIgnoreCase), contract.Name);
        }

        typeof(CreateProfessionalRequest).GetProperty(nameof(CreateProfessionalRequest.ShopId)).ShouldNotBeNull("the shop is chosen once, at creation");
    }

    [Fact]
    public void Professional_RequiresAShop()
    {
        Professional.Create(new ProfessionalId(Guid.CreateVersion7()), default, "faisal", ProfessionalProfile.Create("ف", "F", null, null, null, null), Now)
            .IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void DisableAndEnable_AreExplicitTransitions()
    {
        var professional = NewProfessional();
        professional.Disable(Now).IsSuccess.ShouldBeTrue();
        professional.Status.ShouldBe(ProfessionalStatus.Disabled);
        professional.Disable(Now).Error!.Code.ShouldBe("professional.invalid_transition");
        professional.Enable(Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Notifications_NeedANumber_AndEligibilityNeedsBoth()
    {
        var contact = ProfessionalContact.For(NewProfessional());
        contact.Set(null, notificationsEnabled: true, Now).Error!.FieldErrors!["notificationsEnabled"].ShouldBe(["validation.whatsapp_required"]);

        contact.Set(new ProtectedPhone("cipher", "hash", "+966 5•• ••• •01"), notificationsEnabled: false, Now).IsSuccess.ShouldBeTrue();
        contact.CanReceiveNotifications.ShouldBeFalse("the toggle is off");

        contact.Set(new ProtectedPhone("cipher", "hash", "+966 5•• ••• •01"), notificationsEnabled: true, Now).IsSuccess.ShouldBeTrue();
        contact.CanReceiveNotifications.ShouldBeTrue();

        contact.Set(null, notificationsEnabled: false, Now).IsSuccess.ShouldBeTrue();
        contact.HasNumber.ShouldBeFalse();
        contact.WhatsAppMasked.ShouldBeNull();
    }

    [Fact]
    public void ChangingTheNumber_ResetsVerification()
    {
        var contact = ProfessionalContact.For(NewProfessional());
        contact.Set(new ProtectedPhone("a", "hash-a", "m"), true, Now);
        contact.Verification.ShouldBe(WhatsAppVerification.Unverified);
        contact.Set(new ProtectedPhone("b", "hash-b", "m"), true, Now);
        contact.Verification.ShouldBe(WhatsAppVerification.Unverified);
    }

    [Theory]
    [InlineData("Faisal Al-Qahtani", "faisal-al-qahtani")]
    [InlineData("  Omar   O'Salem ", "omar-o-salem")]
    [InlineData("فيصل", "fallback")]
    public void SlugFrom_MakesUrlSafeSlugs(string nameEn, string expected) =>
        Professional.SlugFrom(nameEn, "fallback").ShouldBe(expected);
}
