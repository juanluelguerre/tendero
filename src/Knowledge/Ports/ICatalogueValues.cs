using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Ports;

/// <summary>
/// What Catalog holds now for a product's attributes, as values (ADR 0014): what
/// a claim waiting for review is put beside, so a claim that says one value where
/// the product holds another is seen before it is approved (phase 13a, I-4).
///
/// Every product still in the catalogue, a draft included — a claim is reviewed
/// before its product is on sale — where <see cref="IProductFacts"/> answers only
/// what a shopper can see.
/// </summary>
public interface ICatalogueValues
{
    /// <param name="culture">The language a held value is shown in: the reviewer's.</param>
    Task<IReadOnlyDictionary<ProductId, IReadOnlyList<HeldValue>>> OfAsync(
        IReadOnlyCollection<ProductId> products, string culture, CancellationToken cancellationToken = default);
}

/// <summary>One attribute's value as a product holds it.</summary>
/// <param name="Key">The value as one culture-free string — an option's code, a
/// number in invariant form, true or false — which is how a claim writes its own.</param>
/// <param name="Shown">The same value as a person reads it, in the culture asked for.</param>
public sealed record HeldValue(string AttributeCode, string Key, string Shown);

/// <summary>
/// Whether a claim's value and a held one are the same value. Each side wrote it
/// its own way — a model in one case, Catalog in another; `4` and `4.0` — so the
/// comparison is of what is meant: numbers as numbers, everything else as text
/// without regard to case or the space around it.
/// </summary>
public static class SameValue
{
    public static bool As(string claimed, string held)
    {
        var (left, right) = (claimed.Trim(), held.Trim());

        return decimal.TryParse(left, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var one)
               && decimal.TryParse(right, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var other)
            ? one == other
            : string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
