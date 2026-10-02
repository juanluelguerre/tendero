using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace ElGuerre.Tendero.AI;

/// <summary>
/// Records one row per call — tokens, cost at the profile's price, latency, outcome
/// — into the log, and lets everything else through untouched: a failure is
/// recorded and thrown on as it came.
/// </summary>
public sealed class MeteringChatClient(
    IChatClient inner, string profileName, ModelProfile profile, AIInvocationLog log, TimeProvider clock)
    : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var started = clock.GetTimestamp();
        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            Record(options, started, response.Usage, AIOutcome.Ok);
            return response;
        }
        catch (Exception failure)
        {
            // A timeout is the HTTP client giving up, not the caller cancelling.
            Record(options, started, null,
                failure is TaskCanceledException or TimeoutException && !cancellationToken.IsCancellationRequested
                    ? AIOutcome.Timeout
                    : AIOutcome.Error);
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Streaming is a non-goal (the spec); metered all the same, from the usage
        // the updates carry.
        var started = clock.GetTimestamp();
        UsageDetails? usage = null;
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (var content in update.Contents.OfType<UsageContent>())
                usage = content.Details;
            yield return update;
        }

        Record(options, started, usage, AIOutcome.Ok);
    }

    private void Record(ChatOptions? options, long started, UsageDetails? usage, AIOutcome outcome)
    {
        var input = usage?.InputTokenCount ?? 0;
        var output = usage?.OutputTokenCount ?? 0;
        log.Record(new AIInvocation(
            Guid.CreateVersion7(clock.GetUtcNow()),
            ModelCall.Read(options, ModelCall.Operation) ?? "unknown",
            profileName,
            options?.ModelId ?? profile.Model ?? profileName,
            ModelCall.Read(options, ModelCall.PromptVersion),
            ModelCall.Read(options, ModelCall.Conversation),
            Activity.Current?.TraceId.ToString(),
            input,
            output,
            profile.CostMicroEuros(input, output),
            (int)clock.GetElapsedTime(started).TotalMilliseconds,
            outcome,
            clock.GetUtcNow()));
    }
}
