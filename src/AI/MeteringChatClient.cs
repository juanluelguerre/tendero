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
            Record(options, started, null, OutcomeOf(failure, cancellationToken));
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Streaming is a non-goal (the spec), metered all the same. By hand, so a
        // stream that breaks, or one the caller stops reading, still leaves its row.
        var started = clock.GetTimestamp();
        UsageDetails? usage = null;
        var outcome = AIOutcome.Cancelled;   // left half read, unless it ends or fails
        await using var updates = base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                ChatResponseUpdate update;
                try
                {
                    if (!await updates.MoveNextAsync())
                        break;
                    update = updates.Current;
                }
                catch (Exception failure)
                {
                    outcome = OutcomeOf(failure, cancellationToken);
                    throw;
                }

                foreach (var content in update.Contents.OfType<UsageContent>())
                    usage = content.Details;
                yield return update;
            }

            outcome = AIOutcome.Ok;
        }
        finally
        {
            Record(options, started, usage, outcome);
        }
    }

    /// <summary>The caller giving up is Cancelled; the HTTP client giving up is a
    /// Timeout; anything else is the model or the network failing.</summary>
    private static AIOutcome OutcomeOf(Exception failure, CancellationToken cancellationToken) => failure switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => AIOutcome.Cancelled,
        TaskCanceledException or TimeoutException => AIOutcome.Timeout,
        _ => AIOutcome.Error,
    };

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
