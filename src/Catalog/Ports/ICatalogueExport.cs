using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// The catalogue as it leaves the shop (phase 16, E-2; spec <c>docs/specs/exports.md</c>):
/// every product still in it, said in one culture, as values — never a
/// <c>Product</c>. A feed for Merchant Center and a CSV another platform imports
/// read it through <c>Exports</c>, an adapter project that may not reach a domain
/// (the rule <c>An_adapter_never_reaches_a_domain</c>), so the rendering a product
/// page already does — an option's label, a category's branch, a variant's
/// pictures — is done here, where it is known, and handed over finished.
/// </summary>
public interface ICatalogueExport
{
    /// <summary>
    /// Every live product — on sale and in Draft, which <see cref="ExportedProduct.OnSale"/>
    /// tells apart — by code, in <paramref name="culture"/> with the catalogue's
    /// fallback. A discontinued variant is left out: nothing anywhere may sell it.
    /// </summary>
    Task<IReadOnlyList<ExportedProduct>> ProductsAsync(string culture, CancellationToken cancellationToken = default);

    /// <summary>
    /// One live product by its code, said as <see cref="ProductsAsync"/> says it; null when it
    /// is archived or unknown. What a sales channel is told when that product changes
    /// (phase 16, S3).
    /// </summary>
    Task<ExportedProduct?> ProductAsync(string code, string culture, CancellationToken cancellationToken = default);
}

/// <param name="Code">The public code: what the storefront's address and a feed's group are made of (ADR 0026).</param>
/// <param name="OnSale">Published, as the storefront shows it; a Draft is false.</param>
/// <param name="Category">The branch, root first, each step named in the culture.</param>
/// <param name="Pictures">The gallery in its order.</param>
/// <param name="Attributes">The product's own values whose attribute the catalogue defines, rendered.</param>
public sealed record ExportedProduct(
    string Code,
    bool OnSale,
    string Name,
    string Slug,
    string? Description,
    string? Brand,
    IReadOnlyList<ExportedCategoryStep> Category,
    IReadOnlyList<ExportedPicture> Pictures,
    IReadOnlyList<ExportedAttribute> Attributes,
    IReadOnlyList<ExportedVariant> Variants);

public sealed record ExportedCategoryStep(string Code, string Name);

/// <param name="ImageId">The key the image store serves it by, at <c>/api/images/{id}</c>.</param>
public sealed record ExportedPicture(string ImageId, string? Alt);

/// <param name="Key">The value free of any culture — an option's code, a number in invariant form — for whoever maps it to another vocabulary.</param>
/// <param name="Value">The value as a shopper reads it in the culture; null for a yes or no.</param>
public sealed record ExportedAttribute(string Code, string Label, string Key, string? Value);

/// <param name="Options">What it varies by, in the product's order of axes.</param>
/// <param name="PictureIds">Its gallery: its colour's pictures, then the ones every variant shares.</param>
public sealed record ExportedVariant(
    string Sku, string? Barcode, Money Price, string? TaxClass, IReadOnlyList<ExportedOption> Options, IReadOnlyList<string> PictureIds);

/// <param name="Axis">The attribute's code: <c>COLOR</c>, <c>SIZE</c>.</param>
/// <param name="Code">The option's code, free of any culture.</param>
public sealed record ExportedOption(string Axis, string AxisLabel, string Code, string Label);
