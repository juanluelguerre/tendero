using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

public sealed class DispatcherTests
{
    private static ServiceProvider BuildProvider() =>
        new ServiceCollection()
            .AddTenderoCqrs(typeof(DispatcherTests).Assembly)
            .BuildServiceProvider();

    [Fact]
    public async Task Dispatching_a_command_runs_its_handler()
    {
        await using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

        var result = await dispatcher.SendAsync(new Greet("Tendero"), TestContext.Current.CancellationToken);

        Assert.Equal("hola, Tendero", result);
    }

    [Fact]
    public async Task Dispatching_a_query_runs_its_handler()
    {
        await using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<IQueryDispatcher>();

        var result = await dispatcher.SendAsync(new CountLetters("abcd"), TestContext.Current.CancellationToken);

        Assert.Equal(4, result);
    }

    [Fact]
    public async Task A_failing_validator_stops_the_request_before_the_handler()
    {
        await using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => dispatcher.SendAsync(new Greet(""), TestContext.Current.CancellationToken));

        Assert.Contains(exception.Errors, failure => failure.PropertyName == nameof(Greet.Name));
    }

    [Fact]
    public async Task A_request_with_no_handler_says_which_handler_is_missing()
    {
        await using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.SendAsync(new Unhandled(), TestContext.Current.CancellationToken));

        Assert.Contains(nameof(Unhandled), exception.Message);
        Assert.Contains("ICommandHandler", exception.Message);
    }

    /// <summary>
    /// The dispatcher is infrastructure: it cannot change the exception a handler
    /// throws. If the domain says InvalidOperationException, that is what the
    /// endpoint has to see — not a TargetInvocationException and nothing wrapped.
    /// </summary>
    [Fact]
    public async Task An_exception_from_the_handler_reaches_the_caller_unwrapped()
    {
        await using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.SendAsync(new Explode(), TestContext.Current.CancellationToken));

        Assert.Equal(ExplodeHandler.Message, exception.Message);
    }

    [Fact]
    public async Task Every_handler_registered_for_an_event_receives_it()
    {
        RecordingHandler.Seen.Clear();

        await using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<IDomainEventDispatcher>();

        await dispatcher.PublishAsync(
            new SomethingHappened(DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);

        Assert.Equal(
            [nameof(RecordingHandler), nameof(SecondRecordingHandler)],
            RecordingHandler.Seen.Order());
    }

    [Fact]
    public async Task An_event_nobody_listens_to_is_not_an_error()
    {
        await using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<IDomainEventDispatcher>();

        await dispatcher.PublishAsync(
            new NobodyListens(DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Scanning_the_same_assembly_twice_does_not_duplicate_handlers()
    {
        // The host can rebuild the container (tests, a reload); registering twice
        // cannot turn one handler into two executions.
        await using var provider = new ServiceCollection()
            .AddTenderoCqrs(typeof(DispatcherTests).Assembly)
            .AddTenderoCqrs(typeof(DispatcherTests).Assembly)
            .BuildServiceProvider();

        Assert.Single(provider.GetServices<ICommandHandler<Greet, string>>());
        Assert.Single(provider.GetServices<IValidator<Greet>>());
    }
}
