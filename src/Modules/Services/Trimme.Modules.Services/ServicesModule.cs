using Trimme.BuildingBlocks.Web.Modules;

namespace Trimme.Modules.Services;

/// <summary>Module entry point (schema <c>services</c>). Features are added in their implementation phases (see docs/implementation).</summary>
public sealed class ServicesModule : ModuleBase
{
    public override string Name => "Services";

    public override string Schema => "services";
}
