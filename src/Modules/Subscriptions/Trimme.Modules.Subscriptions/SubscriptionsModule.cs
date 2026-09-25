using Trimme.BuildingBlocks.Web.Modules;

namespace Trimme.Modules.Subscriptions;

/// <summary>Module entry point (schema <c>subscriptions</c>). Features are added in their implementation phases (see docs/implementation).</summary>
public sealed class SubscriptionsModule : ModuleBase
{
    public override string Name => "Subscriptions";

    public override string Schema => "subscriptions";
}
