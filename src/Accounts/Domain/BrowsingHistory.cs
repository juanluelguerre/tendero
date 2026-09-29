using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Accounts.Domain;

/// <summary>What a trail entry is: a product somebody viewed, or something they searched for. A closed set (ADR 0043).</summary>
public enum BrowsingKind
{
    View,
    Search,
}

/// <summary>
/// One step of the trail: a product's public code, or the words of a search, and
/// when it happened. A value — the same product viewed twice is one entry, at
/// the later time.
/// </summary>
public sealed record TrailEntry(string Key, DateTimeOffset At);

/// <summary>
/// A signed-in customer's trail (R-8, *Sigue donde lo dejaste*): the products they
/// viewed and the searches they made, kept by Accounts so it follows them to
/// every device — the way Amazon keeps it, as the author asked.
///
/// **It is personal data, and the rules that make it tolerable are here, not in
/// a screen.** It keeps the last <see cref="MaxViews"/> products and
/// <see cref="MaxSearches"/> searches, each once, most recent first; anything
/// older than <see cref="Retention"/> is gone — filtered when read, dropped when
/// written, and swept for customers who never come back
/// (<c>ForgetOldBrowsingWorker</c>). The customer can remove one entry, clear it
/// all, and switch it off — after which nothing is recorded and what was kept is
/// cleared, not merely hidden. docs/privacy.md says the same for a reader.
///
/// **Codes and words, never a product.** A viewed product is its public code, a
/// value (ADR 0014): Accounts never learns what the product is, and a product
/// withdrawn since is simply not drawn by the page that resolves it.
///
/// One per customer, keyed by the customer: the trail has no identity of its
/// own, and a customer with two would be a person with two memories of the same
/// afternoon.
/// </summary>
public sealed class BrowsingHistory : AggregateRoot
{
    /// <summary>The spec's cap: the row draws eight, *Tu historial* lists the rest.</summary>
    public const int MaxViews = 50;

    public const int MaxSearches = 20;

    /// <summary>Forgotten after ninety days (the spec) — a season's shopping, not a profile.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    /// <summary>A product code as the catalogue writes it: letters and digits.</summary>
    public const int MaxCodeLength = 20;

    /// <summary>The search box's own limit (<c>SearchProductsValidator</c>).</summary>
    public const int MaxSearchLength = 200;

    private List<TrailEntry> views = [];
    private List<TrailEntry> searches = [];

    public CustomerId CustomerId { get; private set; }

    /// <summary>False once the customer switched it off: nothing is recorded until they switch it on.</summary>
    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private BrowsingHistory() { } // EF Core

    /// <summary>A customer's trail before anything was recorded: on, and empty.</summary>
    public static BrowsingHistory StartFor(CustomerId customer, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return new BrowsingHistory { CustomerId = customer, IsEnabled = true, CreatedAt = now, UpdatedAt = now };
    }

    /// <summary>What is kept of one kind at <paramref name="now"/>, most recent first — never anything past <see cref="Retention"/>, stored or not.</summary>
    public IReadOnlyList<TrailEntry> Kept(BrowsingKind kind, DateTimeOffset now) =>
        [.. Of(kind).Where(entry => entry.At > now - Retention).OrderByDescending(entry => entry.At)];

    /// <summary>A product viewed or a search made, now. Nothing, and false, when the trail is off or the key is blank.</summary>
    public bool Record(TimeProvider clock, BrowsingKind kind, string key) =>
        Merge(clock, kind, [new TrailEntry(key, clock.GetUtcNow())]) > 0;

    /// <summary>
    /// Entries that happened elsewhere — the browser's guest trail, sent once at
    /// sign-in — folded into this one: each key once at its latest time, the caps
    /// and the retention applied as if they had been recorded here. A time from
    /// the future is the browser's clock, not a fact, and counts as now.
    /// Returns how many arrived and were kept; nothing is kept while the trail is off.
    /// </summary>
    public int Merge(TimeProvider clock, BrowsingKind kind, IEnumerable<TrailEntry> arriving)
    {
        if (!IsEnabled)
            return 0;

        var now = clock.GetUtcNow();
        var fresh = arriving
            .Select(entry => new TrailEntry(BrowsingKeys.Normalise(kind, entry.Key), entry.At > now ? now : entry.At))
            .Where(entry => entry.Key.Length > 0 && entry.At > now - Retention)
            .ToList();

        if (fresh.Count == 0)
            return 0;

        var kept = Of(kind)
            .Concat(fresh)
            .Where(entry => entry.At > now - Retention)
            // The latest time for each key, and the key as it was last written.
            .GroupBy(entry => entry.Key, BrowsingKeys.Comparer(kind))
            .Select(group => group.MaxBy(entry => entry.At)!)
            .OrderByDescending(entry => entry.At)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(CapOf(kind))
            .ToList();

        Replace(kind, kept);
        UpdatedAt = now;

        return kept.Count(fresh.Contains);
    }

    /// <summary>One entry removed. False when there was none to remove.</summary>
    public bool Forget(TimeProvider clock, BrowsingKind kind, string key)
    {
        var normalised = BrowsingKeys.Normalise(kind, key);
        var remaining = Of(kind).Where(entry => !BrowsingKeys.Comparer(kind).Equals(entry.Key, normalised)).ToList();

        if (remaining.Count == Of(kind).Count)
            return false;

        Replace(kind, remaining);
        UpdatedAt = clock.GetUtcNow();
        return true;
    }

    /// <summary>Everything removed; the switch stays where it was.</summary>
    public void Clear(TimeProvider clock)
    {
        this.views = [];
        this.searches = [];
        UpdatedAt = clock.GetUtcNow();
    }

    /// <summary>
    /// Switched off: nothing is recorded from now on, and what was kept is
    /// cleared in the same change — "off" that kept the past would be a hidden
    /// trail, which is worse than a visible one.
    /// </summary>
    public void SwitchOff(TimeProvider clock)
    {
        IsEnabled = false;
        Clear(clock);
    }

    /// <summary>Switched on again: recording starts from nothing, since nothing was kept while it was off.</summary>
    public void SwitchOn(TimeProvider clock)
    {
        if (IsEnabled)
            return;

        IsEnabled = true;
        UpdatedAt = clock.GetUtcNow();
    }

    private IReadOnlyList<TrailEntry> Of(BrowsingKind kind) => kind == BrowsingKind.View ? this.views : this.searches;

    private void Replace(BrowsingKind kind, List<TrailEntry> entries)
    {
        if (kind == BrowsingKind.View)
            this.views = entries;
        else
            this.searches = entries;
    }

    private static int CapOf(BrowsingKind kind) => kind == BrowsingKind.View ? MaxViews : MaxSearches;
}

/// <summary>
/// When two keys are the same entry. A code is the catalogue's, upper case, so
/// <c>b073wxyz01</c> is the product a lower-cased link named. A search is the
/// words with their spacing tidied, and the same words in another case are the
/// same search — the latest spelling is the one kept.
/// </summary>
public static class BrowsingKeys
{
    public static string Normalise(BrowsingKind kind, string key)
    {
        var trimmed = (key ?? string.Empty).Trim();

        return kind == BrowsingKind.View
            ? trimmed.ToUpperInvariant()
            : string.Join(' ', trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static StringComparer Comparer(BrowsingKind kind) =>
        kind == BrowsingKind.View ? StringComparer.Ordinal : StringComparer.InvariantCultureIgnoreCase;
}
