using System.Collections.Concurrent;
using System.Reflection;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
            static type => RequestHandlerWrapper.Create(type, typeof(ICommand<>), typeof(ICommandHandler<,>)));

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
            static type => RequestHandlerWrapper.Create(type, typeof(IQuery<>), typeof(IQueryHandler<,>)));

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

// ---------- Plumbing: reflexión una sola vez por tipo, luego llamadas virtuales ----------

internal abstract class RequestHandlerWrapper<TResult>
{
    public abstract Task<TResult> HandleAsync(object request, IServiceProvider services, CancellationToken ct);
}

internal static class RequestHandlerWrapper
{
    /// <summary>
    /// Construye el wrapper cerrado para el tipo concreto de la petición.
    /// <paramref name="requestInterface"/> es ICommand&lt;&gt; o IQuery&lt;&gt;;
    /// de ahí se saca TResult sin que el llamante tenga que declararlo.
    /// </summary>
    public static object Create(Type requestType, Type requestInterface, Type handlerInterface)
    {
        var closed = Array.Find(
            requestType.GetInterfaces(),
            i => i.IsGenericType && i.GetGenericTypeDefinition() == requestInterface)
            ?? throw new InvalidOperationException(
                $"{requestType.Name} does not implement {requestInterface.Name}.");

        var resultType = closed.GetGenericArguments()[0];

        return Activator.CreateInstance(
            typeof(RequestHandlerWrapper<,>).MakeGenericType(requestType, resultType),
            handlerInterface)!;
    }
}

internal sealed class RequestHandlerWrapper<TRequest, TResult>(Type handlerInterface)
    : RequestHandlerWrapper<TResult>
{
    private readonly Type _handlerType = handlerInterface.MakeGenericType(typeof(TRequest), typeof(TResult));

    public override async Task<TResult> HandleAsync(
        object request, IServiceProvider services, CancellationToken ct)
    {
        var typed = (TRequest)request;

        // Paso de validación: FluentValidation antes del handler, siempre.
        await ValidationStep.EnsureValidAsync(typed, services, ct);

        var handler = services.GetService(_handlerType)
            ?? throw new InvalidOperationException(
                $"No handler registered for {typeof(TRequest).Name}. " +
                $"Expected an implementation of {_handlerType.Name}.");

        return await HandlerInvoker<TRequest, TResult>.Invoke(handler, typed, ct);
    }
}

/// <summary>
/// Puente entre el handler resuelto (object) y su interfaz genérica. Command y
/// query tienen la misma forma de método, así que un único delegado cacheado sirve.
/// </summary>
internal static class HandlerInvoker<TRequest, TResult>
{
    private static readonly ConcurrentDictionary<Type, Func<object, TRequest, CancellationToken, Task<TResult>>>
        Invokers = new();

    public static Task<TResult> Invoke(object handler, TRequest request, CancellationToken ct)
    {
        var invoker = Invokers.GetOrAdd(handler.GetType(), static type =>
        {
            var method = type.GetMethod("HandleAsync", BindingFlags.Public | BindingFlags.Instance,
                [typeof(TRequest), typeof(CancellationToken)])
                ?? throw new InvalidOperationException($"{type.Name} has no HandleAsync method.");

            return (h, r, token) => (Task<TResult>)method.Invoke(h, [r, token])!;
        });

        return invoker(handler, request, ct);
    }
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
        var validators = services.GetServices<IValidator<TRequest>>() as IValidator<TRequest>[]
                         ?? [.. services.GetServices<IValidator<TRequest>>()];
        if (validators.Length == 0)
            return;

        var context = new ValidationContext<TRequest>(request!);
        List<ValidationFailure>? failures = null;

        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, ct);
            if (result.IsValid) continue;

            failures ??= [];
            failures.AddRange(result.Errors);
        }

        if (failures is { Count: > 0 })
            throw new ValidationException(failures);
    }
}
