using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Ports;

/// <summary>
/// What Catalog holds about the products a shopper is looking at, as values
/// (ADR 0014): their names and their attributes, rendered in one culture. The
/// specs table and the comparison (P10-7, P10-8) put Knowledge's quotes beside
/// these; Knowledge never reads a <c>Product</c>. Only products a shopper can
/// see are answered — a draft is not there, as on the product page.
/// </summary>
public interface IProductFacts
{
    Task<IReadOnlyList<ProductFacts>> OfAsync(IReadOnlyList<string> productCodes, string culture, CancellationToken cancellationToken = default);
}

/// <param name="Slug">The product's slug in the culture asked for: a comparison links to each page without a redirect (ADR 0026).</param>
public sealed record ProductFacts(ProductId ProductId, string Code, string Name, string Slug, IReadOnlyList<ProductFact> Attributes);

/// <summary>
/// One attribute as the product page renders it — the wording of a yes/no or a
/// missing value is the interface's, not the API's — and its culture-free
/// <see cref="Key"/>, which an applied claim is matched on.
/// </summary>
public sealed record ProductFact(string AttributeCode, string Label, FactKind Kind, string? Value, decimal? Number, string? Unit, bool? Flag, string Key);

/// <summary>
/// The kind of a fact's value, which says which of its fields carries it. The
/// same members as Catalog's <c>AttributeKind</c>, and deliberately not that
/// type: Knowledge references no other context (ADR 0014), so the adapter that
/// reads Catalog translates one into the other by name.
/// </summary>
public enum FactKind
{
    Text,
    LocalizedText,
    Number,
    Boolean,
    Option,
}
