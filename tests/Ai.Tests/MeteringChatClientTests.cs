using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.AI;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

/// <summary>One row per model call, whatever the call did — the cost line of a
/// conversation and the Grafana panel are both sums of these rows.</summary>
public sealed class MeteringChatClientTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public async Task A_call_leaves_a_row_with_its_tokens_cost_operation_and_model()
    {
        var log = new AIInvocationLog();
        var chat = Metered(new Answering(new UsageDetails { InputTokenCount = 1500, OutputTokenCount = 342 }), log,
            new ModelProfile { Model = "gemini-2.5-flash", InputPrice = 0.30m, OutputPrice = 2.50m });

        await chat.GetResponseAsync("hola", ModelCall.Options("steward.turn", "conv-1", "steward-v1"), TestContext.Current.CancellationToken);

        Assert.True(log.Reader.TryRead(out var row));
        Assert.Equal(("steward.turn", "gemini", "gemini-2.5-flash", "conv-1", "steward-v1"), (row.Operation, row.Profile, row.Model, row.ConversationId, row.PromptVersion));
        Assert.Equal((1500L, 342L, 1305L, AIOutcome.Ok), (row.InputTokens, row.OutputTokens, row.CostMicroEuros, row.Outcome));
    }

    [Fact]
    public async Task A_model_that_reports_no_usage_costs_nothing_and_breaks_nothing()
    {
        var log = new AIInvocationLog();

        await Metered(new Answering(null), log, new ModelProfile { Model = "m", InputPrice = 1 })
            .GetResponseAsync("hola", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(log.Reader.TryRead(out var row));
        Assert.Equal((0L, 0L, 0L, "unknown"), (row.InputTokens, row.OutputTokens, row.CostMicroEuros, row.Operation));
    }

    [Fact]
    public async Task A_call_that_fails_is_recorded_and_still_thrown()
    {
        var log = new AIInvocationLog();

        await Assert.ThrowsAsync<HttpRequestException>(() => Metered(new Failing(new HttpRequestException("503")), log, new ModelProfile { Model = "m" })
            .GetResponseAsync("hola", cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(log.Reader.TryRead(out var row));
        Assert.Equal(AIOutcome.Error, row.Outcome);
    }

    [Fact]
    public async Task A_call_that_times_out_is_recorded_as_a_timeout()
    {
        var log = new AIInvocationLog();

        await Assert.ThrowsAsync<TaskCanceledException>(() => Metered(new Failing(new TaskCanceledException("timeout")), log, new ModelProfile { Model = "m" })
            .GetResponseAsync("hola", cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(log.Reader.TryRead(out var row));
        Assert.Equal(AIOutcome.Timeout, row.Outcome);
    }

    private static IChatClient Metered(IChatClient inner, AIInvocationLog log, ModelProfile profile) =>
        new MeteringChatClient(inner, "gemini", profile, log, Clock);

    private sealed class Answering(UsageDetails? usage) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "hola")) { Usage = usage });

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class Failing(Exception failure) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromException<ChatResponse>(failure);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
