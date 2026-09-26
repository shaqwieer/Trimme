using Shouldly;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Domain.Privacy;
using Trimme.Modules.Identity.Domain;

namespace Trimme.UnitTests.BuildingBlocks.Privacy;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("+966 50 214 8830", "+966502148830")]
    [InlineData("0502148830", "+966502148830")]
    [InlineData("00966 55 123 4567", "+966551234567")]
    [InlineData("٠٥٦١٢٣٤٥٦٧", "+966561234567")]
    [InlineData("۰۵۹۱۲۳۴۵۶۷", "+966591234567")]
    [InlineData("+971 50 123 4567", "+971501234567")]
    [InlineData("+20 100 123 4567", "+201001234567")]
    public void PhoneNumber_NormalizesToE164(string input, string expected)
    {
        PhoneNumber.TryParse(input, out var phone).ShouldBeTrue();
        phone.E164.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("not a phone")]
    [InlineData("+966 50 214 88")]
    public void PhoneNumber_RejectsInvalidInput(string? input) =>
        PhoneNumber.TryParse(input, out _).ShouldBeFalse();

    [Fact]
    public void PhoneNumber_Masks_AndNeverPrintsTheNumber()
    {
        PhoneNumber.TryParse("0502148830", out var phone).ShouldBeTrue();
        phone.Masked.ShouldBe("+966 5•• ••• •30");
        phone.ToString().ShouldBe(phone.Masked);
        $"{phone}".ShouldNotContain("502148830");
    }

    [Fact]
    public void Mobile_parsing_rejects_landlines()
    {
        PhoneNumber.TryParseMobile("+966 11 234 5678", out _).ShouldBeFalse();
        PhoneNumber.TryParseMobile("+966 55 123 4567", out var mobile).ShouldBeTrue();
        mobile.E164.ShouldBe("+966551234567");
    }

    /// <summary>
    /// Stored customer lookup hashes are HMAC(purpose, E.164) from the Phase 04 normaliser. For every number both accept,
    /// the value object must produce byte-identical E.164 and masks, so existing customers are still found (D-060).
    /// </summary>
    [Theory]
    [InlineData("+966502148830")]
    [InlineData("0551234567")]
    [InlineData("966561234567")]
    [InlineData("00966591234567")]
    [InlineData("٠٥٣١٢٣٤٥٦٧")]
    [InlineData("۰۵۴۱۲۳۴۵۶۷")]
    [InlineData("+966 58 765 4321")]
    public void Customer_normaliser_and_value_object_agree(string input)
    {
        MobileNumber.TryNormalize(input, out var legacy).ShouldBeTrue();
        PhoneNumber.TryParse(input, out var phone).ShouldBeTrue();

        phone.E164.ShouldBe(legacy);
        phone.Masked.ShouldBe(MobileNumber.Mask(legacy));
    }

    [Theory]
    [InlineData(null, null, 1, 20)]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-3, 500, 1, 100)]
    [InlineData(4, 25, 4, 25)]
    public void PageRequest_ClampsPageSize(int? page, int? size, int expectedPage, int expectedSize)
    {
        var request = new PageRequest(page, size);
        request.Page.ShouldBe(expectedPage);
        request.PageSize.ShouldBe(expectedSize);
        request.Skip.ShouldBe((expectedPage - 1) * expectedSize);
    }
}
