using Shouldly;
using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Shops.Domain;

namespace Trimme.UnitTests.Auditing;

/// <summary>
/// D-155 (the client's 8-6, «سجل النشاط كله بالعربي»): an audit summary's arguments are invariant text the web words in the
/// reader's language — numbers without grouping, decimals with a point, instants in UTC ISO 8601, dates as yyyy-MM-dd,
/// booleans as true/false, lists comma-joined; a null is left out. And a pause past its return time is over.
/// </summary>
public sealed class AuditArgsTests
{
    [Fact]
    public void Arguments_AreInvariantText_AndNullsAreLeftOut()
    {
        var args = AuditArgs.Of(
            ("count", 1234),
            ("price", 49.5m),
            ("at", new DateTimeOffset(2026, 11, 19, 21, 30, 0, TimeSpan.FromHours(3))),
            ("day", new DateOnly(2026, 11, 19)),
            ("on", true),
            ("status", DayOfWeek.Friday),
            ("missing", null),
            ("fields", AuditArgs.List(new[] { "nameAr", "publicPhone" })));

        args.ShouldBe(new Dictionary<string, string>
        {
            ["count"] = "1234",
            ["price"] = "49.5",
            ["at"] = "2026-11-19T18:30:00Z",
            ["day"] = "2026-11-19",
            ["on"] = "true",
            ["status"] = "Friday",
            ["fields"] = "nameAr,publicPhone",
        });
    }

    [Fact]
    public void APause_IsOverAtItsReturnTime_OrLastsUntilResumedWithoutOne()
    {
        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var shop = new ShopId(Guid.NewGuid());
        new OnlineBookingPause(shop, now, "  صيانة  ").InForce(now.AddDays(30)).ShouldBeTrue();
        new OnlineBookingPause(shop, now, "  صيانة  ").Reason.ShouldBe("صيانة");
        var timed = new OnlineBookingPause(shop, now, null, now.AddHours(2), byAdmin: true);
        timed.InForce(now.AddHours(1)).ShouldBeTrue();
        timed.InForce(now.AddHours(2)).ShouldBeFalse();
        timed.ByAdmin.ShouldBeTrue();
    }
}
