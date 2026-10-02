using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Assist;

/// <summary>
/// The closed vocabulary (phase 12): every command some context registered as
/// proposable, found by its stable name. What is not here cannot be proposed.
/// </summary>
public sealed class ProposableCommands
{
    private readonly Dictionary<string, IProposableCommand> entries;

    public ProposableCommands(IEnumerable<IProposableCommand> entries)
    {
        this.entries = [];
        foreach (var entry in entries)
        {
            if (!this.entries.TryAdd(entry.Name, entry))
                throw new InvalidOperationException($"Two proposable commands are named {entry.Name}.");
        }
    }

    public IProposableCommand? Find(string name) => this.entries.GetValueOrDefault(name);
}

public static class ProposalLimits
{
    /// <summary>"A hundred English names, each reviewed" (the spec): one batch, for
    /// proposing, approving and rejecting alike.</summary>
    public const int MaxBatch = 100;
}
