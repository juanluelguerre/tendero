using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Persistence;

// The readers a migration's later steps use, each answering from what is stored
// and from what a rehearsal in the same request would bring (phase 16, S2b,
// P16-21). A whole migration rehearsed in one request then judges the stock, the
// orders and the discounts against the shop the catalogue's and the customers'
// imports would leave, which is the migration minus its writes; anywhere else the
// rehearsal holds nothing and each reader answers exactly as the stored one does.

/// <summary>
/// A host that composes Persistence without the catalogue or the accounts (the
/// workers) runs no import, so it rehearses nothing: the readers above it answer from
/// what is stored. Recording into it is a composition mistake, and says so.
/// </summary>
internal sealed class NothingRehearsed : IRehearsedCatalogue, IRehearsedCustomers
{
    public static readonly NothingRehearsed Instance = new();

    public IReadOnlyList<Catalog.Domain.Product> Products => [];

    public void Bring(Catalog.Domain.Product product) => throw Unregistered();

    public void Keep(string collectionHandle, string categoryCode) => throw Unregistered();

    public string? CategoryOf(string collectionHandle) => null;

    public void Bring(Accounts.Domain.CustomerOrigin origin) => throw Unregistered();

    public IReadOnlyDictionary<string, CustomerId> Of(string source) => new Dictionary<string, CustomerId>();

    private static InvalidOperationException Unregistered() =>
        new("A rehearsal was recorded in a host that registers no rehearsal store: compose AddCatalog and AddAccounts.");
}

/// <summary>The SKU of a source's variant, among the products stored and the ones a rehearsal would create.</summary>
internal sealed class RehearsalSourceVariantSkus(EfProductRepository stored, IRehearsedCatalogue rehearsed) : ISourceVariantSkus
{
    public async Task<IReadOnlyDictionary<string, string>> SkusOfAsync(
        string source, IReadOnlyCollection<string> externalVariantIds, CancellationToken ct)
    {
        var found = await stored.SkusOfAsync(source, externalVariantIds, ct);
        if (rehearsed.Products.Count == 0)
            return found;

        var normalised = source.ToLowerInvariant();
        var all = new Dictionary<string, string>(found, StringComparer.Ordinal);
        foreach (var variant in rehearsed.Products.SelectMany(product => product.Variants))
        {
            foreach (var reference in variant.ExternalReferences.Where(reference =>
                         reference.Source == normalised && externalVariantIds.Contains(reference.ExternalId)))
                all.TryAdd(reference.ExternalId, variant.Sku);
        }

        return all;
    }
}

/// <summary>A collection's category as a rehearsal would keep it, else as it is kept; a product's address as stored.</summary>
internal sealed class RehearsalSourceAddresses(EfSourceAddresses stored, IRehearsedCatalogue rehearsed) : ISourceAddresses
{
    public Task<ProductAddress?> ProductAsync(string handle, CancellationToken ct) => stored.ProductAsync(handle, ct);

    // The rehearsal's first: the import would point the collection at it, replacing what is kept.
    public async Task<string?> CategoryAsync(string collectionHandle, CancellationToken ct) =>
        rehearsed.CategoryOf(collectionHandle) ?? await stored.CategoryAsync(collectionHandle, ct);
}

/// <summary>The catalogue's variants by SKU, stored or a rehearsal would create, mapped the one same way.</summary>
internal sealed class RehearsalCatalogueVariants(EfCatalogueVariants stored, CatalogueLines lines, IRehearsedCatalogue rehearsed) : ICatalogueVariants
{
    public async Task<IReadOnlyDictionary<string, PurchasableVariant>> FindBySkusAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default)
    {
        var found = await stored.FindBySkusAsync(skus, cancellationToken);
        var missing = skus.Select(sku => sku.Trim()).Where(sku => !found.ContainsKey(sku)).ToArray();
        if (missing.Length == 0 || rehearsed.Products.Count == 0)
            return found;

        var brought = await lines.OfAsync(
            rehearsed.Products.Where(product => product.Variants.Any(variant => missing.Contains(variant.Sku, StringComparer.OrdinalIgnoreCase))),
            missing,
            cancellationToken);

        var all = new Dictionary<string, PurchasableVariant>(found, StringComparer.OrdinalIgnoreCase);
        foreach (var (sku, variant) in brought)
            all.TryAdd(sku, variant);
        return all;
    }
}

/// <summary>The customers imported from a source, stored or a rehearsal would create.</summary>
internal sealed class RehearsalImportedCustomerIds(EfImportedCustomerIds stored, IRehearsedCustomers rehearsed) : IImportedCustomerIds
{
    public async Task<IReadOnlyDictionary<string, CustomerId>> ForAsync(
        string source, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken = default)
    {
        var found = await stored.ForAsync(source, externalIds, cancellationToken);
        var brought = rehearsed.Of(source);
        if (brought.Count == 0)
            return found;

        var all = new Dictionary<string, CustomerId>(found, StringComparer.Ordinal);
        foreach (var id in externalIds.Where(brought.ContainsKey))
            all.TryAdd(id, brought[id]);
        return all;
    }
}
