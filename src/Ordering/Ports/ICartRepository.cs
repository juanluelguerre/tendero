using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Ports;

/// <summary>
/// Access to the Cart aggregate.
///
/// **A cart is found by its token, not by its id**, and that asymmetry is the
/// whole security model: the id is how the system names it, the token is how an
/// anonymous visitor proves it is theirs. Offering a lookup by id to an
/// anonymous endpoint would make every cart readable by anybody who could
/// enumerate a GUID v7 — which is to say, by anybody.
/// </summary>
public interface ICartRepository
{
    /// <summary>
    /// The open cart this token addresses, or null.
    ///
    /// Only an OPEN cart comes back. A token whose cart was checked out
    /// yesterday is not an error and not a cart: it is a browser holding a stale
    /// string, and the answer is a new cart rather than the old order's contents.
    /// </summary>
    Task<Cart?> FindOpenByTokenAsync(string token, CancellationToken cancellationToken = default);

    Task<Cart?> FindByIdAsync(CartId id, CancellationToken cancellationToken = default);

    void Add(Cart cart);
}

/// <summary>
/// What a product looks like to `Ordering` when a line is being added.
///
/// It is a VALUE, and `Ordering` never sees a `Product` or a `Variant`. The
/// crossing carries the SKU, the ids, and the two strings that get frozen onto
/// the line — which is ADR 0002's snapshot rule stated as a type rather than as
/// a convention.
/// </summary>
public sealed record PurchasableVariant(
    ProductId ProductId,
    VariantId VariantId,
    string Sku,
    string ProductName,
    string? VariantLabel,
    string? ImageId,
    string Currency);

/// <summary>
/// Resolves a SKU into something that can be put in a basket.
///
/// The port exists so `Ordering` can add a line without referencing `Catalog`,
/// exactly as `IStockLedger` lets it hold stock without referencing `Inventory`.
/// Persistence implements it, which is the same division `Pricing` already makes
/// with its own catalogue reader.
///
/// It takes a culture because the name it freezes is the name the shopper saw.
/// </summary>
public interface IPurchasableReader
{
    /// <summary>
    /// The ones that exist, keyed by SKU. Missing SKUs are simply absent, so the
    /// caller can name them rather than discovering the gap by counting — the
    /// same shape `IAvailabilityReader` uses, and for the same reason.
    /// </summary>
    Task<IReadOnlyDictionary<string, PurchasableVariant>> FindBySkusAsync(
        IReadOnlyCollection<string> skus, string culture, CancellationToken cancellationToken = default);
}
