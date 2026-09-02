using System.Collections.Concurrent;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// A dispatcher over the DI container. The only "middleware" is validation with
/// FluentValidation: anything else (caching, retries, transactions) enters when
/// there is a real case for it, not before.
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
/// Delivers a domain event to every one of its handlers. No filtering and no
/// guaranteed order: if two projections depend on each other, that is a design
/// problem in the slice, not in the dispatcher.
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
// Reflection happens ONCE per request type, and only to build the closed
// wrapper. From there on everything is virtual calls over the generic interface:
// the handler is invoked directly, never through MethodInfo.Invoke, so that an
// exception thrown by the domain reaches the caller unwrapped.

internal abstract class RequestHandlerWrapper<TResult>
{
    public abstract Task<TResult> HandleAsync(object request, IServiceProvider services, CancellationToken ct);

    /// <summary>Validate, resolve the handler, run it. A command and a query
    /// differ only in which interface they resolve, so the rest lives here.</summary>
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
    /// Builds the closed wrapper for the request's concrete type.
    /// <paramref name="requestInterface"/> is ICommand&lt;&gt; or IQuery&lt;&gt;;
    /// TResult comes out of it, so the caller never has to declare it.
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
            // The context is only created when there is at least one validator:
            // most internal queries have none, and this step has to stay cheap.
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
