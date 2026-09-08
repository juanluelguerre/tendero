using ElGuerre.Tendero.Catalog.Features.GetProduct;
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

    public IReadOnlyList<Type> Slices => [typeof(GetProductQuery)];
}
