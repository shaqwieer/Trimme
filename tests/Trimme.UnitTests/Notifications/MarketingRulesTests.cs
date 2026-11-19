using Shouldly;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.Modules.Notifications.Domain;

namespace Trimme.UnitTests.Notifications;

/// <summary>
/// D-155: the marketing limit's month runs from the subscription's day (the client's «العدّاد يرجع صفر كل شهر، من تاريخ
/// اشتراك المحل»), only the win-back message counts, and the shop's cancellation gives its reason in words.
/// </summary>
public sealed class MarketingRulesTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2026-11-15", "2026-11-15", "2026-11-15", "2026-12-14")]
    [InlineData("2026-11-15", "2026-12-14", "2026-11-15", "2026-12-14")]
    [InlineData("2026-11-15", "2026-12-15", "2026-12-15", "2027-01-14")]
    [InlineData("2026-11-15", "2027-03-20", "2027-03-15", "2027-04-14")]
    [InlineData("2026-01-31", "2026-02-27", "2026-01-31", "2026-02-27")]
    [InlineData("2026-01-31", "2026-02-28", "2026-02-28", "2026-03-30")]
    [InlineData("2026-11-20", "2026-11-19", "2026-11-20", "2026-12-19")]
    public void TheWindow_RunsAMonthAtATime_FromTheSubscriptionsDay(string anchor, string today, string start, string end)
    {
        var (windowStart, windowEnd) = MarketingWindows.Of(D(anchor), D(today));
        windowStart.ShouldBe(D(start));
        windowEnd.ShouldBe(D(end));
    }

    [Fact]
    public void WithoutASubscription_TheWindowIsTheCalendarMonth() =>
        MarketingWindows.Calendar(new DateOnly(2026, 2, 14)).ShouldBe((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));

    [Fact]
    public void OnlyTheWinBackMessage_IsMarketing_AndOnlyItsDispatchCountsAgainstTheLimit()
    {
        Enum.GetValues<MessageEvent>().Where(WhatsAppTemplate.IsMarketing).ShouldBe([MessageEvent.WinBack]);
        Enum.GetValues<DispatchKind>().ShouldContain(DispatchKind.WinBack);
        Enum.GetValues<DispatchStatus>().ShouldContain(DispatchStatus.LimitReached);
    }

    [Theory]
    [InlineData("StaffUnwell", null, "ar", "الحلاق تعبان")]
    [InlineData("Emergency", "x", "ar", "ظرف طارئ عند الحلاق")]
    [InlineData("ShopClosed", null, "en", "the shop is closed (power or similar)")]
    [InlineData("Other", "  سافر الحلاق ", "ar", "سافر الحلاق")]
    [InlineData(null, "العميل اتصل", "ar", "العميل اتصل")]
    [InlineData(null, null, "ar", "ظرف عند المحل")]
    [InlineData(null, "  ", "en", "a matter at the shop")]
    public void TheShopsReason_IsWrittenForTheCustomer(string? code, string? text, string locale, string expected) =>
        MessageFormat.CancellationReason(code, text, locale).ShouldBe(expected);
}
