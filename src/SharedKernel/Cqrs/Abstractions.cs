namespace ElGuerre.Tendero.SharedKernel;

// Las abstracciones viven en el namespace raíz a propósito: un slice sólo
// necesita `using ElGuerre.Tendero.SharedKernel;` para escribir su comando y su handler.
// Hechas a mano por política de licencias (CLAUDE.md): NO MediatR.

/// <summary>Intención de escritura que devuelve un resultado.</summary>
public interface ICommand<TResult>;

/// <summary>Intención de lectura. Separada de ICommand para que el dispatcher
/// pueda tener políticas distintas (caché, réplicas de lectura) sin tocar slices.</summary>
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
/// Consumidor de un evento de dominio. Lo invoca el procesador del Outbox
/// (worker), nunca el request HTTP: ver CLAUDE.md, invariante 7.
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
/// Publica un evento de dominio a todos sus <see cref="IDomainEventHandler{T}"/>.
/// El procesador del Outbox rehidrata el evento y lo entrega por aquí.
/// </summary>
public interface IDomainEventDispatcher
{
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
