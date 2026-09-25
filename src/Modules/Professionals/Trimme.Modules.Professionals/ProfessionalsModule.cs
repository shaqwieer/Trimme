using Trimme.BuildingBlocks.Web.Modules;

namespace Trimme.Modules.Professionals;

/// <summary>Module entry point (schema <c>professionals</c>). Features are added in their implementation phases (see docs/implementation).</summary>
public sealed class ProfessionalsModule : ModuleBase
{
    public override string Name => "Professionals";

    public override string Schema => "professionals";
}
