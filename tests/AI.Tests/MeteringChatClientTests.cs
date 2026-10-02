using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.AI;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

/// <summary>One row per model call, whatever the call did — the cost line of a
/// conversation and the Grafana panel are both sums of these rows.</summary>
[Collection(nameof(MeteringChatClientTests))]
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

    /// <summary>
    /// The same call as a metric, for `ai-usage.json` (S-6): counted and costed by
    /// what it was for and who answered — never by conversation, which would make
    /// a series per conversation.
    /// </summary>
    [Fact]
    public async Task A_call_is_counted_and_costed_by_operation_profile_model_and_outcome()
    {
        using var heard = new InstrumentRecorder("tendero.ai.calls", "tendero.ai.cost");
        var chat = Metered(new Answering(new UsageDetails { InputTokenCount = 1500, OutputTokenCount = 342 }), new AIInvocationLog(),
            new ModelProfile { Model = "gemini-2.5-flash", InputPrice = 0.30m, OutputPrice = 2.50m });

        await chat.GetResponseAsync("hola", ModelCall.Options("steward.turn", "conv-1", "steward-v1"), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<HttpRequestException>(() => Metered(new Failing(new HttpRequestException("503")), new AIInvocationLog(),
            new ModelProfile { Model = "m" }).GetResponseAsync("hola", cancellationToken: TestContext.Current.CancellationToken));

        var calls = heard.Of("tendero.ai.calls");
        Assert.Equal(
            [("steward.turn", "gemini", "gemini-2.5-flash", "ok"), ("unknown", "gemini", "m", "error")],
            calls.Select(tags => ((string)tags["operation"]!, (string)tags["profile"]!, (string)tags["model"]!, (string)tags["outcome"]!)));
        Assert.All(calls, tags => Assert.DoesNotContain("conversation", tags.Keys));
        Assert.Equal(1305L, heard.Sum("tendero.ai.cost"));
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

    /// <summary>A shopkeeper closing the panel is not a model failing: the call is
    /// recorded as Cancelled, so the error rate stays the model's (M-1).</summary>
    [Fact]
    public async Task A_call_the_caller_cancelled_is_recorded_as_cancelled()
    {
        var log = new AIInvocationLog();
        using var closed = new CancellationTokenSource();
        await closed.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Metered(new Failing(new TaskCanceledException()), log, new ModelProfile { Model = "m" })
            .GetResponseAsync("hola", cancellationToken: closed.Token));

        Assert.True(log.Reader.TryRead(out var row));
        Assert.Equal(AIOutcome.Cancelled, row.Outcome);
    }

    /// <summary>A stream that breaks still leaves its row (M-2).</summary>
    [Fact]
    public async Task A_stream_that_fails_is_recorded()
    {
        var log = new AIInvocationLog();
        var chat = Metered(new Streaming(fail: true), log, new ModelProfile { Model = "m" });

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in chat.GetStreamingResponseAsync("hola", cancellationToken: TestContext.Current.CancellationToken)) { }
        });

        Assert.True(log.Reader.TryRead(out var row));
        Assert.Equal(AIOutcome.Error, row.Outcome);
    }

    /// <summary>And so does a stream the caller stopped reading (M-2).</summary>
    [Fact]
    public async Task A_stream_left_half_read_is_recorded()
    {
        var log = new AIInvocationLog();
        var chat = Metered(new Streaming(fail: false), log, new ModelProfile { Model = "m" });

        await foreach (var _ in chat.GetStreamingResponseAsync("hola", cancellationToken: TestContext.Current.CancellationToken))
            break;

        Assert.True(log.Reader.TryRead(out var row));
        Assert.Equal(AIOutcome.Cancelled, row.Outcome);
    }

    [Fact]
    public async Task A_stream_read_to_the_end_is_recorded_with_its_usage()
    {
        var log = new AIInvocationLog();
        var chat = Metered(new Streaming(fail: false), log, new ModelProfile { Model = "m" });

        await foreach (var _ in chat.GetStreamingResponseAsync("hola", cancellationToken: TestContext.Current.CancellationToken)) { }

        Assert.True(log.Reader.TryRead(out var row));
        Assert.Equal((AIOutcome.Ok, 7L, 2L), (row.Outcome, row.InputTokens, row.OutputTokens));
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

    private sealed class Streaming(bool fail) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ho");
            await Task.Yield();
            if (fail)
                throw new HttpRequestException("503");
            yield return new ChatResponseUpdate(ChatRole.Assistant, "la");
            yield return new ChatResponseUpdate { Contents = [new UsageContent(new UsageDetails { InputTokenCount = 7, OutputTokenCount = 2 })] };
        }

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
