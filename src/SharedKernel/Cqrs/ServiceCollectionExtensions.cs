using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tendero.SharedKernel;

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
    /// Registra dispatchers y descubre handlers y validadores en los ensamblados
    /// dados. Un slice nuevo no toca este método: basta con que su ensamblado
    /// ya esté en la lista de la composition root.
    /// </summary>
    public static IServiceCollection AddTenderoCqrs(
        this IServiceCollection services, params Assembly[] assemblies)
    {
        services.TryAddScoped<ICommandDispatcher, CommandDispatcher>();
        services.TryAddScoped<IQueryDispatcher, QueryDispatcher>();
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        foreach (var assembly in assemblies.Distinct())
            RegisterHandlers(services, assembly);

        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            if (type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } is false)
                continue;

            foreach (var contract in type.GetInterfaces())
            {
                if (!contract.IsGenericType)
                    continue;

                if (!ScannedInterfaces.Contains(contract.GetGenericTypeDefinition()))
                    continue;

                // TryAddEnumerable: reejecutar el escaneo (tests, host recargado)
                // no duplica registros, y varios handlers por evento conviven.
                services.TryAddEnumerable(ServiceDescriptor.Scoped(contract, type));
            }
        }
    }
}
