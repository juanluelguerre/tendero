namespace ElGuerre.Tendero.AI;

public enum AIOutcome
{
    Ok,
    Error,
    Timeout,

    /// <summary>The caller stopped waiting — a panel closed, a stream left half
    /// read. Not the model failing, so not counted as an error.</summary>
    Cancelled,
}

/// <summary>
/// One model call: what it was for, which profile and model answered, what it
/// cost at the profile's price of that moment, how long it took, how it ended.
/// No prompt and no answer — conversations are not kept (Steward spec, decision 6).
/// </summary>
public sealed record AIInvocation(
    Guid Id,
    string Operation,
    string Profile,
    string Model,
    string? PromptVersion,
    string? ConversationId,
    string? TraceId,
    long InputTokens,
    long OutputTokens,
    long CostMicroEuros,
    int LatencyMs,
    AIOutcome Outcome,
    DateTimeOffset At);

/// <summary>What the rows say back: a conversation's tokens so far, which a turn's
/// budget is checked against (S-3b). Rows land shortly after their call, through
/// the log's worker, so the figure trails the last call by moments.</summary>
public interface IAIInvocationReader
{
    Task<long> ConversationTokensAsync(string conversationId, CancellationToken cancellationToken = default);
}

/// <summary>Where the rows go: Persistence, on a context of its own, as the audit.</summary>
public interface IAIInvocationWriter
{
    Task WriteAsync(IReadOnlyList<AIInvocation> rows, CancellationToken cancellationToken = default);
}
