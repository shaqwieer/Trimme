using Trimme.BuildingBlocks.Web.Modules;

namespace Trimme.Modules.Identity;

/// <summary>Module entry point (schema <c>identity</c>). Features are added in their implementation phases (see docs/implementation).</summary>
public sealed class IdentityModule : ModuleBase
{
    public override string Name => "Identity";

    public override string Schema => "identity";
}
