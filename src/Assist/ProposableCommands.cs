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

    public IEnumerable<IProposableCommand> All => this.entries.Values;
}

public static class ProposalLimits
{
    /// <summary>"A hundred English names, each reviewed" (the spec): one batch, for
    /// proposing, approving and rejecting alike.</summary>
    public const int MaxBatch = 100;

    /// <summary>Proposals still waiting that a job looks through to see what it has
    /// already proposed. The catalogue is a hundred products; past this many waiting,
    /// the look becomes a query of its own.</summary>
    public const int MaxOpenScan = 500;
}
