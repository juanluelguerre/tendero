using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Domain;

public enum VariantStatus
{
    Available,      // se puede comprar
    Discontinued    // se deja de vender, pero el histórico de pedidos la nombra
}

/// <summary>
/// Lo que un cliente compra de verdad: una talla concreta de un color concreto.
///
/// Es una entidad HIJA de <see cref="Product"/>, no un agregado: no tiene ciclo
/// de vida propio —no existe una variante sin producto— y su invariante (los
/// valores de eje son únicos dentro del producto) sólo se puede sostener desde
/// el padre.
///
/// El SKU es lo que cruza fronteras de contexto. Inventory guarda stock por SKU
/// y no por <see cref="VariantId"/>, que es lo que le permite no referenciar
/// Catalog en absoluto: el SKU es al stock lo que <c>ProductName</c> es a una
/// línea de pedido, vocabulario compartido en vez de una referencia viva.
/// </summary>
public sealed class Variant
{
    private readonly Dictionary<string, string> _axisValues = new(StringComparer.OrdinalIgnoreCase);

    public VariantId Id { get; private set; }

    /// <summary>Único en todo el catálogo. Es la clave con la que hablan
    /// inventario, carrito, pedidos y UCP.</summary>
    public string Sku { get; private set; } = default!;

    public Money Price { get; private set; }

    /// <summary>
    /// Qué la distingue de sus hermanas: código de atributo → código de opción,
    /// p. ej. <c>{"COLOR": "NAVY_BLUE", "SIZE": "38"}</c>. Códigos y no
    /// etiquetas: la etiqueta es texto de cara al usuario y por tanto
    /// <c>LocalizedText</c>, que vive en la definición del atributo (fase 2).
    /// </summary>
    public IReadOnlyDictionary<string, string> AxisValues => _axisValues;

    public string? TaxClass { get; private set; }

    /// <summary>Foto propia cuando la variante se ve distinta — el color la
    /// necesita, la talla no. Null significa "usa la del producto".</summary>
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
    /// Cómo se nombra en una línea de pedido: "NAVY_BLUE · 38". Se congela en el
    /// pedido (ADR 0002), así que reordenar los ejes después no reescribe
    /// históricos. El orden lo fija el producto, no el diccionario, porque el
    /// orden de un diccionario no es un dato.
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
