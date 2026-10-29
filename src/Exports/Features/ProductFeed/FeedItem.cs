using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Exports.Features.ProductFeed;

/// <summary>
/// One row of a product feed: a variant on sale, said in a feed's terms rather
/// than any one engine's (E-2). Merchant Center's adapter is the first to write it;
/// Meta's catalogue reads the same fields under the same names.
/// </summary>
/// <param name="Id">The SKU: unique, stable, and the same in every culture's feed.</param>
/// <param name="ItemGroupId">The product's code, which groups its variants.</param>
/// <param name="Link">The product's page in the feed's culture.</param>
/// <param name="ImageLink">The variant's first picture, or the product's; null when it has none.</param>
/// <param name="AdditionalImageLinks">The rest of its gallery, in order.</param>
/// <param name="GoogleProductCategory">Google's taxonomy id for its category, from the mapping a person wrote; null when there is none.</param>
/// <param name="ProductType">The shop's own branch, root first, as Merchant Center writes a path.</param>
public sealed record FeedItem(
    string Id,
    string ItemGroupId,
    string Title,
    string Description,
    string Link,
    string? ImageLink,
    IReadOnlyList<string> AdditionalImageLinks,
    FeedAvailability Availability,
    Money Price,
    string? Brand,
    string? Gtin,
    int? GoogleProductCategory,
    string ProductType,
    string? Color,
    string? Size,
    FeedGender? Gender);

/// <summary>Whether a variant can be bought now, from the stock that can still be sold.</summary>
public enum FeedAvailability
{
    InStock,
    OutOfStock,
}

/// <summary>Who it is made for, when the catalogue says; Merchant Center's three values have no fourth.</summary>
public enum FeedGender
{
    Female,
    Male,
    Unisex,
}
