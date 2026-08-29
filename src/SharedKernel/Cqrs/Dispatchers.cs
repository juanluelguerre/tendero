using System.Collections.Concurrent;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Tendero.SharedKernel;

/// <summary>
/// Dispatcher sobre el contenedor de DI. La única "middleware" es la validación
/// con FluentValidation: cualquier otra cosa (caché, reintentos, transacciones)
/// entrará cuando exista un caso real, no antes.
/// </summary>
public sealed class CommandDispatcher(IServiceProvider services) : ICommandDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var wrapper = (RequestHandlerWrapper<TResult>)Wrappers.GetOrAdd(
            command.GetType(),
            static type => RequestHandlerWrapper.Create(
                type, typeof(ICommand<>), typeof(CommandHandlerWrapper<,>)));

        return wrapper.HandleAsync(command, services, cancellationToken);
    }
}

public sealed class QueryDispatcher(IServiceProvider services) : IQueryDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var wrapper = (RequestHandlerWrapper<TResult>)Wrappers.GetOrAdd(
            query.GetType(),
            static type => RequestHandlerWrapper.Create(
                type, typeof(IQuery<>), typeof(QueryHandlerWrapper<,>)));

        return wrapper.HandleAsync(query, services, cancellationToken);
    }
}

/// <summary>
/// Entrega un evento de dominio a todos sus handlers. Sin filtrado ni orden
/// garantizado: si dos proyecciones dependen entre sí, eso es un problema de
/// diseño del slice, no del dispatcher.
/// </summary>
public sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, DomainEventWrapper> Wrappers = new();

    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var wrapper = Wrappers.GetOrAdd(domainEvent.GetType(), static type =>
            (DomainEventWrapper)Activator.CreateInstance(
                typeof(DomainEventWrapper<>).MakeGenericType(type))!);

        return wrapper.PublishAsync(domainEvent, services, cancellationToken);
    }
}

// ---------- Plumbing ----------
//
// La reflexión ocurre UNA vez por tipo de petición, sólo para construir el
// wrapper cerrado. A partir de ahí todo son llamadas virtuales sobre la interfaz
// genérica: el handler se invoca directamente, nunca vía MethodInfo.Invoke, para
// que la excepción que lance el dominio llegue al llamante sin envolver.

internal abstract class RequestHandlerWrapper<TResult>
{
    public abstract Task<TResult> HandleAsync(object request, IServiceProvider services, CancellationToken ct);

    /// <summary>Validar, resolver el handler, ejecutarlo. Command y query sólo se
    /// diferencian en qué interfaz resuelven, así que el resto vive aquí.</summary>
    protected static async Task<TResult> ValidateAndHandleAsync<TRequest, THandler>(
        object request,
        IServiceProvider services,
        CancellationToken ct,
        Func<THandler, TRequest, CancellationToken, Task<TResult>> handle)
        where THandler : class
    {
        var typed = (TRequest)request;

        await ValidationStep.EnsureValidAsync(typed, services, ct);

        var handler = services.GetService<THandler>()
            ?? throw new InvalidOperationException(
                $"No handler registered for {typeof(TRequest).Name}. " +
                $"Expected an implementation of {typeof(THandler).Name}.");

        return await handle(handler, typed, ct);
    }
}

internal static class RequestHandlerWrapper
{
    /// <summary>
    /// Construye el wrapper cerrado para el tipo concreto de la petición.
    /// <paramref name="requestInterface"/> es ICommand&lt;&gt; o IQuery&lt;&gt;;
    /// de ahí se saca TResult sin que el llamante tenga que declararlo.
    /// </summary>
    public static object Create(Type requestType, Type requestInterface, Type wrapperDefinition)
    {
        var closed = Array.Find(
            requestType.GetInterfaces(),
            candidate => candidate.IsGenericType
                         && candidate.GetGenericTypeDefinition() == requestInterface)
            ?? throw new InvalidOperationException(
                $"{requestType.Name} does not implement {requestInterface.Name}.");

        var resultType = closed.GetGenericArguments()[0];

        return Activator.CreateInstance(
            wrapperDefinition.MakeGenericType(requestType, resultType))!;
    }
}

internal sealed class CommandHandlerWrapper<TCommand, TResult> : RequestHandlerWrapper<TResult>
    where TCommand : ICommand<TResult>
{
    public override Task<TResult> HandleAsync(object request, IServiceProvider services, CancellationToken ct) =>
        ValidateAndHandleAsync<TCommand, ICommandHandler<TCommand, TResult>>(
            request, services, ct,
            static (handler, command, token) => handler.HandleAsync(command, token));
}

internal sealed class QueryHandlerWrapper<TQuery, TResult> : RequestHandlerWrapper<TResult>
    where TQuery : IQuery<TResult>
{
    public override Task<TResult> HandleAsync(object request, IServiceProvider services, CancellationToken ct) =>
        ValidateAndHandleAsync<TQuery, IQueryHandler<TQuery, TResult>>(
            request, services, ct,
            static (handler, query, token) => handler.HandleAsync(query, token));
}

internal abstract class DomainEventWrapper
{
    public abstract Task PublishAsync(IDomainEvent domainEvent, IServiceProvider services, CancellationToken ct);
}

internal sealed class DomainEventWrapper<TDomainEvent> : DomainEventWrapper
    where TDomainEvent : IDomainEvent
{
    public override async Task PublishAsync(
        IDomainEvent domainEvent, IServiceProvider services, CancellationToken ct)
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TDomainEvent>>())
            await handler.HandleAsync((TDomainEvent)domainEvent, ct);
    }
}

internal static class ValidationStep
{
    public static async Task EnsureValidAsync<TRequest>(
        TRequest request, IServiceProvider services, CancellationToken ct)
    {
        ValidationContext<TRequest>? context = null;
        List<ValidationFailure>? failures = null;

        foreach (var validator in services.GetServices<IValidator<TRequest>>())
        {
            // El contexto se crea sólo si hay al menos un validador: la mayoría de
            // queries internas no tienen ninguno y este paso debe salir barato.
            context ??= new ValidationContext<TRequest>(request!);

            var result = await validator.ValidateAsync(context, ct);
            if (result.IsValid) continue;

            failures ??= [];
            failures.AddRange(result.Errors);
        }

        if (failures is not null)
            throw new ValidationException(failures);
    }
}
