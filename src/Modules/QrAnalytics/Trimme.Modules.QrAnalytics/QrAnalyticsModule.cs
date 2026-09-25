using Trimme.BuildingBlocks.Web.Modules;

namespace Trimme.Modules.QrAnalytics;

/// <summary>Module entry point (schema <c>qr</c>). Features are added in their implementation phases (see docs/implementation).</summary>
public sealed class QrAnalyticsModule : ModuleBase
{
    public override string Name => "QrAnalytics";

    public override string Schema => "qr";
}
