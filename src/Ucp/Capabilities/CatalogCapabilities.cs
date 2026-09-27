using ElGuerre.Tendero.Catalog.Features.GetProduct;
using ElGuerre.Tendero.Knowledge.Features.ProductSpecs;
using ElGuerre.Tendero.Search.Features.SearchProducts;

namespace ElGuerre.Tendero.Ucp.Capabilities;

/// <summary>
/// Search for products using query text and filters.
///
/// The specification's own words for this capability, and they describe what
/// `SearchProductsQuery` already does — including the filters, since the same
/// slice browses a category and sorts by newest.
/// </summary>
public sealed class CatalogSearchCapability : IUcpCapability
{
    public string Id => "dev.ucp.shopping.catalog.search";

    public string Version => UcpSpecification.Version;

    public string Spec => $"{UcpSpecification.BaseUrl}/catalog/";

    public string Schema => $"{UcpSpecification.SchemaBaseUrl}/shopping/catalog_search.json";

    public IReadOnlyList<Type> Slices => [typeof(SearchProductsQuery)];
}

/// <summary>
/// Retrieve products or variants by identifier.
///
/// `GetProductQuery` takes the product's public code and answers with every
/// variant and its SKU, which is both halves of what the specification asks for
/// — and the reason ADR 0026 put a code in the URL rather than a slug is
/// exactly the reason this capability is honest: an identifier an agent can
/// hold on to.
/// </summary>
public sealed class CatalogLookupCapability : IUcpCapability
{
    public string Id => "dev.ucp.shopping.catalog.lookup";

    public string Version => UcpSpecification.Version;

    public string Spec => $"{UcpSpecification.BaseUrl}/catalog/";

    public string Schema => $"{UcpSpecification.SchemaBaseUrl}/shopping/catalog_lookup.json";

    public IReadOnlyList<Type> Slices => [typeof(GetProductQuery)];
}

/// <summary>
/// The shop's own extension of UCP's catalogue (P11-8), and the project's
/// differentiator carried by the protocol's own mechanism: once negotiated, every
/// product a lookup or a product read answers carries <c>knowledge</c> — its specs,
/// each with its source, and for a value a person approved from the product's text,
/// the words it rests on and how well they support it.
///
/// Reverse-DNS under our own name, because UCP reserves <c>dev.ucp.*</c>. Its
/// schema is ours to publish, so it is served by this API rather than pointed at
/// ucp.dev; its version is ours too, the date its shape was settled.
/// </summary>
public sealed class KnowledgeCapability : IUcpCapability
{
    public const string Name = "com.elguerre.tendero.knowledge";

    public const string SchemaPath = "/ucp/schemas/com.elguerre.tendero.knowledge.json";

    public string Id => Name;

    public string Version => "2026-09-27";

    /// <summary>The schema's own descriptions are its specification: one document, served beside the code that keeps it.</summary>
    public string Spec => SchemaPath;

    public string Schema => SchemaPath;

    public string? Extends => "dev.ucp.shopping.catalog.lookup";

    public IReadOnlyList<Type> Slices => [typeof(ProductSpecsQuery)];
}
