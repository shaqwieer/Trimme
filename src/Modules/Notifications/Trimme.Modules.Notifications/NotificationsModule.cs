using Trimme.BuildingBlocks.Web.Modules;

namespace Trimme.Modules.Notifications;

/// <summary>Module entry point (schema <c>notifications</c>). Features are added in their implementation phases (see docs/implementation).</summary>
public sealed class NotificationsModule : ModuleBase
{
    public override string Name => "Notifications";

    public override string Schema => "notifications";
}
