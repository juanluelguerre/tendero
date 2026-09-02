using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Pricing.Domain;

/// <summary>
/// What a SKU costs in one particular list.
///
/// **The key is the SKU and not the <see cref="VariantId"/>**, which is the same
/// call the search golden set already made: the internal id is a GUID v7 minted
/// on every import, so a price list keyed on it would expire each time the
/// catalogue is reimported. A SKU is stable, readable in a diff, and it is also
/// the vocabulary Inventory will use for the same article without sharing an
/// entity with anyone.
/// </summary>
public sealed record PriceListEntry(string Sku, Money Price);

/// <summary>
/// A price list for a customer segment: <c>retail</c> and <c>vip</c> at
/// laboratory depth.
///
/// The validity window is part of the data rather than a separate lookup because
/// a dated price is half of the sale problem; the other half is the promotions,
/// and both are evaluated against the same instant.
/// </summary>
public sealed class PriceList
{
    public PriceList(
        string code,
        string segment,
        string currency,
        int priority,
        IReadOnlyList<PriceListEntry> entries,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validTo = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        Code = code.Trim().ToLowerInvariant();
        Segment = segment.Trim().ToLowerInvariant();
        Currency = currency.Trim().ToUpperInvariant();
        Priority = priority;
        ValidFrom = validFrom;
        ValidTo = validTo;

        _entries = entries.ToDictionary(entry => entry.Sku, StringComparer.OrdinalIgnoreCase);
        Entries = entries;
    }

    private readonly Dictionary<string, PriceListEntry> _entries;

    public string Code { get; }

    /// <summary>Who it serves. <c>retail</c> is everybody's default segment,
    /// a guest included.</summary>
    public string Segment { get; }

    public string Currency { get; }

    /// <summary>Lower wins. With two lists serving one segment the lower
    /// priority decides — deterministic and without ties, because the code
    /// breaks them.</summary>
    public int Priority { get; }

    public DateTimeOffset? ValidFrom { get; }
    public DateTimeOffset? ValidTo { get; }

    public IReadOnlyList<PriceListEntry> Entries { get; }

    public bool IsActiveAt(DateTimeOffset at) =>
        (ValidFrom is null || at >= ValidFrom) && (ValidTo is null || at < ValidTo);

    public bool Serves(string segment) =>
        string.Equals(Segment, segment, StringComparison.OrdinalIgnoreCase);

    public Money? PriceFor(string sku) =>
        _entries.TryGetValue(sku, out var entry) ? entry.Price : null;
}

/// <summary>
/// Every list, at once. It is the value the resolver takes, and that is why it
/// exists: without it, resolving a price would be one query per line and the
/// resolver would stop being a pure function.
/// </summary>
public sealed class PriceBook(IReadOnlyList<PriceList> lists)
{
    public static readonly PriceBook Empty = new([]);

    public IReadOnlyList<PriceList> Lists { get; } = lists;

    /// <summary>
    /// The lists that apply to a segment at an instant, in deciding order. The
    /// tiebreak on code is what makes the answer reproducible, which is a
    /// requirement for a quote that gets frozen and recomputed later.
    /// </summary>
    public IEnumerable<PriceList> ApplicableTo(string segment, DateTimeOffset at) =>
        Lists.Where(list => list.Serves(segment) && list.IsActiveAt(at))
             .OrderBy(list => list.Priority)
             .ThenBy(list => list.Code, StringComparer.Ordinal);

    /// <summary>
    /// A fingerprint of the content, so a quote expires when the prices change
    /// and not only when time passes. Without it, a tariff edit would leave live
    /// quotes promising a price that no longer exists.
    /// </summary>
    public string Fingerprint() =>
        Fingerprints.Of(Lists
            .OrderBy(list => list.Code, StringComparer.Ordinal)
            .Select(list => string.Join('|',
                list.Code, list.Segment, list.Currency, list.Priority,
                list.ValidFrom?.ToUnixTimeSeconds(), list.ValidTo?.ToUnixTimeSeconds(),
                string.Join(',', list.Entries
                    .OrderBy(entry => entry.Sku, StringComparer.Ordinal)
                    .Select(entry => $"{entry.Sku}={entry.Price.Amount}{entry.Price.Currency}")))));
}
