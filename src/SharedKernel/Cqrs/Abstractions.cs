namespace ElGuerre.Tendero.SharedKernel;

// The abstractions live in the root namespace on purpose: a slice only needs
// `using ElGuerre.Tendero.SharedKernel;` to write its command and its handler.
// Hand-rolled by licensing policy (CLAUDE.md): NO MediatR.

/// <summary>A write intent that returns a result.</summary>
public interface ICommand<TResult>;

/// <summary>A read intent. Kept separate from ICommand so the dispatcher can
/// have different policies (caching, read replicas) without touching slices.</summary>
public interface IQuery<TResult>;

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// A consumer of a domain event. The Outbox processor invokes it (in the
/// worker), never the HTTP request: see CLAUDE.md, invariant 7.
/// </summary>
public interface IDomainEventHandler<in TDomainEvent>
    where TDomainEvent : IDomainEvent
{
    Task HandleAsync(TDomainEvent domainEvent, CancellationToken cancellationToken);
}

public interface ICommandDispatcher
{
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);
}

public interface IQueryDispatcher
{
    Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publishes a domain event to every <see cref="IDomainEventHandler{T}"/>.
/// The Outbox processor rehydrates the event and delivers it through here.
/// </summary>
public interface IDomainEventDispatcher
{
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
