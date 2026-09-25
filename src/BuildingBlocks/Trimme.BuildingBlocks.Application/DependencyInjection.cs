using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Validation;

namespace Trimme.BuildingBlocks.Application;

public static class DependencyInjection
{
    /// <summary>Registers the dispatcher, the system clock and the default pipeline (validation). Idempotent.</summary>
    public static IServiceCollection AddTrimmeApplication(this IServiceCollection services)
    {
        services.TryAddScoped<IDispatcher, Dispatcher>();
        services.TryAddSingleton(TimeProvider.System);

        var validationRegistered = services.Any(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) && d.ImplementationType == typeof(ValidationBehavior<,>));
        if (!validationRegistered)
        {
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        }

        return services;
    }

    /// <summary>
    /// Registers every request handler and FluentValidation validator in <paramref name="assembly"/>,
    /// including internal types (module handlers are internal by convention, see D-038).
    /// </summary>
    public static IServiceCollection AddRequestHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var concreteTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });

        foreach (var type in concreteTypes)
        {
            var contracts = type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>));
            foreach (var contract in contracts)
            {
                services.AddScoped(contract, type);
            }
        }

        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Scoped, includeInternalTypes: true);
        return services;
    }
}
