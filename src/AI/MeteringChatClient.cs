using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using ElGuerre.Tendero.SharedKernel;
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
    private static readonly Meter Meter = new(TelemetrySources.AI);

    /// <summary>`tendero.ai.calls` (the tracking plan): model calls by what they were
    /// for, who answered and how it ended — `ai-usage.json`'s errors and its mix.</summary>
    private static readonly Counter<long> Calls = Meter.CreateCounter<long>(
        "tendero.ai.calls", unit: "{call}", description: "Model calls, by operation, profile, model and outcome.");

    /// <summary>`tendero.ai.cost`: what they cost at the profile's price of that
    /// moment, in micro-euros — the dashboard's cost per day and per model.</summary>
    private static readonly Counter<long> Cost = Meter.CreateCounter<long>(
        "tendero.ai.cost", unit: "{micro_euro}", description: "What model calls cost at their profile's price, in micro-euros.");

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
        var operation = ModelCall.Read(options, ModelCall.Operation) ?? "unknown";
        var model = options?.ModelId ?? profile.Model ?? profileName;
        var cost = profile.CostMicroEuros(input, output);

        // Never the conversation as a label: a series per conversation is a
        // cardinality explosion, and the rows already carry it.
        TagList labels = new()
        {
            { "operation", operation },
            { "profile", profileName },
            { "model", model },
            { "outcome", MetricLabels.Of(outcome) },
        };
        Calls.Add(1, labels);
        if (cost > 0)
            Cost.Add(cost, labels);

        log.Record(new AIInvocation(
            Guid.CreateVersion7(clock.GetUtcNow()),
            operation,
            profileName,
            model,
            ModelCall.Read(options, ModelCall.PromptVersion),
            ModelCall.Read(options, ModelCall.Conversation),
            Activity.Current?.TraceId.ToString(),
            input,
            output,
            cost,
            (int)clock.GetElapsedTime(started).TotalMilliseconds,
            outcome,
            clock.GetUtcNow(),
            ModelCall.Read(options, ModelCall.Person)));
    }
}
