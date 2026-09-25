using Trimme.BuildingBlocks.Web.Modules;

namespace Trimme.Modules.Bookings;

/// <summary>Module entry point (schema <c>bookings</c>). Features are added in their implementation phases (see docs/implementation).</summary>
public sealed class BookingsModule : ModuleBase
{
    public override string Name => "Bookings";

    public override string Schema => "bookings";
}
