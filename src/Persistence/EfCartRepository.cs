using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// The Cart aggregate over EF Core. Tracking, always: nothing loads a cart
/// except to change it.
/// </summary>
internal sealed class EfCartRepository(TenderoDbContext context) : ICartRepository
{
    public Task<Cart?> FindOpenByTokenAsync(string token, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(token)
            ? Task.FromResult<Cart?>(null)
            : context.Carts.FirstOrDefaultAsync(
                cart => cart.Token == token && cart.Status == CartStatus.Open, cancellationToken);

    public Task<Cart?> FindByIdAsync(CartId id, CancellationToken cancellationToken = default) =>
        context.Carts.FirstOrDefaultAsync(cart => cart.Id == id, cancellationToken);

    public void Add(Cart cart) => context.Carts.Add(cart);
}

/// <summary>
/// What the catalogue knows about a SKU, for the ordering context.
///
/// The sibling of <see cref="EfPricedItemReader"/>, and it lives here for the
/// same reason: `Ordering` must not reference `Catalog`, somebody has to bridge
/// the two, and this project is where every outbound adapter already lives.
///
/// The crossing carries **values** — ids, a SKU, and the two texts that get
/// frozen onto the line, in every culture. That is ADR 0002's snapshot rule made
/// into a type: what comes back cannot be held onto, edited, or accidentally
/// saved.
///
/// Only Active products are purchasable. A draft is something nobody has
/// approved for sale, and letting one into a cart would be buying through the
/// review queue — the same rule the priced-item reader states, for the same
/// reason.
/// </summary>
internal sealed class EfPurchasableReader(CatalogueLines lines) : IPurchasableReader
{
    public Task<IReadOnlyDictionary<string, PurchasableVariant>> FindBySkusAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default) =>
        lines.FindAsync(skus, onSaleOnly: true, cancellationToken);
}

/// <summary>
/// The catalogue's variants by SKU whatever their status (phase 16, S2b): what an
/// imported order's lines snapshot. A catalogue just imported is all in Draft, and
/// an order already placed is history, not a purchase.
/// </summary>
internal sealed class EfCatalogueVariants(CatalogueLines lines) : ICatalogueVariants
{
    public Task<IReadOnlyDictionary<string, PurchasableVariant>> FindBySkusAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default) =>
        lines.FindAsync(skus, onSaleOnly: false, cancellationToken);
}

/// <summary>
/// A variant as Ordering sees it, for both readers above and for the rehearsal's
/// (<see cref="RehearsalCatalogueVariants"/>): one mapping, so a product the
/// rehearsal would bring reads exactly as the stored one will.
/// </summary>
internal sealed class CatalogueLines(
    TenderoDbContext context, IAttributeDefinitionReader definitions, ICategoryReader categories)
{
    public async Task<IReadOnlyDictionary<string, PurchasableVariant>> FindAsync(
        IReadOnlyCollection<string> skus, bool onSaleOnly, CancellationToken cancellationToken)
    {
        if (skus.Count == 0)
            return new Dictionary<string, PurchasableVariant>(StringComparer.OrdinalIgnoreCase);

        var wanted = skus.Select(sku => sku.Trim()).ToArray();
        var products = await context.Products
            .AsNoTracking()
            .Where(product => (!onSaleOnly || product.Status == ProductStatus.Active)
                              && product.Variants.Any(variant => wanted.Contains(variant.Sku)))
            .ToListAsync(cancellationToken);

        return await OfAsync(products, wanted, cancellationToken);
    }

    /// <summary>The variants of these products whose SKU is wanted, keyed by SKU.</summary>
    public async Task<IReadOnlyDictionary<string, PurchasableVariant>> OfAsync(
        IEnumerable<Product> products, IReadOnlyCollection<string> wanted, CancellationToken cancellationToken)
    {
        var attributes = await definitions.AllAsync(cancellationToken);
        var tree = await categories.AllAsync(cancellationToken);

        return (
            from product in products
            from variant in product.Variants
            where wanted.Contains(variant.Sku, StringComparer.OrdinalIgnoreCase)
            select new PurchasableVariant(
                product.Id,
                variant.Id,
                variant.Sku,
                // Frozen by the caller in every culture, so retranslating the
                // catalogue does not rewrite somebody's basket — and switching
                // language does not leave it in the one it was filled in.
                product.Name,
                Label(variant, attributes),
                product.PictureOf(variant)?.Id.ToString(),
                variant.Price.Currency,
                // The order line snapshots it, as it does the name (R-9, D-7), and
                // its path, so a department's figure reaches everything under it.
                product.Category,
                product.Category is null ? null : tree.ByCode(product.Category)?.Path)
        ).ToDictionary(item => item.Sku, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// "Azul marino · 38" / "Navy blue · 38" — the axis values in the product's
    /// own axis order, in every culture the shop speaks.
    ///
    /// Null when there is nothing to say. Every product has at least one variant
    /// (the implicit `{externalId}-DEFAULT` minted at import), and printing an
    /// empty label under every single-variant product is noise.
    /// </summary>
    private static LocalizedText? Label(Variant variant, AttributeDefinitions attributes)
    {
        if (variant.AxisValues.Count == 0)
            return null;

        var labels = CultureNegotiation.Supported
            .Select(culture => (Culture: culture, Label: Label(variant, attributes, culture)))
            .Where(candidate => candidate.Label is not null)
            .ToDictionary(candidate => candidate.Culture, candidate => candidate.Label!);

        return labels.Count == 0 ? null : new LocalizedText(labels);
    }

    private static string? Label(Variant variant, AttributeDefinitions attributes, string culture)
    {

        // The axis values are CODES — {"COLOR": "NAVY_BLUE", "SIZE": "38"} — and
        // the definitions are what turn them into words. The same resolution the
        // search document does for `attributesText`, and for the same reason: a
        // code is not user-facing text, and phase 2 measured what happens when
        // one leaks into something a person reads.
        //
        // A code with no definition falls back to itself rather than
        // disappearing. Losing "38" because SIZE was never defined would make a
        // basket say less than it knows.
        var parts = variant.AxisValues
            .Select(pair => attributes.ByCode(pair.Key)?.LabelForOption(pair.Value, culture) ?? pair.Value)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
