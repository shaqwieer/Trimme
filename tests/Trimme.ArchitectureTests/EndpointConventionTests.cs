using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Trimme.ArchitectureTests;

/// <summary>Conventions every TRIMME endpoint must follow (R-FND-12).</summary>
public sealed class EndpointConventionTests
{
    [Fact]
    public void Endpoints_AcceptCancellationToken()
    {
        using var factory = new EndpointInspectionFactory();
        var endpoints = TrimmeHandlerMethods(factory).ToArray();

        endpoints.ShouldNotBeEmpty();
        var missing = endpoints
            .Where(e => e.Method.GetParameters().All(p => p.ParameterType != typeof(CancellationToken)))
            .Select(e => e.Route)
            .ToArray();

        missing.ShouldBeEmpty("Every endpoint handler must accept a CancellationToken and pass it down.");
    }

    [Fact]
    public void Feature_endpoints_are_versioned_under_api_v1()
    {
        using var factory = new EndpointInspectionFactory();

        var unversioned = TrimmeHandlerMethods(factory)
            .Where(e => !e.Route.StartsWith("/api/v1/", StringComparison.Ordinal))
            .Select(e => e.Route)
            .ToArray();

        unversioned.ShouldBeEmpty();
    }

    private static IEnumerable<(string Route, MethodInfo Method)> TrimmeHandlerMethods(WebApplicationFactory<Program> factory)
    {
        var sources = factory.Services.GetServices<EndpointDataSource>();
        foreach (var endpoint in sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>())
        {
            var method = endpoint.Metadata.OfType<MethodInfo>().FirstOrDefault();
            if (method?.DeclaringType?.Namespace?.StartsWith("Trimme.", StringComparison.Ordinal) == true)
            {
                yield return ("/" + endpoint.RoutePattern.RawText?.TrimStart('/'), method);
            }
        }
    }

    /// <summary>Builds the real host without touching a database (no request is sent).</summary>
    private sealed class EndpointInspectionFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Trimme", "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused");
        }
    }
}
