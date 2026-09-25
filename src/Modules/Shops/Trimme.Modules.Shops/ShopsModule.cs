using Trimme.BuildingBlocks.Web.Modules;

namespace Trimme.Modules.Shops;

/// <summary>Module entry point (schema <c>shops</c>). Features are added in their implementation phases (see docs/implementation).</summary>
public sealed class ShopsModule : ModuleBase
{
    public override string Name => "Shops";

    public override string Schema => "shops";
}
