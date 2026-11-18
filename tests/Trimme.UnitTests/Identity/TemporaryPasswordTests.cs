using Shouldly;
using Trimme.Modules.Identity.Application.Barbers;
using Trimme.Modules.Identity.Domain;

namespace Trimme.UnitTests.Identity;

/// <summary>D-146: the temporary password the server generates for a barber's login.</summary>
public sealed class TemporaryPasswordTests
{
    [Fact]
    public void TemporaryPasswords_AreTwelveEasyToReadCharacters_ThatPassIdentitysRules_AndRarelyRepeat()
    {
        var passwords = Enumerable.Range(0, 2000).Select(_ => TemporaryPasswords.New()).ToList();

        passwords.ShouldAllBe(p => p.Length == TemporaryPasswords.Length);
        passwords.ShouldAllBe(p => p.All(c => TemporaryPasswords.Alphabet.Contains(c)));
        passwords.ShouldAllBe(p => p.Distinct().Count() >= 4, "IdentityModule asks for 4 different characters");
        passwords.Distinct(StringComparer.Ordinal).Count().ShouldBe(passwords.Count);

        // No look-alikes to misread when the owner reads it out or the barber types it.
        foreach (var lookAlike in "0o1liIO")
        {
            TemporaryPasswords.Alphabet.ShouldNotContain(lookAlike);
        }
    }

    [Fact]
    public void TheOwner_ManagesBarbers_ButBooksNoWalkIns_TheBarbersDo()
    {
        var owner = SystemRoles.Find(SystemRoles.ShopOwner).ShouldNotBeNull().DefaultPermissions;
        var barber = SystemRoles.Find(SystemRoles.ShopBarber).ShouldNotBeNull().DefaultPermissions;

        owner.ShouldContain(Permissions.Shop.BarbersManage);
        owner.ShouldContain(Permissions.Shop.BarbersManageLogin);
        owner.ShouldNotContain(Permissions.Shop.BookingsCreateWalkIn);
        barber.ShouldContain(Permissions.Shop.BookingsCreateWalkIn);
        barber.ShouldNotContain(Permissions.Shop.BarbersManage);
    }
}
