using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Shouldly;
using Trimme.Api.Hosting;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Errors;
using Trimme.UnitTests.BuildingBlocks.Domain;

namespace Trimme.UnitTests.Api;

public sealed class HostingTests
{
    [Theory]
    [InlineData("Production", "true", "--dev")]
    [InlineData("Staging", "true", "--dev")]
    [InlineData("Testing", "true", "--dev")]
    [InlineData("Development", null, "--dev")]
    [InlineData("Development", "false", "--dev")]
    [InlineData("Development", "true", "--prod")]
    public void Seed_IsDevelopmentOnly_and_requires_explicit_opt_in(string environment, string? allowFlag, string flag)
    {
        DevSeedGuard.Evaluate(environment, allowFlag, ["seed", flag]).Allowed.ShouldBeFalse();
    }

    [Fact]
    public void Seed_is_allowed_with_development_flag_and_argument()
    {
        DevSeedGuard.Evaluate("Development", "true", ["seed", "--dev"]).Allowed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(new[] { "migrate" }, true)]
    [InlineData(new[] { "seed", "--dev" }, true)]
    [InlineData(new[] { "--urls", "http://+:8080" }, false)]
    [InlineData(new string[0], false)]
    public void Host_commands_are_recognised(string[] args, bool expected)
    {
        HostCommands.IsHostCommand(args).ShouldBe(expected);
    }

    [Theory]
    [InlineData(400, ApiErrorCodes.BadRequest)]
    [InlineData(401, ApiErrorCodes.Unauthenticated)]
    [InlineData(403, ApiErrorCodes.Forbidden)]
    [InlineData(404, ApiErrorCodes.NotFound)]
    [InlineData(405, ApiErrorCodes.MethodNotAllowed)]
    [InlineData(413, ApiErrorCodes.PayloadTooLarge)]
    [InlineData(429, ApiErrorCodes.RateLimited)]
    [InlineData(503, ApiErrorCodes.Unexpected)]
    public void Status_codes_map_to_stable_error_codes(int status, string expected)
    {
        ApiErrorCodes.ForStatus(status).ShouldBe(expected);
    }

    [Theory]
    [InlineData(ErrorKind.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorKind.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorKind.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorKind.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorKind.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorKind.BusinessRule, StatusCodes.Status422UnprocessableEntity)]
    public void Domain_errors_become_problem_responses_with_their_code(ErrorKind kind, int expectedStatus)
    {
        var problem = new Error("sample.code", "Sample", kind).ToProblem().ShouldBeOfType<ProblemHttpResult>();

        problem.StatusCode.ShouldBe(expectedStatus);
        problem.ProblemDetails.Extensions[ApiErrorCodes.ErrorCodeExtension].ShouldBe("sample.code");
    }

    [Fact]
    public void Entity_id_converter_round_trips_through_guid()
    {
        var converter = new EntityIdValueConverter<SampleId>();
        var id = new SampleId(Guid.CreateVersion7());

        var stored = (Guid)converter.ConvertToProvider(id)!;
        var restored = (SampleId)converter.ConvertFromProvider(stored)!;

        stored.ShouldBe(id.Value);
        restored.ShouldBe(id);
    }
}
