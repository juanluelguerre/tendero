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
    private readonly Dictionary<string, string> _axisValues = new(StringComparer.OrdinalIgnoreCase);

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
    public IReadOnlyDictionary<string, string> AxisValues => _axisValues;

    public string? TaxClass { get; private set; }

    /// <summary>Its own photo when the variant looks different — colour needs
    /// one, size does not. Null means "use the product's".</summary>
    public ImageId? Image { get; private set; }

    public VariantStatus Status { get; private set; }

    private Variant() { } // EF Core

    internal static Variant Create(
        string sku, Money price, IReadOnlyDictionary<string, string> axisValues,
        string? taxClass = null, ImageId? image = null)
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
            Image = image,
            Status = VariantStatus.Available
        };

        foreach (var (axis, value) in axisValues)
            variant._axisValues[axis.Trim()] = value.Trim();

        return variant;
    }

    internal void SetPrice(Money price)
    {
        if (price.Amount < 0)
            throw new InvalidOperationException("A variant price cannot be negative.");
        Price = price;
    }

    internal void Discontinue() => Status = VariantStatus.Discontinued;

    internal void SetImage(ImageId? image) => Image = image;

    /// <summary>
    /// How it is named on an order line: "NAVY_BLUE · 38". It is frozen onto the
    /// order (ADR 0002), so reordering the axes afterwards does not rewrite
    /// history. The product fixes the order, not the dictionary, because a
    /// dictionary's order is not data.
    /// </summary>
    public string LabelFor(IReadOnlyList<string> axisOrder) =>
        string.Join(" · ", axisOrder
            .Where(_axisValues.ContainsKey)
            .Select(axis => _axisValues[axis]));

    internal bool Matches(IReadOnlyDictionary<string, string> axisValues) =>
        _axisValues.Count == axisValues.Count &&
        axisValues.All(pair =>
            _axisValues.TryGetValue(pair.Key, out var value) &&
            string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase));
}
