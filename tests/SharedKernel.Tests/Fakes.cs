using FluentValidation;

namespace Tendero.SharedKernel.Tests;

// Fakes deterministas en vez de mocks (docs/testing.md): lo que se prueba aquí
// es el cableado del dispatcher, y un fake dice qué pasó sin ceremonia.

internal sealed record Greet(string Name) : ICommand<string>;

internal sealed class GreetHandler : ICommandHandler<Greet, string>
{
    public Task<string> HandleAsync(Greet command, CancellationToken cancellationToken) =>
        Task.FromResult($"hola, {command.Name}");
}

internal sealed class GreetValidator : AbstractValidator<Greet>
{
    public GreetValidator() => RuleFor(x => x.Name).NotEmpty();
}

internal sealed record CountLetters(string Text) : IQuery<int>;

internal sealed class CountLettersHandler : IQueryHandler<CountLetters, int>
{
    public Task<int> HandleAsync(CountLetters query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Text.Length);
}

/// <summary>Comando cuyo handler revienta: sirve para comprobar que la excepción
/// del dominio llega al llamante tal cual, sin envoltorios de reflexión.</summary>
internal sealed record Explode : ICommand<string>;

internal sealed class ExplodeHandler : ICommandHandler<Explode, string>
{
    public const string Message = "the aggregate said no";

    public Task<string> HandleAsync(Explode command, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(Message);
}

internal sealed record Unhandled : ICommand<string>;

internal sealed record SomethingHappened(DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed class RecordingHandler : IDomainEventHandler<SomethingHappened>
{
    public static readonly List<string> Seen = [];

    public Task HandleAsync(SomethingHappened domainEvent, CancellationToken cancellationToken)
    {
        Seen.Add(nameof(RecordingHandler));
        return Task.CompletedTask;
    }
}

internal sealed class SecondRecordingHandler : IDomainEventHandler<SomethingHappened>
{
    public Task HandleAsync(SomethingHappened domainEvent, CancellationToken cancellationToken)
    {
        RecordingHandler.Seen.Add(nameof(SecondRecordingHandler));
        return Task.CompletedTask;
    }
}

internal sealed record NobodyListens(DateTimeOffset OccurredAt) : IDomainEvent;
