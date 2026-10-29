using ElGuerre.Tendero.Catalog.Domain;

namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// What a rehearsal of the catalogue's import would bring (phase 16, S2b, P16-21):
/// the products it would create and the collections it would keep, held for the
/// request and nothing longer. A whole migration rehearsed in one request reads the
/// later steps against it — the stock's SKUs, an order's lines, a discount's
/// category — so each is judged against the catalogue the import would leave, not
/// the one before it. An import for real records nothing here: what it brings is
/// stored, and read from where it is stored.
/// </summary>
public interface IRehearsedCatalogue
{
    /// <summary>The products the rehearsal would create, as the import would build them.</summary>
    IReadOnlyList<Product> Products { get; }

    void Bring(Product product);

    void Keep(string collectionHandle, string categoryCode);

    /// <summary>The category the rehearsal would keep a collection as, or null. Handles are lowercase.</summary>
    string? CategoryOf(string collectionHandle);
}
