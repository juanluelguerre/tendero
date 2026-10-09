using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Assist.Domain;

public enum ProposalStatus
{
    Proposed,

    /// <summary>A person said yes and the command is running — or the process died
    /// while it ran, and the list shows "approved, result unknown".</summary>
    Approved,

    Applied,
    Failed,
    Rejected,

    /// <summary>What the command would change changed after it was proposed.</summary>
    Stale,
}

/// <summary>Where a proposal came from. Who asked is the principal's name, never
/// a value from the request.</summary>
/// <param name="Proposer">What proposed it (<see cref="Proposers"/>): Steward in a
/// conversation, or a job. Null on proposals made before a second proposer
/// existed, which were all Steward's.</param>
public sealed record ProposalProvenance(
    string? Profile,
    string? Model,
    string? PromptVersion,
    string? ConversationId,
    string? TraceId,
    string AskedBy,
    string? Proposer = null);

/// <summary>
/// What may propose (phase 13a): a closed set of names, stored on the proposal so
/// the review says whose it is. A person reads its words in their language; this
/// is the name those words are filed under.
/// </summary>
public static class Proposers
{
    /// <summary>The backoffice copilot, in a conversation.</summary>
    public const string Steward = "Steward";

    /// <summary>The job that reads product pictures and describes them.</summary>
    public const string AltTextJob = "AltTextJob";

    /// <summary>Whose a proposal is, for one stored before the name was.</summary>
    public static string Of(ProposalProvenance provenance) => provenance.Proposer ?? Steward;
}

/// <summary>
/// A command a model proposed, waiting for a person (phase 12). It carries values,
/// never a reference into another context (invariant 1): the command's registry
/// name, its payload as JSON, the subject's code, and a snapshot of what the
/// command would change as it was when proposed — which is how approving finds
/// it stale.
/// </summary>
public sealed class ActionProposal : AggregateRoot
{
    private static readonly TransitionTable<ProposalStatus> AllowedTransitions = new()
    {
        [ProposalStatus.Proposed] = [ProposalStatus.Approved, ProposalStatus.Rejected, ProposalStatus.Stale],
        [ProposalStatus.Approved] = [ProposalStatus.Applied, ProposalStatus.Failed],
        [ProposalStatus.Applied] = [],
        [ProposalStatus.Failed] = [],
        [ProposalStatus.Rejected] = [],
        [ProposalStatus.Stale] = [],
    };

    /// <summary>What the result column holds. A reason longer than this is cut, so
    /// recording a failure can never fail itself.</summary>
    public const int ResultMaxLength = 1000;

    /// <summary>
    /// How long an approval may take before it is presumed dead. An approved row
    /// runs one command in the same request; one approved longer ago than this
    /// left no result because its process died, and may be finished again. One
    /// approved more recently may still be running in another request — a double
    /// click, a second tab — and running it again would run its command twice.
    /// </summary>
    public static readonly TimeSpan StillRunning = TimeSpan.FromMinutes(5);

    private ActionProposal() { }

    public ProposalId Id { get; private set; }
    public string Command { get; private set; } = null!;
    public string Payload { get; private set; } = null!;

    /// <summary>
    /// The payload as it was proposed, kept when a person corrected its words before
    /// approving (phase 13a); null when what was approved is what was proposed. An
    /// edit is what a prompt is judged against as much as a rejection is.
    /// </summary>
    public string? ProposedPayload { get; private set; }

    /// <summary>Whether a person corrected it before approving.</summary>
    public bool Edited => ProposedPayload is not null;
    public string Subject { get; private set; } = null!;
    public string? Before { get; private set; }

    /// <summary>What a person reads of it, as its owning context wrote it when it was
    /// proposed (<see cref="ProposalDisplay"/>, JSON); null on proposals made before S-4.</summary>
    public string? Display { get; private set; }
    public string Reason { get; private set; } = null!;
    public ProposalProvenance Provenance { get; private set; } = null!;
    public DateTimeOffset ProposedAt { get; private set; }
    public ProposalStatus Status { get; private set; }
    public string? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Why it failed, or why it was rejected; null otherwise.</summary>
    public string? Result { get; private set; }

    /// <summary>The stored row's version, set by the store on every write: two
    /// decisions taken on one version cannot both be saved.</summary>
    public uint Version { get; private set; }

    public static ActionProposal Propose(
        string command, string payload, string subject, string? before, string reason,
        ProposalProvenance provenance, TimeProvider clock, string? display = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(provenance);

        var at = clock.GetUtcNow();
        return new ActionProposal
        {
            Id = ProposalId.New(at),
            Command = command,
            Payload = payload,
            Subject = subject,
            Before = before,
            Display = display,
            Reason = reason,
            Provenance = provenance,
            ProposedAt = at,
            Status = ProposalStatus.Proposed,
        };
    }

    public void Approve(string by, TimeProvider clock) => Decide(ProposalStatus.Approved, by, clock);

    /// <summary>
    /// The person's words in place of the model's, before the approval that applies
    /// them: only while it still waits, and what the model wrote stays beside it.
    /// </summary>
    /// <param name="display">What a person reads of it now, as its context writes it for the new payload.</param>
    public void Amend(string payload, string? display)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        if (Status != ProposalStatus.Proposed)
            throw new InvalidOperationException($"Proposal {Id} is {Status}: only one still waiting is corrected.");

        ProposedPayload ??= Payload;
        Payload = payload;
        Display = display ?? Display;
    }

    public void Reject(string by, string reason, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Decide(ProposalStatus.Rejected, by, clock);
        Result = reason;
    }

    public void MarkStale(string by, TimeProvider clock) => Decide(ProposalStatus.Stale, by, clock);

    /// <summary>Approved, with no result, for longer than an approval can take.</summary>
    public bool MayRetry(TimeProvider clock) =>
        Status == ProposalStatus.Approved && DecidedAt <= clock.GetUtcNow() - StillRunning;

    /// <summary>Takes over an approval that died: whoever finishes it is its
    /// approver now, and the new moment keeps a second retry from running at once.</summary>
    public void Retry(string by, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        if (!MayRetry(clock))
            throw new InvalidOperationException($"Proposal {Id} is {Status}, approved at {DecidedAt}; it cannot be retried yet.");

        DecidedBy = by;
        DecidedAt = clock.GetUtcNow();
    }

    public void RecordApplied() => Move(ProposalStatus.Applied);

    public void RecordFailed(string reason)
    {
        Move(ProposalStatus.Failed);
        Result = string.IsNullOrWhiteSpace(reason) ? "The command failed without saying why."
            : reason.Length > ResultMaxLength ? reason[..ResultMaxLength]
            : reason;
    }

    private void Decide(ProposalStatus to, string by, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        Move(to);
        DecidedBy = by;
        DecidedAt = clock.GetUtcNow();
    }

    private void Move(ProposalStatus to)
    {
        AllowedTransitions.EnsureAllowed(Status, to, $"proposal {Id}");
        Status = to;
    }
}
