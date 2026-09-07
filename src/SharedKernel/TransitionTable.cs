namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// A declarative state machine: for every state, the states it may move to.
///
/// <c>Order.AllowedTransitions</c> established the idiom and three more
/// aggregates copied it — <c>Cart</c>, <c>ReturnRequest</c> and
/// <c>Reservation</c> — each with its own three-line guard and its own wording
/// for the same refusal. The table is still declared where the aggregate lives,
/// because reading the edges beside the methods that walk them is the whole
/// point of the idiom; only the guard and the message live here, once.
///
/// <para>
/// It is initialised with the indexer, so a table reads as the list of edges it
/// is: <c>[Pending] = [PaymentAuthorized, Cancelled]</c>. A state with no row is
/// an error the first time it is asked about, with a message that names the
/// state rather than the <c>KeyNotFoundException</c> a dictionary would throw.
/// </para>
/// </summary>
public sealed class TransitionTable<TStatus>
    where TStatus : struct, Enum
{
    private readonly Dictionary<TStatus, TStatus[]> edges = [];

    /// <summary>The states reachable from <paramref name="from"/>. Empty for a terminal state.</summary>
    public IReadOnlyList<TStatus> this[TStatus from]
    {
        get => this.edges.TryGetValue(from, out var targets)
            ? targets
            : throw new InvalidOperationException(
                $"No transitions are declared from {typeof(TStatus).Name}.{from}.");
        init => this.edges[from] = [.. value];
    }

    public bool Allows(TStatus from, TStatus to) => this[from].Contains(to);

    /// <summary>
    /// The guard every aggregate used to write by hand. <paramref name="subject"/>
    /// names the instance — <c>"order 0199…"</c> — so the message says which
    /// thing refused, not only which edge is missing.
    /// </summary>
    public void EnsureAllowed(TStatus from, TStatus to, string subject)
    {
        if (!Allows(from, to))
            throw new InvalidOperationException($"Illegal transition {from} -> {to} for {subject}.");
    }
}
