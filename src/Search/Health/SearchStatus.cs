namespace ElGuerre.Tendero.Search.Health;

/// <summary>The stages a search runs, in order: its words, their meaning, and the reranking of what both found.</summary>
public enum SearchStage
{
    Words,
    Meaning,
    Reranking,
}

/// <summary>
/// A stage as the shopper gets it: answering, not answering, or switched off in
/// this process — which is a choice, not a failure (invariant 8: words answer alone).
/// </summary>
public enum StageState
{
    Up,
    Down,
    Off,
}

/// <summary>A stage's state since its last change, and why, in the check's words.</summary>
public sealed record StageStatus(SearchStage Stage, StageState State, DateTimeOffset Since, string? Reason);

/// <summary>
/// Search's status for the shopkeeper (R-9, D-7): whether it answers in full,
/// stage by stage, and since when — *"Meaning: down since 11:40 — searches answer
/// with words only"*. Not Grafana: no latency and no memory, only whether a
/// search is whole. Empty until the first round of checks.
/// </summary>
public sealed record SearchStatus(IReadOnlyList<StageStatus> Stages);

/// <summary>
/// The last change of each stage's state, kept for the life of the process. A
/// check that finds a stage as it was changes nothing, so <see cref="StageStatus.Since"/>
/// is when it last changed, not when it was last looked at.
/// </summary>
public sealed class SearchStatusMemory
{
    private readonly Lock gate = new();
    private readonly Dictionary<SearchStage, StageStatus> stages = [];

    public void Record(SearchStage stage, StageState state, string? reason, DateTimeOffset at)
    {
        lock (this.gate)
        {
            if (this.stages.TryGetValue(stage, out var known) && known.State == state)
            {
                // The same state: only the reason may have been refined.
                this.stages[stage] = known with { Reason = reason };
                return;
            }

            this.stages[stage] = new StageStatus(stage, state, at, reason);
        }
    }

    public SearchStatus Current()
    {
        lock (this.gate)
            return new SearchStatus([.. this.stages.Values.OrderBy(status => status.Stage)]);
    }
}
