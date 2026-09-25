using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Infrastructure.Persistence;

namespace Trimme.BuildingBlocks.Web.Modules;

/// <summary>
/// Entry point of a feature module (D-038). The API host registers modules explicitly in <c>ModuleCatalog</c>.
/// </summary>
public interface IModule : IModelContributor
{
    string Name { get; }

    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Maps the module's endpoints under the versioned API group (<c>/api/v1</c>).</summary>
    void MapEndpoints(IEndpointRouteBuilder api);
}

/// <summary>
/// Base class with sensible defaults: handlers and validators are discovered in the module assembly,
/// entity configurations are applied only from types in the module's own namespace.
/// </summary>
public abstract class ModuleBase : IModule
{
    public abstract string Name { get; }

    public abstract string Schema { get; }

    public Assembly Assembly => GetType().Assembly;

    public virtual void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        Application.DependencyInjection.AddRequestHandlersFrom(services, Assembly);
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder api)
    {
    }

    public virtual void ConfigureModel(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        if (HasEntityConfigurations(Assembly))
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly);
        }

        // Schema-per-module convention (D-038): entities declared in this module default to its schema.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(e => e.ClrType.Assembly == Assembly && !e.IsOwned() && e.GetSchema() is null))
        {
            entityType.SetSchema(Schema);
        }
    }

    private static bool HasEntityConfigurations(Assembly assembly) =>
        assembly.GetTypes().Any(t => t is { IsAbstract: false, IsGenericTypeDefinition: false }
                                     && t.GetInterfaces().Any(i => i.IsGenericType
                                                                   && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)));
}
