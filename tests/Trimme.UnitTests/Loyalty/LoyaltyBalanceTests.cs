using Shouldly;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Loyalty.Domain;

namespace Trimme.UnitTests.Loyalty;

/// <summary>
/// D-149's balance rules: a full card does not stop (the extra goes to the next one), a wipe takes the card after a period
/// without visits, a decay takes one point per period, held points are never lost, and a reward's discount.
/// </summary>
public sealed class LoyaltyBalanceTests
{
    private static readonly DateTimeOffset Visit = new(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(3));
    private static readonly ShopId Shop = new(Guid.CreateVersion7());

    private static LoyaltySettings Settings(LoyaltyInactivityRule rule = LoyaltyInactivityRule.None, int? days = null, int reward = 6) =>
        LoyaltySettings.Create(new LoyaltySettingsId(Guid.CreateVersion7()), Shop,
            new LoyaltyRules(3, 1, reward, LoyaltyRewardType.AmountOff, null, 20m, rule, days), null, Visit).Value;

    private static LoyaltyAccount Account(int points)
    {
        var account = LoyaltyAccount.Open(Shop, "member", Visit);
        account.Visit(points, Visit);
        return account;
    }

    [Theory]
    [InlineData(5, 0, 5, 1)]
    [InlineData(6, 1, 0, 6)]
    [InlineData(8, 1, 2, 4)]
    [InlineData(13, 2, 1, 5)]
    public void AFullCard_DoesNotStop(int points, int ready, int card, int toNext)
    {
        LoyaltyBalance.Card(points, 6).ShouldBe(new LoyaltyCardState(points, ready, card, toNext));
    }

    [Fact]
    public void AWipe_TakesTheCard_OnceThePeriodHasPassed_ButNotTheHeldPoints()
    {
        var settings = Settings(LoyaltyInactivityRule.Wipe, 30);
        var account = Account(9);
        LoyaltyBalance.Available(account, settings, held: 0, Visit.AddDays(29)).ShouldBe(9);
        LoyaltyBalance.Available(account, settings, held: 0, Visit.AddDays(30)).ShouldBe(0);
        LoyaltyBalance.Available(account, settings, held: 6, Visit.AddDays(30)).ShouldBe(0, "the free 3 are wiped, the held 6 are set aside");

        var (kind, lost, at) = account.Settle(settings, held: 6, Visit.AddDays(45));
        kind.ShouldBe(LoyaltyEntryKind.Wiped);
        lost.ShouldBe(3);
        at.ShouldBe(Visit.AddDays(30));
        account.Points.ShouldBe(6);
    }

    [Fact]
    public void ADecay_TakesOnePointPerPeriod_OnlyOnce()
    {
        var settings = Settings(LoyaltyInactivityRule.DecayPerPeriod, 30);
        var account = Account(5);
        LoyaltyBalance.Available(account, settings, 0, Visit.AddDays(61)).ShouldBe(3);
        account.Settle(settings, 0, Visit.AddDays(61)).Lost.ShouldBe(2);
        account.Points.ShouldBe(3);
        account.Settle(settings, 0, Visit.AddDays(62)).Lost.ShouldBe(0, "the two periods were already taken");
        account.Settle(settings, 0, Visit.AddDays(91)).Lost.ShouldBe(1);

        // A visit starts the clock again.
        account.Visit(1, Visit.AddDays(92));
        LoyaltyBalance.Available(account, settings, 0, Visit.AddDays(100)).ShouldBe(3);
    }

    [Fact]
    public void WithoutARule_PointsAreKept()
    {
        LoyaltyBalance.Available(Account(4), Settings(), 0, Visit.AddYears(3)).ShouldBe(4);
        LoyaltyBalance.Available(null, Settings(), 0, Visit).ShouldBe(0);
    }

    [Fact]
    public void TheDiscount_IsTheAmount_OrTheFreeServicesPrice_NeverMoreThanThePrice()
    {
        var haircut = Guid.CreateVersion7();
        var amountOff = Settings();
        LoyaltyBalance.Discount(amountOff, 60m, []).ShouldBe(20m);
        LoyaltyBalance.Discount(amountOff, 15m, []).ShouldBe(15m);

        var free = LoyaltySettings.Create(new LoyaltySettingsId(Guid.CreateVersion7()), Shop,
            new LoyaltyRules(1, 1, 4, LoyaltyRewardType.FreeService, haircut, null, LoyaltyInactivityRule.None, null), ("حلاقة", "Haircut"), Visit).Value;
        LoyaltyBalance.Discount(free, 80m, [(haircut, 60m), (Guid.CreateVersion7(), 20m)]).ShouldBe(60m);
        LoyaltyBalance.Discount(free, 20m, [(Guid.CreateVersion7(), 20m)]).ShouldBeNull();
        free.RewardTitle().ShouldBe(("حلاقة", "Haircut"));
    }

    [Theory]
    [InlineData(20.0, "خصم ٢٠ ⃁", "SAR 20 off")]
    [InlineData(12.5, "خصم ١٢٫٥ ⃁", "SAR 12.5 off")]
    public void AnAmountOff_IsTitledAsTheAppWritesPrices(double amount, string arabic, string english)
    {
        var settings = LoyaltySettings.Create(new LoyaltySettingsId(Guid.CreateVersion7()), Shop,
            new LoyaltyRules(1, 1, 4, LoyaltyRewardType.AmountOff, null, (decimal)amount, LoyaltyInactivityRule.None, null), null, Visit).Value;
        settings.RewardTitle().ShouldBe((arabic, english));
    }

    [Theory]
    [InlineData(0, 1, 4, "AmountOff", 20.0, "None", null, "firstVisitPoints")]
    [InlineData(1, 0, 4, "AmountOff", 20.0, "None", null, "visitPoints")]
    [InlineData(1, 1, 1, "AmountOff", 20.0, "None", null, "pointsForReward")]
    [InlineData(1, 1, 4, "AmountOff", 4.99, "None", null, "rewardAmount")]
    [InlineData(1, 1, 4, "AmountOff", 1000.01, "None", null, "rewardAmount")]
    [InlineData(1, 1, 4, "FreeService", null, "None", null, "rewardServiceId")]
    [InlineData(1, 1, 4, "AmountOff", 20.0, "Wipe", 6, "inactivityDays")]
    public void TheRules_AreValidated_FieldByField(int first, int visit, int reward, string type, double? amount, string rule, int? days, string field)
    {
        var error = LoyaltySettings.Validate(new LoyaltyRules(
            first, visit, reward, Enum.Parse<LoyaltyRewardType>(type), null, amount is null ? null : (decimal)amount.Value, Enum.Parse<LoyaltyInactivityRule>(rule), days));
        error.ShouldNotBeNull().FieldErrors.ShouldNotBeNull().Keys.ShouldContain(field);
    }

    /// <summary>D-152 (the client's 1-3): an old D-132 coupon now comes off the price: a free service all of it, a percent its share.</summary>
    [Theory]
    [InlineData("FreeService", null, 60.0, 60.0)]
    [InlineData("FreeService", null, 0.0, 0.0)]
    [InlineData("Discount", 25, 60.0, 15.0)]
    [InlineData("Discount", 15, 85.0, 12.75)]
    [InlineData("Discount", 33, 10.0, 3.3)]
    [InlineData("Discount", 100, 85.0, 85.0)]
    public void AnOldCoupon_TakesItsValueOffThePrice(string kind, int? percent, double price, double discount)
    {
        var program = LoyaltyProgram.Create(
            new LoyaltyProgramId(Guid.CreateVersion7()), Shop, new LoyaltyTerms(true, 4, Enum.Parse<LoyaltyRewardKind>(kind), percent, "مكافأة", null, null), Visit).Value;
        var coupon = LoyaltyReward.Issue(new LoyaltyRewardId(Guid.CreateVersion7()), program, Guid.CreateVersion7(), LoyaltyCard.NewCode(), Visit);
        coupon.DiscountOn((decimal)price).ShouldBe((decimal)discount);
    }
}
