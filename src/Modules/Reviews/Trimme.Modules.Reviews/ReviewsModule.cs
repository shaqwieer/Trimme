using Trimme.BuildingBlocks.Web.Modules;

namespace Trimme.Modules.Reviews;

/// <summary>Module entry point (schema <c>reviews</c>). Features are added in their implementation phases (see docs/implementation).</summary>
public sealed class ReviewsModule : ModuleBase
{
    public override string Name => "Reviews";

    public override string Schema => "reviews";
}
