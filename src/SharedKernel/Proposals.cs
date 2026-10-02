namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// A proposal's identity. In the kernel and not in Assist because the audit log
/// carries it: a command run for an approved proposal writes this id into its
/// row, which is what lets the log say who proposed a change and who approved it.
/// </summary>
public readonly record struct ProposalId(Guid Value)
{
    public static ProposalId New(DateTimeOffset at) => new(Guid.CreateVersion7(at));

    public override string ToString() => Value.ToString();
}

/// <summary>
/// The proposal the commands of this DI scope run for, read by the audit step.
///
/// Approving runs each proposal in a scope of its own, so a scope carries one
/// proposal and a second one is refused: a row filed under the wrong proposal
/// would name the wrong approver for a change.
/// </summary>
public sealed class ProposalScope
{
    public ProposalId? Current { get; private set; }

    public void Enter(ProposalId id)
    {
        if (Current is { } current && current != id)
            throw new InvalidOperationException($"This scope already runs proposal {current}.");

        Current = id;
    }
}
