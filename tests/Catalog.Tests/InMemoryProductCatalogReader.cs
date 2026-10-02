using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// The catalogue's read side over a list, shared by every slice test that needs
/// one. It was nested inside <c>ListProductsTests</c> while it had one caller;
/// the product page made it two, and a fake copied is a fake that drifts.
/// </summary>
internal sealed class InMemoryProductCatalogReader(params Product[] products) : IProductCatalogReader
{
    public Task<ProductPage> ListAsync(
        ProductStatus? status, string? text, string? missingCulture, int page, int pageSize, CancellationToken ct)
    {
        var typed = text?.Trim() ?? "";
        var typedMatches = products
            .Where(p => typed.Length == 0
                || p.Code.Contains(typed, StringComparison.OrdinalIgnoreCase)
                || p.Name.Cultures.Any(culture => p.Name.In(culture).Contains(typed, StringComparison.OrdinalIgnoreCase)))
            .Where(p => missingCulture is not { Length: > 0 } culture || !p.Name.Cultures.Contains(Culture.Normalize(culture)))
            .ToList();
        var matching = typedMatches.Where(p => status is null || p.Status == status).ToList();

        return Task.FromResult(new ProductPage(
            [.. matching.Skip((page - 1) * pageSize).Take(pageSize)],
            matching.Count,
            typedMatches.GroupBy(p => p.Status).ToDictionary(group => group.Key, group => group.Count())));
    }

    public Task<IReadOnlyList<Product>> LiveAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Product>>([.. products.Where(p => p.Status != ProductStatus.Archived)]);

    public Task<IReadOnlyList<string>> SkusOfProductsMatchingAsync(string text, CancellationToken ct)
    {
        var typed = text.Trim();
        IReadOnlyList<string> skus = typed.Length == 0
            ? []
            : [.. products
                .Where(p => p.Code.Contains(typed, StringComparison.OrdinalIgnoreCase)
                    || p.Name.Cultures.Any(culture => p.Name.In(culture).Contains(typed, StringComparison.OrdinalIgnoreCase)))
                .SelectMany(p => p.Variants.Select(variant => variant.Sku))];
        return Task.FromResult(skus);
    }

    /// <summary>
    /// Plain equality, and there is nothing else to reproduce: a code is unique
    /// by constraint, so there is no tie-break, no culture and no fallback for a
    /// fake to get subtly wrong. That the double is this boring is the clearest
    /// evidence the key got simpler (ADR 0026) — the version of this that looked
    /// products up by slug had to mirror an ordering rule to stay honest.
    /// </summary>
    public Task<Product?> FindByCodeAsync(string code, CancellationToken ct) =>
        Task.FromResult(products.FirstOrDefault(product => product.Code == code));

    public Task<IReadOnlyList<SkuDescription>> DescribeSkusAsync(
        IReadOnlyCollection<string> skus, string culture, CancellationToken ct)
    {
        var wanted = skus.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A SKU the catalogue does not know is ABSENT, not empty. The fake has
        // to reproduce that, because it is the case the screen renders
        // differently — stock can outlive a product.
        IReadOnlyList<SkuDescription> found =
        [
            .. products.SelectMany(product => product.Variants
                .Where(variant => wanted.Contains(variant.Sku))
                .Select(variant => new SkuDescription(
                    variant.Sku,
                    product.Code,
                    product.Name.In(culture),
                    product.Slug.In(culture),
                    product.VariantAxes,
                    variant.AxisValues)))
        ];

        return Task.FromResult(found);
    }
}
