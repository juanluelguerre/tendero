using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Exports.Features.CatalogueForAnotherPlatform;

/// <summary>
/// One product as another platform imports it (E-3): in one culture, with what a
/// store's importer asks of a product, its variants and its pictures, in no one
/// platform's words. Shopify's CSV is the first to write it.
/// </summary>
/// <param name="Handle">The product's address at the other platform: its slug and its code, so two products of one name stay two.</param>
/// <param name="Description">Plain text, paragraphs split by a blank line.</param>
/// <param name="Type">The leaf of its category, as the shopkeeper names it.</param>
/// <param name="OnSale">On sale here; a Draft goes as a draft.</param>
/// <param name="OptionNames">What its variants vary by, in order; none for a product sold one way.</param>
/// <param name="Pictures">Its gallery, in order.</param>
/// <param name="GoogleProductCategory">Google's taxonomy id, from the mapping a person wrote, for a platform that keeps it.</param>
public sealed record ProductForAnotherPlatform(
    string Handle,
    string Title,
    string? Description,
    string? Vendor,
    string? Type,
    bool OnSale,
    IReadOnlyList<string> OptionNames,
    IReadOnlyList<VariantForAnotherPlatform> Variants,
    IReadOnlyList<PictureForAnotherPlatform> Pictures,
    int? GoogleProductCategory);

/// <param name="OptionValues">In the order of the product's option names.</param>
/// <param name="Inventory">The units that can still be sold, across the warehouses.</param>
/// <param name="PictureUrl">Its own first picture, when it has one of its own.</param>
public sealed record VariantForAnotherPlatform(
    string Sku, string? Barcode, Money Price, IReadOnlyList<string> OptionValues, int Inventory, string? PictureUrl);

public sealed record PictureForAnotherPlatform(string Url, string? Alt);
