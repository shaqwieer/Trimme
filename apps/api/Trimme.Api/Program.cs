using Trimme.Api;
using Trimme.Api.Hosting;

// Container health probe: answered before any host is built (no configuration, no DI).
if (HostCommands.IsHealthcheck(args))
{
    return await HostCommands.RunHealthcheckAsync();
}

// Everything up to Build() must be free of side effects: EF Core tooling runs this entry point
// and stops at Build() to obtain the service provider.
var builder = WebApplication.CreateBuilder(args);
builder.AddTrimmeApi(ModuleCatalog.All);
var app = builder.Build();

if (HostCommands.IsHostCommand(args))
{
    return await HostCommands.RunAsync(app, args);
}

app.UseTrimmeApi(ModuleCatalog.All);
await app.RunAsync();
return 0;

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
