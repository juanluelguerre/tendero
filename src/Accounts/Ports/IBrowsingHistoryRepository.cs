using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Accounts.Ports;

/// <summary>A customer's trail as an aggregate, for the commands that change it (R-8).</summary>
public interface IBrowsingHistoryRepository
{
    Task<BrowsingHistory?> FindAsync(CustomerId customer, CancellationToken ct = default);

    void Add(BrowsingHistory history);

    /// <summary>The whole trail gone, when the account is removed (P15-9).</summary>
    void Remove(BrowsingHistory history);
}

/// <summary>
/// A customer's trail as the page reads it: what is kept now, most recent first,
/// and whether it is on. A read, untracked, never saved back — a port of its own
/// and an adapter of its own (the backlog's "a repository is not a reader").
/// </summary>
public interface IBrowsingHistoryReader
{
    /// <summary>Null for a customer with no trail yet — on, and empty, as far as anybody can tell.</summary>
    Task<BrowsingTrail?> ForCustomerAsync(CustomerId customer, DateTimeOffset now, CancellationToken ct = default);
}

/// <summary>What is kept of a trail at a moment: nothing past its retention, most recent first.</summary>
public sealed record BrowsingTrail(bool IsEnabled, IReadOnlyList<TrailEntry> Views, IReadOnlyList<TrailEntry> Searches)
{
    public static readonly BrowsingTrail Empty = new(true, [], []);

    public static BrowsingTrail Of(BrowsingHistory history, DateTimeOffset now) =>
        new(history.IsEnabled, history.Kept(BrowsingKind.View, now), history.Kept(BrowsingKind.Search, now));
}

/// <summary>
/// The promise that a trail is forgotten after <see cref="BrowsingHistory.Retention"/>
/// kept for customers who never come back: a customer who returns has their old
/// entries dropped on the next write and never read, but one who does not return
/// would keep them in the table forever without this sweep.
/// </summary>
public interface IBrowsingRetention
{
    /// <summary>Removes every entry that happened before <paramref name="cutoff"/>; answers how many trails changed.</summary>
    Task<int> ForgetBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
