using Trimme.BuildingBlocks.Web.Modules;

namespace Trimme.Modules.Administration;

/// <summary>Module entry point (schema <c>administration</c>). Features are added in their implementation phases (see docs/implementation).</summary>
public sealed class AdministrationModule : ModuleBase
{
    public override string Name => "Administration";

    public override string Schema => "administration";
}
