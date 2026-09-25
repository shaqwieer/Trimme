using Trimme.BuildingBlocks.Web.Modules;

namespace Trimme.Modules.Customers;

/// <summary>Module entry point (schema <c>customers</c>). Features are added in their implementation phases (see docs/implementation).</summary>
public sealed class CustomersModule : ModuleBase
{
    public override string Name => "Customers";

    public override string Schema => "customers";
}
