using Shouldly;
using Trimme.BuildingBlocks.Domain.Results;

namespace Trimme.UnitTests.BuildingBlocks.Domain;

public sealed class ResultTests
{
    private static readonly Error SlotTaken = Error.Conflict("booking.slot_unavailable", "The slot is no longer available.");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Failure_carries_error()
    {
        Result result = SlotTaken;

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SlotTaken);
    }

    [Fact]
    public void Generic_success_exposes_value()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Reading_value_of_failure_throws()
    {
        Result<int> result = SlotTaken;

        var exception = Should.Throw<InvalidOperationException>(() => _ = result.Value);
        exception.Message.ShouldContain("booking.slot_unavailable");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Error_requires_a_code(string code)
    {
        Should.Throw<ArgumentException>(() => new Error(code, "message", ErrorKind.Validation));
    }

    [Fact]
    public void Error_factories_set_kind()
    {
        Error.Validation("a.b", "m").Kind.ShouldBe(ErrorKind.Validation);
        Error.NotFound("a.b", "m").Kind.ShouldBe(ErrorKind.NotFound);
        Error.Conflict("a.b", "m").Kind.ShouldBe(ErrorKind.Conflict);
        Error.Forbidden("a.b", "m").Kind.ShouldBe(ErrorKind.Forbidden);
        Error.Unauthorized("a.b", "m").Kind.ShouldBe(ErrorKind.Unauthorized);
        Error.BusinessRule("a.b", "m").Kind.ShouldBe(ErrorKind.BusinessRule);
    }
}
