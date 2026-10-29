using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Domain;

public enum VariantStatus
{
    Available,      // can be bought
    Discontinued    // no longer sold, but order history still names it
}

/// <summary>
/// What a customer actually buys: one specific size in one specific colour.
///
/// It is a CHILD entity of <see cref="Product"/>, not an aggregate: it has no
/// lifecycle of its own — a variant without a product does not exist — and its
/// invariant (axis values are unique within the product) can only be held from
/// the parent.
///
/// The SKU is what crosses context boundaries. Inventory keys stock on SKU and
/// not on <see cref="VariantId"/>, which is what lets it not reference Catalog
/// at all: the SKU is to stock what <c>ProductName</c> is to an order line,
/// shared vocabulary instead of a live reference.
/// </summary>
public sealed class Variant
{
    private readonly Dictionary<string, string> axisValues = new(StringComparer.OrdinalIgnoreCase);

    public VariantId Id { get; private set; }

    /// <summary>Unique across the whole catalogue. It is the key inventory, the
    /// cart, orders and UCP all speak.</summary>
    public string Sku { get; private set; } = default!;

    public Money Price { get; private set; }

    /// <summary>
    /// What tells it apart from its siblings: attribute code to option code, for
    /// example <c>{"COLOR": "NAVY_BLUE", "SIZE": "38"}</c>. Codes and not
    /// labels: a label is user-facing text and therefore <c>LocalizedText</c>,
    /// which lives on the attribute's definition (phase 2).
    /// </summary>
    public IReadOnlyDictionary<string, string> AxisValues => this.axisValues;

    public string? TaxClass { get; private set; }

    public VariantStatus Status { get; private set; }

    /// <summary>The barcode its source gives it (EAN, UPC), or null (phase 16).</summary>
    public string? Barcode { get; private set; }

    private readonly List<ExternalReference> externalReferences = [];

    /// <summary>
    /// Which variant each source knows this as (phase 16, the table <c>P1-2</c>
    /// deferred until a source had ids per variant). It is how a re-import finds
    /// it when the source has renamed its SKU — ours never changes, because every
    /// other context speaks it.
    /// </summary>
    public IReadOnlyList<ExternalReference> ExternalReferences => this.externalReferences;

    private Variant() { } // EF Core

    internal static Variant Create(
        string sku, Money price, IReadOnlyDictionary<string, string> axisValues,
        string? taxClass = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        if (price.Amount < 0)
            throw new InvalidOperationException("A variant price cannot be negative.");

        var variant = new Variant
        {
            Id = VariantId.New(),
            Sku = sku.Trim(),
            Price = price,
            TaxClass = taxClass,
            Status = VariantStatus.Available
        };

        foreach (var (axis, value) in axisValues)
            variant.axisValues[axis.Trim()] = value.Trim();

        return variant;
    }

    internal void SetPrice(Money price)
    {
        if (price.Amount < 0)
            throw new InvalidOperationException("A variant price cannot be negative.");
        Price = price;
    }

    internal void Discontinue() => Status = VariantStatus.Discontinued;

    internal bool IsKnownTo(string source, string externalId) =>
        this.externalReferences.Any(reference => reference.Is(source, externalId));

    internal bool LinkExternal(string source, string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        if (IsKnownTo(source, externalId))
            return false;

        this.externalReferences.Add(new ExternalReference(source.ToLowerInvariant(), externalId));
        return true;
    }

    internal bool SetTaxClass(string taxClass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taxClass);
        var trimmed = taxClass.Trim();
        if (TaxClass == trimmed)
            return false;

        TaxClass = trimmed;
        return true;
    }

    internal bool SetBarcode(string? barcode)
    {
        var trimmed = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        if (Barcode == trimmed)
            return false;

        Barcode = trimmed;
        return true;
    }

    /// <summary>
    /// How it is named on an order line: "NAVY_BLUE · 38". It is frozen onto the
    /// order (ADR 0002), so reordering the axes afterwards does not rewrite
    /// history. The product fixes the order, not the dictionary, because a
    /// dictionary's order is not data.
    /// </summary>
    public string LabelFor(IReadOnlyList<string> axisOrder) =>
        string.Join(
            " · ", axisOrder
                .Where(this.axisValues.ContainsKey)
                .Select(axis => this.axisValues[axis]));

    internal bool Matches(IReadOnlyDictionary<string, string> axisValues) =>
        this.axisValues.Count == axisValues.Count &&
        axisValues.All(pair =>
            this.axisValues.TryGetValue(pair.Key, out var value) &&
            string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase));
}
