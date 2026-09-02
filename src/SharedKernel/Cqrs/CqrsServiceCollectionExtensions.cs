using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElGuerre.Tendero.SharedKernel;

public static class CqrsServiceCollectionExtensions
{
    private static readonly Type[] ScannedInterfaces =
    [
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>),
        typeof(IDomainEventHandler<>),
        typeof(IValidator<>)
    ];

    /// <summary>
    /// Registers the dispatchers and discovers handlers and validators in the
    /// given assemblies. A new slice does not touch this method: it is enough
    /// that its assembly is already on the composition root's list.
    /// </summary>
    public static IServiceCollection AddTenderoCqrs(
        this IServiceCollection services, params Assembly[] assemblies)
    {
        // The clock is registered here rather than in each context because time
        // is everybody's dependency: aggregates take it to stamp themselves, and
        // the time-bounded rules that are coming — promotion validity, mandate
        // expiry, reservation expiry, the returns window — are not verifiable
        // with DateTimeOffset.UtcNow baked in. TimeProvider is in the BCL, so it
        // is not a new dependency.
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddScoped<ICommandDispatcher, CommandDispatcher>();
        services.TryAddScoped<IQueryDispatcher, QueryDispatcher>();
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        foreach (var assembly in assemblies.Distinct())
            RegisterHandlers(services, assembly);

        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, Assembly assembly)
    {
        // GetTypes, not GetExportedTypes: an internal handler is legitimate (and
        // in fact preferable, since only the dispatcher invokes it), and with
        // GetExportedTypes it would go unregistered in silence, to fail at runtime.
        foreach (var type in assembly.GetTypes())
        {
            if (type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } is false)
                continue;

            foreach (var contract in type.GetInterfaces())
            {
                if (!contract.IsGenericType)
                    continue;

                if (!ScannedInterfaces.Contains(contract.GetGenericTypeDefinition()))
                    continue;

                // TryAddEnumerable: re-running the scan (tests, a reloaded host)
                // does not duplicate registrations, and several handlers per event coexist.
                services.TryAddEnumerable(ServiceDescriptor.Scoped(contract, type));
            }
        }
    }
}
