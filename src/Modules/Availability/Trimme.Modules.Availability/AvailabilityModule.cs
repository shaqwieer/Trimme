using Trimme.BuildingBlocks.Web.Modules;

namespace Trimme.Modules.Availability;

/// <summary>Module entry point (schema <c>availability</c>). Features are added in their implementation phases (see docs/implementation).</summary>
public sealed class AvailabilityModule : ModuleBase
{
    public override string Name => "Availability";

    public override string Schema => "availability";
}
