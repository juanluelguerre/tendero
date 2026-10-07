using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Adapters;

/// <summary>
/// Catalog's answer to <see cref="IProductPictures"/>: every picture of every
/// product still in the catalogue, by product code and then by its place in the
/// gallery.
///
/// In memory over the live catalogue: a hundred products (the scale rule's
/// "deferred with its number"); the full catalogue makes it SQL.
/// </summary>
public sealed class ProductPicturesReader(IProductCatalogReader products) : IProductPictures
{
    public async Task<IReadOnlyList<ProductPicture>> AllAsync(CancellationToken cancellationToken = default) =>
    [
        .. (await products.LiveAsync(cancellationToken))
            .OrderBy(product => product.Code, StringComparer.Ordinal)
            .SelectMany(product => product.Images
                .OrderBy(image => image.SortOrder)
                .Select(image => new ProductPicture(product.Id.Value, product.Code, image.Id.Value))),
    ];
}
